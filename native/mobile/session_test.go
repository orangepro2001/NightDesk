package mobile

import (
	"context"
	"crypto/tls"
	"errors"
	"sync"
	"testing"
	"time"

	"github.com/orangepro2001/nightdesk/native/internal/proto"
	"github.com/orangepro2001/nightdesk/native/internal/remote"
	"github.com/orangepro2001/nightdesk/native/internal/tile"
	"github.com/orangepro2001/nightdesk/native/internal/trust"
)

type testSource struct {
	w, h int
	pix  []byte
}

func (s *testSource) Size() (int, int) { return s.w, s.h }
func (s *testSource) Grab(dst *tile.Frame) error {
	copy(dst.Pix, s.pix)
	return nil
}

type testInjector struct {
	mu     sync.Mutex
	inputs []proto.Input
	texts  []string
}

func (i *testInjector) Inject(in proto.Input) error {
	i.mu.Lock()
	i.inputs = append(i.inputs, in)
	i.mu.Unlock()
	return nil
}

func (i *testInjector) InjectText(text string) error {
	i.mu.Lock()
	i.texts = append(i.texts, text)
	i.mu.Unlock()
	return nil
}

func (i *testInjector) count(kind byte) int {
	i.mu.Lock()
	defer i.mu.Unlock()
	var n int
	for _, in := range i.inputs {
		if in.Kind == kind {
			n++
		}
	}
	return n
}

func (i *testInjector) textCount() int {
	i.mu.Lock()
	defer i.mu.Unlock()
	return len(i.texts)
}

func (i *testInjector) input(kind byte) (proto.Input, bool) {
	i.mu.Lock()
	defer i.mu.Unlock()
	for _, in := range i.inputs {
		if in.Kind == kind {
			return in, true
		}
	}
	return proto.Input{}, false
}

type tileEvent struct {
	x, y, w, h int
	pix        []byte
}

type frameEvent struct {
	w, h  int
	tiles []tileEvent
}

type testCallback struct {
	mu        sync.Mutex
	states    []string
	errors    []string
	receipts  map[int64]int
	frames    []frameEvent
	stateCh   chan string
	errorCh   chan string
	frameCh   chan struct{}
	receiptCh chan int64
}

func newTestCallback() *testCallback {
	return &testCallback{
		receipts:  make(map[int64]int),
		stateCh:   make(chan string, 32),
		errorCh:   make(chan string, 8),
		frameCh:   make(chan struct{}, 32),
		receiptCh: make(chan int64, 32),
	}
}

func (c *testCallback) OnState(state string) {
	c.mu.Lock()
	c.states = append(c.states, state)
	c.mu.Unlock()
	select {
	case c.stateCh <- state:
	default:
	}
}

func (c *testCallback) OnFrameBegin(width, height int) {
	c.mu.Lock()
	c.frames = append(c.frames, frameEvent{w: width, h: height})
	c.mu.Unlock()
}

func (c *testCallback) OnTile(x, y, w, h int, pixels []byte) {
	c.mu.Lock()
	if len(c.frames) == 0 {
		c.mu.Unlock()
		return
	}
	f := &c.frames[len(c.frames)-1]
	f.tiles = append(f.tiles, tileEvent{
		x: x, y: y, w: w, h: h,
		pix: append([]byte(nil), pixels...),
	})
	c.mu.Unlock()
	select {
	case c.frameCh <- struct{}{}:
	default:
	}
}

func (c *testCallback) OnReceipt(opID int64, status int) {
	c.mu.Lock()
	c.receipts[opID] = status
	c.mu.Unlock()
	select {
	case c.receiptCh <- opID:
	default:
	}
}

func (c *testCallback) OnError(code, message string) {
	c.mu.Lock()
	c.errors = append(c.errors, code+": "+message)
	c.mu.Unlock()
	select {
	case c.errorCh <- code:
	default:
	}
}

func (c *testCallback) waitState(t *testing.T, want string) {
	t.Helper()
	deadline := time.After(10 * time.Second)
	for {
		select {
		case got := <-c.stateCh:
			if got == want {
				return
			}
		case <-deadline:
			t.Fatalf("state %q not observed", want)
		}
	}
}

func (c *testCallback) waitFrame(t *testing.T) frameEvent {
	t.Helper()
	select {
	case <-c.frameCh:
		c.mu.Lock()
		defer c.mu.Unlock()
		return c.frames[len(c.frames)-1]
	case <-time.After(10 * time.Second):
		t.Fatal("frame not received")
		return frameEvent{}
	}
}

func (c *testCallback) waitError(t *testing.T, want string) {
	t.Helper()
	deadline := time.After(10 * time.Second)
	for {
		select {
		case got := <-c.errorCh:
			if got == want {
				return
			}
		case <-deadline:
			c.mu.Lock()
			defer c.mu.Unlock()
			t.Fatalf("error %q not observed; got %v", want, c.errors)
		}
	}
}

func identityForTest(t *testing.T) tls.Certificate {
	t.Helper()
	id, err := trust.Identity(t.TempDir())
	if err != nil {
		t.Fatal(err)
	}
	return id
}

func fingerprint(id tls.Certificate) string { return trust.Fingerprint(id.Certificate[0]) }

// startMobileHost serves one accepted connection at a time on loopback.
func startMobileHost(t *testing.T, hostID tls.Certificate, viewerFP, password string, src remote.Source, inj remote.Injector) string {
	t.Helper()
	ctx, cancel := context.WithCancel(context.Background())
	ln, err := remote.Listen("127.0.0.1:0", hostID)
	if err != nil {
		cancel()
		t.Fatal(err)
	}
	t.Cleanup(func() {
		cancel()
		ln.Close()
	})
	go func() {
		for {
			conn, err := ln.Accept(ctx)
			if err != nil {
				return
			}
			go func() {
				_ = remote.Serve(ctx, conn, src, inj, proto.NewDedup(), remote.Options{
					FPS: 50,
					Check: func(fp, _, pw string) (trust.Verdict, error) {
						if fp != viewerFP {
							return trust.Untrusted, nil
						}
						if pw != password {
							return trust.BadPassword, nil
						}
						return trust.Allowed, nil
					},
				})
			}()
		}
	}()
	return ln.Addr().String()
}

func TestSessionPairsStreamsAndInjectsInput(t *testing.T) {
	hostID := identityForTest(t)
	clientDir := t.TempDir()
	viewerID, err := trust.Identity(clientDir)
	if err != nil {
		t.Fatal(err)
	}
	src := &testSource{w: 150, h: 90, pix: make([]byte, 150*90*4)}
	copy(src.pix[:4], []byte{9, 8, 7, 255})
	inj := &testInjector{}
	addr := startMobileHost(t, hostID, fingerprint(viewerID), "123456", src, inj)

	cb := newTestCallback()
	s, err := NewSession(clientDir, addr, fingerprint(hostID), "123456", cb)
	if err != nil {
		t.Fatal(err)
	}
	defer s.Close()

	f := cb.waitFrame(t)
	if f.w != 150 || f.h != 90 || len(f.tiles) == 0 {
		t.Fatalf("frame = %dx%d tiles=%d", f.w, f.h, len(f.tiles))
	}
	first := f.tiles[0]
	if first.x != 0 || first.y != 0 || first.w != 64 || first.h != 64 {
		t.Fatalf("first tile = %+v", first)
	}
	if len(first.pix) < 4 || first.pix[0] != 9 || first.pix[1] != 8 || first.pix[2] != 7 || first.pix[3] != 255 {
		t.Fatalf("first pixel = %v", first.pix[:4])
	}
	cb.waitState(t, StateStreaming)
	if s.Width() != 150 || s.Height() != 90 {
		t.Fatalf("size = %dx%d", s.Width(), s.Height())
	}

	if err := s.Move(10, 20); err != nil {
		t.Fatal(err)
	}
	if err := s.MouseDown(0); err != nil {
		t.Fatal(err)
	}
	if err := s.Wheel(120); err != nil {
		t.Fatal(err)
	}
	if err := s.WheelH(-120); err != nil {
		t.Fatal(err)
	}
	if err := s.MouseUp(0); err != nil {
		t.Fatal(err)
	}
	if err := s.KeyDown(0x41); err != nil {
		t.Fatal(err)
	}
	if err := s.KeyUp(0x41); err != nil {
		t.Fatal(err)
	}
	if err := s.Text("hello 世界"); err != nil {
		t.Fatal(err)
	}
	waitReceipt(t, cb, 1, ReceiptExecuted)

	deadline := time.Now().Add(5 * time.Second)
	for time.Now().Before(deadline) {
		if inj.count(proto.KindMouseMove) == 1 && inj.count(proto.KindMouseDown) == 1 &&
			inj.count(proto.KindMouseUp) == 1 && inj.count(proto.KindWheel) == 1 &&
			inj.count(proto.KindWheelH) == 1 &&
			inj.count(proto.KindKeyDown) == 1 && inj.count(proto.KindKeyUp) == 1 && inj.textCount() == 1 {
			break
		}
		time.Sleep(10 * time.Millisecond)
	}
	if got := inj.count(proto.KindMouseMove); got != 1 {
		t.Fatalf("move injections = %d", got)
	}
	if inj.textCount() != 1 {
		t.Fatalf("text injections = %d", inj.textCount())
	}
	for _, kind := range []byte{proto.KindMouseDown, proto.KindMouseUp, proto.KindWheel, proto.KindWheelH} {
		in, ok := inj.input(kind)
		if !ok {
			t.Fatalf("input kind %d not observed", kind)
		}
		if in.X != 10 || in.Y != 20 {
			t.Fatalf("input kind %d coordinates = (%d,%d), want (10,20)", kind, in.X, in.Y)
		}
	}
}

func TestSessionReleasesHeldInput(t *testing.T) {
	hostID := identityForTest(t)
	clientDir := t.TempDir()
	viewerID, err := trust.Identity(clientDir)
	if err != nil {
		t.Fatal(err)
	}
	src := &testSource{w: 64, h: 64, pix: make([]byte, 64*64*4)}
	inj := &testInjector{}
	addr := startMobileHost(t, hostID, fingerprint(viewerID), "code", src, inj)

	cb := newTestCallback()
	s, err := NewSession(clientDir, addr, fingerprint(hostID), "code", cb)
	if err != nil {
		t.Fatal(err)
	}
	if err := s.Move(23, 34); err != nil {
		t.Fatal(err)
	}
	if err := s.MouseDown(1); err != nil {
		t.Fatal(err)
	}
	if err := s.KeyDown(0x10); err != nil {
		t.Fatal(err)
	}
	if err := s.ReleaseAll(); err != nil {
		t.Fatal(err)
	}
	deadline := time.Now().Add(5 * time.Second)
	for time.Now().Before(deadline) {
		if inj.count(proto.KindMouseUp) == 1 && inj.count(proto.KindKeyUp) == 1 {
			break
		}
		time.Sleep(10 * time.Millisecond)
	}
	if inj.count(proto.KindMouseUp) != 1 || inj.count(proto.KindKeyUp) != 1 {
		t.Fatalf("held inputs not released: mouseUp=%d keyUp=%d", inj.count(proto.KindMouseUp), inj.count(proto.KindKeyUp))
	}
	in, ok := inj.input(proto.KindMouseUp)
	if !ok || in.X != 23 || in.Y != 34 {
		t.Fatalf("released mouse coordinates = (%d,%d), want (23,34)", in.X, in.Y)
	}
}

func TestSessionRejectsWrongHostFingerprint(t *testing.T) {
	hostID := identityForTest(t)
	clientDir := t.TempDir()
	viewerID, err := trust.Identity(clientDir)
	if err != nil {
		t.Fatal(err)
	}
	addr := startMobileHost(t, hostID, fingerprint(viewerID), "", &testSource{w: 8, h: 8, pix: make([]byte, 8*8*4)}, &testInjector{})

	cb := newTestCallback()
	_, err = NewSession(clientDir, addr, "0000000000000000000000000000000000000000000000000000000000000000", "", cb)
	if err == nil {
		t.Fatal("wrong fingerprint accepted")
	}
	if !errors.Is(err, remote.ErrHostChanged) {
		t.Fatalf("err = %v, want ErrHostChanged", err)
	}
}

func TestSessionReportsBadPairingCode(t *testing.T) {
	hostID := identityForTest(t)
	clientDir := t.TempDir()
	viewerID, err := trust.Identity(clientDir)
	if err != nil {
		t.Fatal(err)
	}
	addr := startMobileHost(t, hostID, fingerprint(viewerID), "123456", &testSource{w: 8, h: 8, pix: make([]byte, 8*8*4)}, &testInjector{})

	cb := newTestCallback()
	s, err := NewSession(clientDir, addr, fingerprint(hostID), "000000", cb)
	if err != nil {
		t.Fatal(err)
	}
	defer s.Close()
	cb.waitError(t, "bad-code")
}

func TestSessionValidatesArguments(t *testing.T) {
	cb := newTestCallback()
	if _, err := NewSession("", "127.0.0.1:1", "", "", cb); err == nil {
		t.Fatal("empty storage dir accepted")
	}
	if _, err := NewSession(t.TempDir(), "not-an-address", "", "", cb); err == nil {
		t.Fatal("bad address accepted")
	}
	if _, err := NewSession(t.TempDir(), "127.0.0.1:1", "bad", "", cb); err == nil {
		t.Fatal("bad fingerprint accepted")
	}
}

func waitReceipt(t *testing.T, cb *testCallback, opID int64, want int) {
	t.Helper()
	deadline := time.After(10 * time.Second)
	for {
		select {
		case got := <-cb.receiptCh:
			if got != opID {
				continue
			}
			cb.mu.Lock()
			status := cb.receipts[got]
			cb.mu.Unlock()
			if status != want {
				t.Fatalf("receipt %d = %d, want %d", opID, status, want)
			}
			return
		case <-deadline:
			t.Fatalf("receipt %d not received", opID)
		}
	}
}

// Compile-time checks for the gomobile-facing surface.
var _ Callback = (*testCallback)(nil)
var _ remote.TextInjector = (*testInjector)(nil)
