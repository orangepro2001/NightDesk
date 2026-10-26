package probe

import (
	"context"
	"path/filepath"
	"testing"
	"time"

	"github.com/orangepro2001/nightdesk/native/internal/logsink"
	"github.com/orangepro2001/nightdesk/native/internal/proto"
	"github.com/orangepro2001/nightdesk/native/internal/remote"
	"github.com/orangepro2001/nightdesk/native/internal/synth"
	"github.com/orangepro2001/nightdesk/native/internal/trust"
)

const testCode = "123456"

// startHost runs a real QUIC host with the synthetic source on loopback.
func startHost(t *testing.T, wantPassword string) (addr, fingerprint string, sink *logsink.Sink, stop func()) {
	t.Helper()
	dir := t.TempDir()
	id, err := trust.Identity(filepath.Join(dir, "host"))
	if err != nil {
		t.Fatal(err)
	}
	ln, err := remote.Listen("127.0.0.1:0", id)
	if err != nil {
		t.Fatal(err)
	}
	src, err := synth.New(640, 480)
	if err != nil {
		t.Fatal(err)
	}
	sink = logsink.New(nil)
	ctx, cancel := context.WithCancel(context.Background())
	go func() {
		for {
			conn, err := ln.Accept(ctx)
			if err != nil {
				return
			}
			go func() {
				_ = remote.Serve(ctx, conn, src, sink, proto.NewDedup(), remote.Options{
					FPS: 10,
					Check: func(_fp, _addr, password string) (trust.Verdict, error) {
						if password == wantPassword {
							return trust.Allowed, nil
						}
						return trust.BadPassword, nil
					},
				})
			}()
		}
	}()
	stop = func() {
		cancel()
		_ = ln.Close()
	}
	return ln.Addr().String(), trust.Fingerprint(id.Certificate[0]), sink, stop
}

func TestProbeReceivesFramesAndInputReceipts(t *testing.T) {
	addr, fp, sink, stop := startHost(t, testCode)
	defer stop()

	ctx, cancel := context.WithTimeout(context.Background(), 25*time.Second)
	defer cancel()
	res, err := Run(ctx, Config{
		Addr: addr, Fingerprint: fp, Code: testCode,
		Frames: 3, SendInput: true,
	})
	if err != nil {
		t.Fatalf("probe failed: %v (%+v)", err, res)
	}
	if !res.Connected || res.Width != 640 || res.Height != 480 {
		t.Fatalf("unexpected result: %+v", res)
	}
	if res.Frames < 3 {
		t.Fatalf("frames = %d", res.Frames)
	}
	if res.Receipts != TestInputs {
		t.Fatalf("receipts = %d, want %d", res.Receipts, TestInputs)
	}
	if res.DigestFirst == res.DigestLast {
		t.Fatal("expected the synthetic source to animate")
	}
	if n, _ := sink.Snapshot(); n != TestInputs {
		t.Fatalf("host accepted %d input operations, want %d", n, TestInputs)
	}
}

func TestProbeRejectsWrongFingerprint(t *testing.T) {
	addr, _, _, stop := startHost(t, testCode)
	defer stop()

	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if _, err := Run(ctx, Config{
		Addr: addr, Fingerprint: string(make([]byte, 64)), Code: testCode, Frames: 1,
	}); err == nil {
		t.Fatal("expected the pinned fingerprint check to fail")
	}
}

func TestProbeRejectsWrongCode(t *testing.T) {
	addr, fp, _, stop := startHost(t, testCode)
	defer stop()

	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if _, err := Run(ctx, Config{
		Addr: addr, Fingerprint: fp, Code: "000000", Frames: 1, Timeout: 5 * time.Second,
	}); err == nil {
		t.Fatal("expected the host to refuse the pairing code")
	}
}
