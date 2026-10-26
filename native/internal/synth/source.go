// Package synth renders a deterministic, animated BGRA test pattern. It exists
// so the real host binary can be exercised end to end on a machine where GDI
// capture is not safe or not available. It never touches the desktop.
package synth

import (
	"errors"
	"sync"

	"github.com/orangepro2001/nightdesk/native/internal/tile"
)

// DefaultWidth and DefaultHeight match a common 1080p desktop.
const (
	DefaultWidth  = 1920
	DefaultHeight = 1080
)

const (
	blockW = 192
	blockH = 96
	barH   = 24
)

// Source produces a mostly static frame with a small moving block and a
// changing progress bar. The small dirty region keeps tile diffs realistic:
// a probe can verify both the full first frame and incremental updates.
type Source struct {
	W, H int

	mu    sync.Mutex
	frame uint64
	bg    []byte
}

// New returns a pattern source with the given size. The minimum size leaves
// room for the moving block, margin and progress bar without negative modulo
// ranges.
func New(w, h int) (*Source, error) {
	if w < blockW+16 || h < blockH+144 {
		return nil, errors.New("synth: frame is too small for the test pattern")
	}
	s := &Source{W: w, H: h, bg: make([]byte, w*h*4)}
	for y := 0; y < h; y++ {
		for x := 0; x < w; x++ {
			o := (y*w + x) * 4
			s.bg[o+0] = byte(20 + y*60/h)
			s.bg[o+1] = byte(26 + y*20/h)
			s.bg[o+2] = byte(34 + x*40/w)
			s.bg[o+3] = 0xff
		}
	}
	// A static border makes orientation and cropping errors obvious.
	for x := 0; x < w; x++ {
		for _, y := range []int{0, 1, h - 2, h - 1} {
			s.set(x, y, 0x00, 0xc0, 0xff)
		}
	}
	for y := 0; y < h; y++ {
		for _, x := range []int{0, 1, w - 2, w - 1} {
			s.set(x, y, 0x00, 0xc0, 0xff)
		}
	}
	return s, nil
}

// Size implements remote.Source.
func (s *Source) Size() (int, int) { return s.W, s.H }

// Grab draws the next animation step into dst. The caller owns dst.
func (s *Source) Grab(dst *tile.Frame) error {
	if dst.W != s.W || dst.H != s.H {
		return errors.New("synth: destination frame size does not match the source")
	}
	s.mu.Lock()
	s.frame++
	step := s.frame
	s.mu.Unlock()

	copy(dst.Pix, s.bg)
	apply := func(x, y int, b, g, r byte) {
		if x < 0 || y < 0 || x >= s.W || y >= s.H {
			return
		}
		o := (y*s.W + x) * 4
		dst.Pix[o+0], dst.Pix[o+1], dst.Pix[o+2], dst.Pix[o+3] = b, g, r, 0xff
	}
	// Moving block: its colour and position change every frame.
	bx := int(step*16) % (s.W - blockW)
	by := 64 + int(step*8)%(s.H-blockH-128)
	for y := 0; y < blockH; y++ {
		for x := 0; x < blockW; x++ {
			apply(bx+x, by+y, byte(step*9), byte(90+step*3), byte(200-step))
		}
	}
	// Progress bar: grows and wraps, changing only a couple of tiles.
	bw := int(step*24) % (s.W - 32)
	for y := s.H - barH - 8; y < s.H-8; y++ {
		for x := 8; x < 8+bw; x++ {
			apply(x, y, 0xff, 0xff, 0xff)
		}
	}
	return nil
}

// Frames reports how many frames have been rendered, for logs and tests.
func (s *Source) Frames() uint64 {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.frame
}

func (s *Source) set(x, y int, b, g, r byte) {
	o := (y*s.W + x) * 4
	s.bg[o+0], s.bg[o+1], s.bg[o+2], s.bg[o+3] = b, g, r, 0xff
}
