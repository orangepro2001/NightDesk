// Command nx-probe connects to a running go-nx host, receives frames and
// optionally sends one of each input kind. It is an acceptance tool for real
// Windows hosts; it never captures or injects anything itself.
package main

import (
	"context"
	"encoding/json"
	"flag"
	"fmt"
	"os"
	"os/signal"
	"time"

	"github.com/orangepro2001/nightdesk/native/internal/probe"
)

func main() {
	var (
		addr    = flag.String("addr", "", "host address, e.g. 100.64.0.1:7000")
		fp      = flag.String("fingerprint", "", "expected host certificate SHA-256 fingerprint")
		code    = flag.String("code", "", "pairing code shown by the host")
		state   = flag.String("state-dir", "", "client identity directory (default: temporary)")
		frames  = flag.Int("frames", 3, "number of frames to receive")
		timeout = flag.Duration("timeout", 30*time.Second, "overall deadline")
		input   = flag.Bool("input", false, "send mouse, wheel, key and text operations")
		text    = flag.String("text", "NightDesk probe 你好", "text used with -input")
		asJSON  = flag.Bool("json", false, "print the result as JSON")
	)
	flag.Parse()
	if *addr == "" || *fp == "" {
		fmt.Fprintln(os.Stderr, "nx-probe: -addr and -fingerprint are required")
		os.Exit(2)
	}

	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt)
	defer stop()
	res, err := probe.Run(ctx, probe.Config{
		Addr:        *addr,
		Fingerprint: *fp,
		Code:        *code,
		StateDir:    *state,
		Frames:      *frames,
		Timeout:     *timeout,
		SendInput:   *input,
		Text:        *text,
	})
	if *asJSON {
		_ = json.NewEncoder(os.Stdout).Encode(res)
	} else {
		fmt.Printf("connected: %v\n", res.Connected)
		fmt.Printf("screen: %dx%d\n", res.Width, res.Height)
		fmt.Printf("first frame: %d ms\n", res.FirstFrameMS)
		fmt.Printf("frames: %d (%d tile updates)\n", res.Frames, res.Tiles)
		fmt.Printf("digest first/last: %s / %s\n", short(res.DigestFirst), short(res.DigestLast))
		if res.InputsSent > 0 {
			fmt.Printf("inputs: %d sent, %d receipts, %d failed\n", res.InputsSent, res.Receipts, res.ReceiptFailures)
		}
		fmt.Printf("duration: %d ms\n", res.DurationMS)
	}
	if err != nil {
		fmt.Fprintln(os.Stderr, "FAIL:", err)
		os.Exit(1)
	}
	if !*asJSON {
		fmt.Println("OK: remote-control probe passed")
	}
}

func short(s string) string {
	if len(s) <= 16 {
		return s
	}
	return s[:16]
}
