//go:build windows

// Command nx-host serves one Windows monitor over the go-nx QUIC protocol.
//
// It is a headless sidecar for the NightDesk WPF app: NightDesk starts it,
// talks to it as JSON lines on stdin/stdout, and never edits the display
// topology itself. The host refuses unknown viewers by default; pairing is
// opened on request and binds one certificate fingerprint per code.
package main

import (
	"bufio"
	"context"
	"crypto/rand"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"log"
	"math/big"
	"os"
	"os/signal"
	"path/filepath"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"github.com/quic-go/quic-go"

	"github.com/orangepro2001/nightdesk/native/internal/capture"
	"github.com/orangepro2001/nightdesk/native/internal/inject"
	"github.com/orangepro2001/nightdesk/native/internal/logsink"
	"github.com/orangepro2001/nightdesk/native/internal/proto"
	"github.com/orangepro2001/nightdesk/native/internal/remote"
	"github.com/orangepro2001/nightdesk/native/internal/synth"
	"github.com/orangepro2001/nightdesk/native/internal/tile"
	"github.com/orangepro2001/nightdesk/native/internal/trust"
)

const version = "0.2.1"

const (
	pairingWindow = 5 * time.Minute
	defaultFPS    = 10
)

type event struct {
	Event       string            `json:"event"`
	Version     string            `json:"version,omitempty"`
	PID         int               `json:"pid,omitempty"`
	Listen      string            `json:"listen,omitempty"`
	Display     string            `json:"display,omitempty"`
	Width       int               `json:"width,omitempty"`
	Height      int               `json:"height,omitempty"`
	DPI         int               `json:"dpi,omitempty"`
	Identity    string            `json:"identity,omitempty"`
	Code        string            `json:"code,omitempty"`
	ExpiresIn   int               `json:"expiresIn,omitempty"`
	Addr        string            `json:"addr,omitempty"`
	Reason      string            `json:"reason,omitempty"`
	Message     string            `json:"message,omitempty"`
	Trusted     map[string]string `json:"trusted,omitempty"`
	FPS         int               `json:"fps,omitempty"`
	RequireAuth bool              `json:"requireAuth"`
	TestMode    bool              `json:"testMode,omitempty"`
	Source      string            `json:"source,omitempty"`
	InputMode   string            `json:"inputMode,omitempty"`
}

type command struct {
	Cmd         string `json:"cmd"`
	Seconds     int    `json:"seconds"`
	Fingerprint string `json:"fingerprint"`
	Value       int    `json:"value"`
}

// emitter serializes JSON lines to stdout. Every line is one event; NightDesk's
// NxHostService reads them one at a time.
type emitter struct {
	mu    sync.Mutex
	human bool
}

func (e *emitter) emit(ev event) {
	e.mu.Lock()
	defer e.mu.Unlock()
	if e.human {
		switch ev.Event {
		case "ready":
			fmt.Fprintln(os.Stderr, fmt.Sprintf("listening on %s, display %s %dx%d identity %s", ev.Listen, ev.Display, ev.Width, ev.Height, ev.Identity))
		case "pairing":
			fmt.Fprintln(os.Stderr, fmt.Sprintf("pairing code %s (valid %ds)", ev.Code, ev.ExpiresIn))
		case "connected", "disconnected":
			fmt.Fprintln(os.Stderr, ev.Event, ev.Addr, ev.Reason)
		case "error":
			fmt.Fprintln(os.Stderr, "error:", ev.Message)
		default:
			fmt.Fprintln(os.Stderr, ev.Event, ev.Addr, ev.Message)
		}
		return
	}
	b, err := json.Marshal(ev)
	if err != nil {
		return
	}
	_, _ = os.Stdout.Write(append(b, 0x0A))
}

func main() {
	var (
		listenAddr = flag.String("listen", "127.0.0.1:7000", "UDP address to listen on, e.g. 0.0.0.0:7000")
		display    = flag.String("display", "primary", "monitor: index, device name or primary")
		fps        = flag.Int("fps", defaultFPS, "capture rate")
		stateDir   = flag.String("state-dir", "", "identity and trusted devices (default: LOCALAPPDATA/NightDesk/nx-host)")
		pair       = flag.Bool("pair", false, "open pairing at startup")
		noAuth     = flag.Bool("no-auth", false, "accept any viewer; development only")
		control    = flag.String("control", "stdio", "stdio for JSON lines on stdin/stdout, none for a console host")
		testMode   = flag.Bool("test-mode", false, "isolated pattern/log acceptance mode; requires -source pattern -inject log")
		sourceMode = flag.String("source", "gdi", "gdi for real capture or pattern for a synthetic test pattern")
		injectMode = flag.String("inject", "sendinput", "sendinput for real input or log to accept input without touching the desktop")
		showVer    = flag.Bool("version", false, "print version and exit")
	)
	flag.Parse()
	if *showVer {
		fmt.Println("nx-host " + version)
		return
	}

	log.SetFlags(log.Ltime | log.Lmicroseconds)
	log.SetOutput(os.Stderr)

	if *sourceMode != "gdi" && *sourceMode != "pattern" {
		fatalMsg(&emitter{human: *control == "none"}, "unknown -source "+*sourceMode+" (want gdi or pattern)")
	}
	if *injectMode != "sendinput" && *injectMode != "log" {
		fatalMsg(&emitter{human: *control == "none"}, "unknown -inject "+*injectMode+" (want sendinput or log)")
	}
	if (*sourceMode != "gdi" || *injectMode != "sendinput") && !*testMode {
		fatalMsg(&emitter{human: *control == "none"}, "-source pattern and -inject log require -test-mode")
	}
	// A test-mode process must never be able to touch the desktop, even when a
	// flag such as -source gdi is passed by accident.
	if *testMode && (*sourceMode != "pattern" || *injectMode != "log") {
		fatalMsg(&emitter{human: *control == "none"}, "-test-mode requires exactly -source pattern -inject log")
	}

	em := &emitter{human: *control == "none"}
	dir := *stateDir
	if dir == "" {
		if *testMode {
			// Keep acceptance runs away from the production identity and trust
			// store. The temporary directory is removed when the host exits.
			tmp, err := os.MkdirTemp("", "nx-host-test-")
			if err != nil {
				fatal(em, "cannot create test state directory", err)
			}
			dir = tmp
			defer os.RemoveAll(tmp)
		} else {
			base := os.Getenv("LOCALAPPDATA")
			if base == "" {
				base = os.Getenv("TEMP")
			}
			dir = filepath.Join(base, "NightDesk", "nx-host")
		}
	}
	if err := os.MkdirAll(dir, 0o700); err != nil {
		fatal(em, "cannot create state directory", err)
	}

	var (
		src remote.Source
		d   capture.Display
	)
	if *sourceMode == "pattern" {
		p, err := synth.New(synth.DefaultWidth, synth.DefaultHeight)
		if err != nil {
			fatal(em, "cannot create test pattern", err)
		}
		src = p
		d = capture.Display{Device: "pattern", W: synth.DefaultWidth, H: synth.DefaultHeight, Primary: true, DPI: 96}
		log.Printf("capture: %s (TEST MODE: synthetic)", d)
	} else {
		scr, err := capture.Open(*display)
		if err != nil {
			fatal(em, "cannot open display", err)
		}
		defer scr.Close()
		src = &checkedSource{Screen: scr}
		d = scr.Display()
		log.Printf("capture: %s", d)
	}

	id, err := trust.Identity(dir)
	if err != nil {
		fatal(em, "cannot load identity", err)
	}
	store, err := trust.OpenStore(filepath.Join(dir, "trusted.json"))
	if err != nil {
		fatal(em, "cannot load trusted devices", err)
	}
	policy := trust.NewPolicy(store)
	policy.SetRequireAuth(!*noAuth)
	if *noAuth {
		log.Printf("WARNING: authentication disabled (--no-auth)")
	}

	ln, err := remote.Listen(*listenAddr, id)
	if err != nil {
		fatal(em, "cannot listen", fmt.Errorf("%v (another process may already use %s)", err, *listenAddr))
	}
	defer ln.Close()

	var inj remote.Injector
	if *injectMode == "log" {
		inj = logsink.New(log.Default())
		log.Printf("TEST MODE: input is logged, not injected")
	} else {
		vx, vy, vw, vh := capture.VirtualScreen()
		inj = inject.NewDisplay(int(d.W), int(d.H), int(d.X), int(d.Y), vx, vy, vw, vh)
	}

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	h := &host{
		em: em, policy: policy, store: store, cancel: cancel,
		identity: trust.Fingerprint(id.Certificate[0]),
		display:  d, listen: *listenAddr,
		testMode: *testMode, source: *sourceMode, inputMode: *injectMode,
	}
	h.fps.Store(int32(max(*fps, 1)))

	em.emit(event{
		Event: "ready", Version: version, PID: os.Getpid(), Listen: *listenAddr,
		Display: d.Device, Width: int(d.W), Height: int(d.H), DPI: d.DPI,
		Identity: h.identity, FPS: int(h.fps.Load()), RequireAuth: policy.RequireAuth(),
		TestMode: *testMode, Source: *sourceMode, InputMode: *injectMode,
	})
	if *testMode {
		em.emit(event{Event: "warning", Message: "TEST MODE: synthetic source and logged input; no screen capture or OS input"})
	}

	go h.acceptLoop(ctx, ln, src, inj)

	if *pair {
		h.openPairing(pairingWindow)
	}

	sig := make(chan os.Signal, 1)
	signal.Notify(sig, os.Interrupt)
	go func() {
		select {
		case <-sig:
			h.stop()
		case <-ctx.Done():
		}
	}()

	if *control == "stdio" {
		go h.readCommands(os.Stdin)
	}
	<-ctx.Done()
	em.emit(event{Event: "stopped", Reason: "shutdown"})
}

func fatal(em *emitter, msg string, err error) {
	em.emit(event{Event: "error", Message: msg + ": " + err.Error()})
	log.Printf("%s: %v", msg, err)
	os.Exit(2)
}

func fatalMsg(em *emitter, msg string) {
	em.emit(event{Event: "error", Message: msg})
	log.Printf("%s", msg)
	os.Exit(2)
}

type host struct {
	em        *emitter
	policy    *trust.Policy
	store     *trust.Store
	cancel    context.CancelFunc
	fps       atomic.Int32
	identity  string
	display   capture.Display
	listen    string
	testMode  bool
	source    string
	inputMode string

	mu         sync.Mutex
	active     *quic.Conn
	controller string
	lastFrame  atomic.Int64
	frames     atomic.Uint64
}

func (h *host) stop() { h.cancel() }

func (h *host) openPairing(d time.Duration) {
	code := randomCode()
	h.policy.OpenPairing(code, d)
	h.em.emit(event{Event: "pairing", Code: code, ExpiresIn: int(d.Seconds())})
	log.Printf("pairing window open for %s", d)
}

func randomCode() string {
	n, err := rand.Int(rand.Reader, big.NewInt(1_000_000))
	if err != nil {
		return "000000"
	}
	return fmt.Sprintf("%06d", n.Int64())
}

func (h *host) check(fp, addr, password string) (trust.Verdict, error) {
	v, err := h.policy.Check(fp, addr, password)
	if v != trust.Allowed {
		reason := map[trust.Verdict]string{
			trust.Untrusted: "untrusted", trust.BadPassword: "bad-code", trust.Locked: "locked",
		}[v]
		h.em.emit(event{Event: "denied", Addr: addr, Reason: reason})
		log.Printf("refused %s: %s (%s)", addr, reason, fp)
	}
	return v, err
}

func (h *host) acceptLoop(ctx context.Context, ln *quic.Listener, src remote.Source, inj remote.Injector) {
	var (
		dd    = proto.NewDedup()
		input = remote.NewInputController(inj)
	)
	for {
		conn, err := ln.Accept(ctx)
		if err != nil {
			if ctx.Err() == nil {
				h.em.emit(event{Event: "error", Message: "accept failed: " + err.Error()})
			}
			return
		}
		go h.serve(ctx, conn, src, inj, dd, input)
	}
}

func (h *host) serve(ctx context.Context, conn *quic.Conn, src remote.Source, inj remote.Injector, dd *proto.Dedup, input *remote.InputController) {
	addr := conn.RemoteAddr().String()
	err := remote.Serve(ctx, conn, src, inj, dd, remote.Options{
		FPS:             int(h.fps.Load()),
		Check:           h.check,
		InputController: input,
		OnAuthed: func() {
			h.mu.Lock()
			previous := h.active
			h.active = conn
			h.controller = addr
			h.mu.Unlock()
			if previous != nil {
				previous.CloseWithError(remote.CloseReplaced, "replaced by a new viewer")
			}
			h.em.emit(event{Event: "connected", Addr: addr, Identity: peerFingerprint(conn)})
			log.Printf("viewer connected: %s", addr)
		},
		OnFrame: func(seq uint32, bytes int) {
			h.frames.Add(1)
			h.lastFrame.Store(time.Now().Unix())
			_, _ = seq, bytes
		},
		OnError: func(err error) { log.Printf("session: %v", err) },
	})
	h.mu.Lock()
	if h.active == conn {
		h.active = nil
		h.controller = ""
	}
	h.mu.Unlock()
	if errors.Is(err, remote.ErrDenied) {
		return
	}
	h.em.emit(event{Event: "disconnected", Addr: addr, Reason: reason(err)})
	log.Printf("viewer %s disconnected: %v", addr, err)
}

func reason(err error) string {
	if err == nil {
		return "closed"
	}
	return err.Error()
}

func peerFingerprint(conn *quic.Conn) string {
	if certs := conn.ConnectionState().TLS.PeerCertificates; len(certs) > 0 {
		return trust.Fingerprint(certs[0].Raw)
	}
	return ""
}

func (h *host) readCommands(r *os.File) {
	sc := bufio.NewScanner(r)
	sc.Buffer(make([]byte, 0, 4096), 64<<10)
	for sc.Scan() {
		line := strings.TrimSpace(sc.Text())
		if line == "" {
			continue
		}
		var c command
		if err := json.Unmarshal([]byte(line), &c); err != nil {
			h.em.emit(event{Event: "error", Message: "bad command: " + err.Error()})
			continue
		}
		switch c.Cmd {
		case "pair":
			d := pairingWindow
			if c.Seconds > 0 {
				d = time.Duration(c.Seconds) * time.Second
			}
			h.openPairing(d)
		case "pair-cancel":
			h.policy.ClosePairing()
			h.em.emit(event{Event: "pairing", Code: "", ExpiresIn: 0})
		case "untrust":
			if c.Fingerprint == "" {
				h.em.emit(event{Event: "error", Message: "untrust needs fingerprint"})
				continue
			}
			if err := h.store.Delete(c.Fingerprint); err != nil {
				h.em.emit(event{Event: "error", Message: "untrust: " + err.Error()})
				continue
			}
			h.em.emit(event{Event: "status", Trusted: h.store.List()})
		case "fps":
			if c.Value < 1 || c.Value > 60 {
				h.em.emit(event{Event: "error", Message: "fps out of range"})
				continue
			}
			h.fps.Store(int32(c.Value))
			h.em.emit(event{Event: "status", FPS: c.Value, Trusted: h.store.List()})
		case "status":
			h.em.emit(event{
				Event: "status", FPS: int(h.fps.Load()), Trusted: h.store.List(),
				RequireAuth: h.policy.RequireAuth(), Listen: h.listen,
				Display: h.display.Device, Width: int(h.display.W), Height: int(h.display.H),
				Addr: h.controller, Identity: h.identity,
				Source: h.source, InputMode: h.inputMode, TestMode: h.testMode,
				Message: fmt.Sprintf("frames=%d", h.frames.Load()),
			})
		case "stop":
			h.stop()
			return
		default:
			h.em.emit(event{Event: "error", Message: "unknown command " + c.Cmd})
		}
	}
	// The parent process owns this sidecar: stdin EOF means NightDesk is gone.
	h.stop()
}

// checkedSource fails cleanly when the captured monitor disappears or changes
// size, instead of sending frames of the wrong region.
type checkedSource struct {
	*capture.Screen
	n int
}

func (s *checkedSource) Grab(dst *tile.Frame) error {
	s.n++
	if s.n%30 == 0 && !s.Screen.Valid() {
		return errors.New("capture: display topology changed")
	}
	return s.Screen.Grab(dst)
}
