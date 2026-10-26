package synth

import (
	"bytes"
	"testing"

	"github.com/orangepro2001/nightdesk/native/internal/tile"
)

func TestNewRejectsTinyFrames(t *testing.T) {
	if _, err := New(64, 64); err == nil {
		t.Fatal("expected an error for a frame smaller than two tiles")
	}
}

func TestGrabAnimatesAndKeepsStaticBackground(t *testing.T) {
	s, err := New(320, 240)
	if err != nil {
		t.Fatal(err)
	}
	if w, h := s.Size(); w != 320 || h != 240 {
		t.Fatalf("Size() = %dx%d", w, h)
	}
	first := tile.NewFrame(320, 240)
	if err := s.Grab(first); err != nil {
		t.Fatal(err)
	}
	if bytes.Count(first.Pix, []byte{0}) == len(first.Pix) {
		t.Fatal("first frame is empty")
	}
	second := tile.NewFrame(320, 240)
	if err := s.Grab(second); err != nil {
		t.Fatal(err)
	}
	if bytes.Equal(first.Pix, second.Pix) {
		t.Fatal("consecutive frames should differ")
	}
	// The top-left interior pixel is static background and must not move.
	if !bytes.Equal(first.Pix[4*8:4*9], second.Pix[4*8:4*9]) {
		t.Fatal("static background changed between frames")
	}
	if s.Frames() != 2 {
		t.Fatalf("Frames() = %d", s.Frames())
	}
}

func TestNewAcceptsMinimumFrameAndRejectsOnePixelLess(t *testing.T) {
	if _, err := New(blockW+16, blockH+144); err != nil {
		t.Fatalf("minimum size rejected: %v", err)
	}
	if _, err := New(blockW+15, blockH+144); err == nil {
		t.Fatal("narrow frame accepted")
	}
	if _, err := New(blockW+16, blockH+143); err == nil {
		t.Fatal("short frame accepted")
	}
}

func TestGrabRejectsWrongSize(t *testing.T) {
	s, err := New(320, 240)
	if err != nil {
		t.Fatal(err)
	}
	if err := s.Grab(tile.NewFrame(320, 256)); err == nil {
		t.Fatal("expected a size mismatch error")
	}
}
