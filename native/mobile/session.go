// Package mobile exposes the go-nx controlling core to Android through
// gomobile bind. It deliberately contains no Android-specific code: Kotlin owns
// the Activity, rendering surface, IME and notification lifecycle, while this
// package owns QUIC, certificate pinning, tile decoding and input encoding.
package mobile

import (
	"context"
	"errors"
	"fmt"
	"net"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"github.com/quic-go/quic-go"

	"github.com/orangepro2001/nightdesk/native/internal/proto"
	"github.com/orangepro2001/nightdesk/native/internal/remote"
	"github.com/orangepro2001/nightdesk/native/internal/tile"
	"github.com/orangepro2001/nightdesk/native/internal/trust"
)

// Session states passed to Callback.OnState.
const (
	StateConnecting = "connecting"
	StateStreaming  = "streaming"
	StateClosed     = "closed"
)

// Receipt statuses passed to Callback.OnReceipt.
const (
	ReceiptExecuted = int(proto.StatusExecuted)
	ReceiptFailed   = int(proto.StatusFailed)
)

// Callback receives session events from Go worker goroutines. An Android
// implementation must hand UI work to the main thread and must never retain the
// memory passed to OnTile: pixels is only valid until that method returns.
//
// One update is OnFrameBegin followed by one OnTile call per dirty rectangle.
// Each tile is a tightly packed BGRA rectangle copied from Go's persistent
// frame. Tile updates are incremental: only changed areas arrive after the
// first full frame.
type Callback interface {
	OnState(state string)
	OnFrameBegin(width, height int)
	OnTile(x, y, w, h int, pixels []byte)
	OnReceipt(opID int64, status int)
	OnError(code, message string)
}

// Session is one pinned, authenticated go-nx viewer connection.
type Session struct {
	client *remote.Client
	cb     Callback

	ctx    context.Context
	cancel context.CancelFunc
	done   chan struct{}

	intentional atomic.Bool
	streamed    atomic.Bool
	closeOnce   sync.Once
	nextOp      atomic.Uint64

	inputMu sync.Mutex
	buttons map[int32]struct{}
	keys    map[int32]struct{}
	mouseX  int32
	mouseY  int32
	closed  bool

	sizeMu sync.RWMutex
	width  int
	height int

	pixelBuf []byte
}

// NewSession creates or loads this Android device's persistent client identity,
// verifies the host certificate against fingerprint, sends pairingCode as the
// Hello password, and starts the frame loop. It blocks only for the QUIC
// handshake and should be called from a worker thread.
func NewSession(storageDir, hostAddr, fingerprint, pairingCode string, callback Callback) (*Session, error) {
	if callback == nil {
		return nil, errors.New("mobile: callback is required")
	}
	if strings.TrimSpace(storageDir) == "" {
		return nil, errors.New("mobile: storage directory is required")
	}
	if _, _, err := net.SplitHostPort(hostAddr); err != nil {
		return nil, fmt.Errorf("mobile: invalid host address: %w", err)
	}
	normalized, err := trust.NormalizeFingerprint(fingerprint)
	if err != nil {
		return nil, fmt.Errorf("mobile: %w", err)
	}
	if len(pairingCode) > 255 {
		return nil, errors.New("mobile: pairing code is too long")
	}

	safeState(callback, StateConnecting)
	id, err := trust.Identity(storageDir)
	if err != nil {
		safeError(callback, "identity", "cannot load client identity")
		return nil, err
	}
	dialCtx, cancelDial := context.WithTimeout(context.Background(), 15*time.Second)
	client, err := remote.DialPinned(dialCtx, hostAddr, id, normalized, pairingCode)
	cancelDial()
	if err != nil {
		code := "disconnected"
		if errors.Is(err, remote.ErrHostChanged) || strings.Contains(err.Error(), remote.ErrHostChanged.Error()) {
			code = "host-identity"
		}
		safeError(callback, code, err.Error())
		safeState(callback, StateClosed)
		return nil, err
	}

	ctx, cancel := context.WithCancel(context.Background())
	s := &Session{
		client: client,
		cb:     callback,
		ctx:    ctx, cancel: cancel,
		done:    make(chan struct{}),
		buttons: make(map[int32]struct{}),
		keys:    make(map[int32]struct{}),
	}
	go s.run()
	return s, nil
}

func (s *Session) run() {
	defer close(s.done)
	err := s.client.Run(s.ctx, s.deliverFrame, func(opID uint64, status byte) {
		safeCall(func() { s.cb.OnReceipt(int64(opID), int(status)) })
	})
	if !s.intentional.Load() {
		code, message := classify(err)
		safeError(s.cb, code, message)
	}
	safeState(s.cb, StateClosed)
}

func classify(err error) (string, string) {
	if err == nil {
		return "disconnected", "session ended"
	}
	var ae *quic.ApplicationError
	if errors.As(err, &ae) {
		switch ae.ErrorCode {
		case remote.CloseUntrusted:
			return "untrusted", "host does not trust this device; pair it again"
		case remote.CloseBadPassword:
			return "bad-code", "pairing code is wrong or expired"
		case remote.CloseReplaced:
			return "replaced", "another viewer took over this session"
		case remote.CloseLocked:
			return "locked", "too many wrong pairing attempts; try again later"
		}
	}
	if errors.Is(err, context.Canceled) {
		return "disconnected", "session closed"
	}
	return "disconnected", err.Error()
}

// Close releases held keys and mouse buttons, then closes the QUIC session. It
// is safe to call more than once and is intentionally non-blocking.
func (s *Session) Close() {
	if s == nil {
		return
	}
	s.closeOnce.Do(func() {
		s.intentional.Store(true)
		s.ReleaseAll()
		s.cancel()
		s.client.Close()
	})
}

// ReleaseAll sends key-up and mouse-up events for every input this session
// believes is held. Call it when the remote view loses focus or before closing.
func (s *Session) ReleaseAll() error {
	if s == nil {
		return nil
	}
	s.inputMu.Lock()
	defer s.inputMu.Unlock()
	var errs []error
	for key := range s.keys {
		if err := s.client.SendInput(proto.Input{Kind: proto.KindKeyUp, Arg: key}); err != nil {
			errs = append(errs, err)
		}
		delete(s.keys, key)
	}
	for button := range s.buttons {
		if err := s.client.SendInput(proto.Input{Kind: proto.KindMouseUp, X: s.mouseX, Y: s.mouseY, Arg: button}); err != nil {
			errs = append(errs, err)
		}
		delete(s.buttons, button)
	}
	return errors.Join(errs...)
}

// Width and Height report the latest host frame size, or zero before the first
// frame. Android uses them to map touch coordinates into host pixels.
func (s *Session) Width() int {
	s.sizeMu.RLock()
	defer s.sizeMu.RUnlock()
	return s.width
}

func (s *Session) Height() int {
	s.sizeMu.RLock()
	defer s.sizeMu.RUnlock()
	return s.height
}

// Move sends an absolute cursor position in host pixels.
func (s *Session) Move(x, y int) error {
	s.inputMu.Lock()
	defer s.inputMu.Unlock()
	in := proto.Input{Kind: proto.KindMouseMove, X: int32(x), Y: int32(y)}
	if err := s.client.SendInput(in); err != nil {
		return err
	}
	s.mouseX, s.mouseY = in.X, in.Y
	return nil
}

// MouseDown and MouseUp use button 0=left, 1=right, 2=middle.
func (s *Session) MouseDown(button int) error { return s.mouse(button, true) }
func (s *Session) MouseUp(button int) error   { return s.mouse(button, false) }

func (s *Session) mouse(button int, down bool) error {
	if button < 0 || button > 2 {
		return errors.New("mobile: mouse button must be 0, 1 or 2")
	}
	s.inputMu.Lock()
	defer s.inputMu.Unlock()
	kind := proto.KindMouseUp
	if down {
		kind = proto.KindMouseDown
	}
	if err := s.client.SendInput(proto.Input{Kind: kind, X: s.mouseX, Y: s.mouseY, Arg: int32(button)}); err != nil {
		return err
	}
	if down {
		s.buttons[int32(button)] = struct{}{}
	} else {
		delete(s.buttons, int32(button))
	}
	return nil
}

// Wheel sends a vertical wheel delta in Windows 1/120-notch units.
func (s *Session) Wheel(delta int) error {
	s.inputMu.Lock()
	defer s.inputMu.Unlock()
	return s.client.SendInput(proto.Input{Kind: proto.KindWheel, X: s.mouseX, Y: s.mouseY, Arg: int32(delta)})
}

// KeyDown and KeyUp accept a Windows virtual-key code. Values 0x100..0x1ff
// represent the corresponding extended key (for example 0x100|0x2d is Insert).
func (s *Session) KeyDown(vk int) error { return s.key(vk, true) }
func (s *Session) KeyUp(vk int) error   { return s.key(vk, false) }

func (s *Session) key(vk int, down bool) error {
	if vk < 1 || vk > 0x1ff {
		return errors.New("mobile: virtual-key code out of range")
	}
	s.inputMu.Lock()
	defer s.inputMu.Unlock()
	kind := proto.KindKeyUp
	if down {
		kind = proto.KindKeyDown
	}
	if err := s.client.SendInput(proto.Input{Kind: kind, Arg: int32(vk)}); err != nil {
		return err
	}
	if down {
		s.keys[int32(vk)] = struct{}{}
	} else {
		delete(s.keys, int32(vk))
	}
	return nil
}

// Text sends literal UTF-8 text using MsgText, bypassing the host keyboard
// layout. The host reports delivery through OnReceipt.
func (s *Session) Text(text string) error {
	if len(text) == 0 {
		return nil
	}
	if len(text) > proto.MaxTextLen {
		return errors.New("mobile: text exceeds 1024 UTF-8 bytes")
	}
	opID := s.nextOp.Add(1)
	if err := s.client.SendTextOp(opID, text); err != nil {
		return err
	}
	return nil
}

func (s *Session) deliverFrame(frame *tile.Frame, rects []tile.Rect) {
	s.sizeMu.Lock()
	s.width, s.height = frame.W, frame.H
	s.sizeMu.Unlock()
	safeCall(func() { s.cb.OnFrameBegin(frame.W, frame.H) })
	for _, r := range rects {
		s.pixelBuf = s.pixelBuf[:0]
		for y := 0; y < r.H; y++ {
			off := ((r.Y+y)*frame.W + r.X) * 4
			s.pixelBuf = append(s.pixelBuf, frame.Pix[off:off+r.W*4]...)
		}
		pixels := s.pixelBuf
		safeCall(func() { s.cb.OnTile(r.X, r.Y, r.W, r.H, pixels) })
	}
	if s.streamed.CompareAndSwap(false, true) {
		safeState(s.cb, StateStreaming)
	}
}

func safeState(cb Callback, state string) {
	safeCall(func() { cb.OnState(state) })
}

func safeError(cb Callback, code, message string) {
	safeCall(func() { cb.OnError(code, message) })
}

func safeCall(fn func()) {
	defer func() { _ = recover() }()
	fn()
}
