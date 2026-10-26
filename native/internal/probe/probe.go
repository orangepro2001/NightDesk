// Package probe connects to a go-nx host with the same pinned TLS, pairing and
// tile-decoding path the Android client uses, then verifies frames and input
// receipts. It is used as an acceptance tool against a real host.
package probe

import (
	"context"
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"fmt"
	"os"
	"sync"
	"time"

	"github.com/orangepro2001/nightdesk/native/internal/proto"
	"github.com/orangepro2001/nightdesk/native/internal/remote"
	"github.com/orangepro2001/nightdesk/native/internal/tile"
	"github.com/orangepro2001/nightdesk/native/internal/trust"
)

// Config describes one acceptance run.
type Config struct {
	Addr        string
	Fingerprint string
	Code        string
	StateDir    string
	Frames      int           // frames to receive; default 3
	Timeout     time.Duration // overall deadline; default 30s
	DialTimeout time.Duration // handshake deadline; default 15s
	SendInput   bool          // send one of each input kind plus a text commit
	Text        string        // text used when SendInput is true
}

// Result is the machine-readable outcome of a run.
type Result struct {
	Addr            string `json:"addr"`
	Connected       bool   `json:"connected"`
	Width           int    `json:"width"`
	Height          int    `json:"height"`
	FirstFrameMS    int64  `json:"firstFrameMs"`
	Frames          int    `json:"frames"`
	Tiles           int    `json:"tiles"`
	DigestFirst     string `json:"digestFirst"`
	DigestLast      string `json:"digestLast"`
	InputsSent      int    `json:"inputsSent"`
	Receipts        int    `json:"receipts"`
	ReceiptFailures int    `json:"receiptFailures"`
	DurationMS      int64  `json:"durationMs"`
	Error           string `json:"error,omitempty"`
}

// TestInputs is the number of receipts expected when SendInput is enabled.
const TestInputs = 7

func (c Config) withDefaults() Config {
	if c.Frames <= 0 {
		c.Frames = 3
	}
	if c.Timeout <= 0 {
		c.Timeout = 30 * time.Second
	}
	if c.DialTimeout <= 0 {
		c.DialTimeout = 15 * time.Second
	}
	if c.Text == "" {
		c.Text = "NightDesk probe 你好"
	}
	return c
}

type state struct {
	mu              sync.Mutex
	frames          int
	tiles           int
	width, height   int
	firstFrameMS    int64
	digests         []string
	receipts        map[uint64]byte
	receiptFailures int
}

func (s *state) snapshot() (frames, tiles int, receipts, failures int, width, height int, first int64) {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.frames, s.tiles, len(s.receipts), s.receiptFailures, s.width, s.height, s.firstFrameMS
}

func (s *state) digest(i int) string {
	s.mu.Lock()
	defer s.mu.Unlock()
	if i < 0 || i >= len(s.digests) {
		return ""
	}
	return s.digests[i]
}

// Run connects, receives frames and optionally exercises every input kind.
func Run(ctx context.Context, cfg Config) (Result, error) {
	cfg = cfg.withDefaults()
	start := time.Now()
	res := Result{Addr: cfg.Addr}

	dir := cfg.StateDir
	if dir == "" {
		tmp, err := os.MkdirTemp("", "nx-probe-")
		if err != nil {
			return res, err
		}
		defer os.RemoveAll(tmp)
		dir = tmp
	}
	id, err := trust.Identity(dir)
	if err != nil {
		return res, fmt.Errorf("probe: identity: %w", err)
	}

	dialCtx, cancelDial := context.WithTimeout(ctx, cfg.DialTimeout)
	client, err := remote.DialPinned(dialCtx, cfg.Addr, id, cfg.Fingerprint, cfg.Code)
	cancelDial()
	if err != nil {
		res.Error = err.Error()
		res.DurationMS = time.Since(start).Milliseconds()
		return res, fmt.Errorf("probe: connect to %s: %w", cfg.Addr, err)
	}
	defer client.Close()
	res.Connected = true

	runCtx, cancelRun := context.WithCancel(ctx)
	defer cancelRun()

	st := &state{receipts: map[uint64]byte{}}
	first := make(chan struct{}, 1)
	progress := make(chan struct{}, 1)
	done := make(chan error, 1)
	go func() {
		done <- client.Run(runCtx, func(f *tile.Frame, rects []tile.Rect) {
			sum := sha256.Sum256(f.Pix)
			st.mu.Lock()
			st.frames++
			st.tiles += len(rects)
			if st.frames == 1 {
				st.width, st.height = f.W, f.H
				st.firstFrameMS = time.Since(start).Milliseconds()
			}
			st.digests = append(st.digests, hex.EncodeToString(sum[:]))
			st.mu.Unlock()
			select {
			case first <- struct{}{}:
			default:
			}
			select {
			case progress <- struct{}{}:
			default:
			}
		}, func(opID uint64, status byte) {
			st.mu.Lock()
			st.receipts[opID] = status
			if status != proto.StatusExecuted {
				st.receiptFailures++
			}
			st.mu.Unlock()
			select {
			case progress <- struct{}{}:
			default:
			}
		})
	}()

	res.InputsSent = 0
	expected := 0
	var sendErr error
	if cfg.SendInput {
		inputTimer := time.NewTimer(cfg.Timeout)
		defer inputTimer.Stop()
		select {
		case <-first:
			res.InputsSent, sendErr = sendInputs(client, cfg.Text)
			expected = TestInputs
		case err := <-done:
			res.Error = fmt.Sprintf("session ended before the first frame: %v", err)
			res.DurationMS = time.Since(start).Milliseconds()
			return res, errors.New(res.Error)
		case <-inputTimer.C:
			res.Error = "no frame before the input deadline"
			res.DurationMS = time.Since(start).Milliseconds()
			return res, errors.New(res.Error)
		}
	}

	deadline := time.NewTimer(cfg.Timeout)
	defer deadline.Stop()
	var sessionErr error
loop:
	for {
		frames, _, receipts, failures, _, _, _ := st.snapshot()
		if frames >= cfg.Frames && receipts >= expected {
			break
		}
		select {
		case <-progress:
		case <-deadline.C:
			res.Error = fmt.Sprintf("timeout: frames=%d receipts=%d failures=%d", frames, receipts, failures)
			break loop
		case err := <-done:
			sessionErr = err
			res.Error = fmt.Sprintf("session ended: %v", err)
			break loop
		}
	}

	frames, tiles, receipts, failures, width, height, firstMS := st.snapshot()
	res.Frames = frames
	res.Tiles = tiles
	res.Width, res.Height = width, height
	res.FirstFrameMS = firstMS
	res.Receipts = receipts
	res.ReceiptFailures = failures
	res.DigestFirst = st.digest(0)
	res.DigestLast = st.digest(frames - 1)
	res.DurationMS = time.Since(start).Milliseconds()

	cancelRun()
	client.Close()
	select {
	case <-done:
	case <-time.After(2 * time.Second):
	}

	switch {
	case sendErr != nil:
		return res, fmt.Errorf("probe: send input: %w", sendErr)
	case sessionErr != nil && res.Error == "":
		return res, fmt.Errorf("probe: session: %w", sessionErr)
	case res.Error != "":
		return res, errors.New(res.Error)
	case frames < cfg.Frames:
		return res, fmt.Errorf("probe: received %d frames, want %d", frames, cfg.Frames)
	case receipts < expected:
		return res, fmt.Errorf("probe: received %d receipts, want %d", receipts, expected)
	case failures > 0:
		return res, fmt.Errorf("probe: %d input receipts failed", failures)
	case cfg.Frames > 1 && res.DigestFirst == res.DigestLast:
		return res, errors.New("probe: consecutive frames are identical; the source did not animate")
	}
	return res, nil
}

// sendInputs exercises every input kind once and a text commit. All operations
// carry an op ID so the host must report a receipt for each one.
func sendInputs(c *remote.Client, text string) (int, error) {
	ops := []proto.Input{
		{OpID: 1, Kind: proto.KindMouseMove, X: 120, Y: 140},
		{OpID: 2, Kind: proto.KindMouseDown, Arg: 0},
		{OpID: 3, Kind: proto.KindMouseUp, Arg: 0},
		{OpID: 4, Kind: proto.KindWheel, Arg: 120},
		{OpID: 5, Kind: proto.KindKeyDown, Arg: 0x41},
		{OpID: 6, Kind: proto.KindKeyUp, Arg: 0x41},
	}
	sent := 0
	for _, in := range ops {
		if err := c.SendInput(in); err != nil {
			return sent, err
		}
		sent++
	}
	if err := c.SendTextOp(7, text); err != nil {
		return sent, err
	}
	return TestInputs, nil
}
