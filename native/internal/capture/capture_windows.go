//go:build windows

// Package capture grabs one monitor of the Windows desktop with GDI BitBlt.
//
// Those are the same GDI semantics go-nx used for the primary screen: no
// Desktop Duplication, no HDR tone mapping and no protected-content path. What
// is new here is monitor selection and the fact that the caller receives the
// monitor's virtual-desktop origin, which the injector needs to put the cursor
// on the right screen.
package capture

import (
	"errors"
	"fmt"
	"strconv"
	"strings"
	"sync"
	"syscall"
	"unsafe"

	"github.com/orangepro2001/nightdesk/native/internal/tile"
)

var (
	user32    = syscall.NewLazyDLL("user32.dll")
	gdi32     = syscall.NewLazyDLL("gdi32.dll")
	shcore    = syscall.NewLazyDLL("shcore.dll")
	getDC     = user32.NewProc("GetDC")
	releaseDC = user32.NewProc("ReleaseDC")
	enumDisp  = user32.NewProc("EnumDisplayMonitors")
	getMonInf = user32.NewProc("GetMonitorInfoW")
	getMetric = user32.NewProc("GetSystemMetrics")
	createDC  = gdi32.NewProc("CreateCompatibleDC")
	createDIB = gdi32.NewProc("CreateDIBSection")
	selectObj = gdi32.NewProc("SelectObject")
	deleteObj = gdi32.NewProc("DeleteObject")
	deleteDC  = gdi32.NewProc("DeleteDC")
	bitBlt    = gdi32.NewProc("BitBlt")
	getDPI    = shcore.NewProc("GetDpiForMonitor")
	setCtx    = user32.NewProc("SetProcessDpiAwarenessContext")

	dpiAwareOnce sync.Once
)

const (
	smXScreen               = 76
	smYScreen               = 77
	smCXScreen              = 78
	smCYScreen              = 79
	monitorDefaultToPrimary = 0x00000001
	srccopy                 = 0x00CC0020
	captureBlt              = 0x40000000  // include layered windows
	dpiAwarePerMonitorV2    = ^uintptr(3) // (DPI_AWARENESS_CONTEXT)-4
)

// Display describes one monitor in virtual-desktop coordinates.
type Display struct {
	Index   int
	Device  string // "\\.\DISPLAY1"
	X, Y    int32  // top-left in virtual-desktop pixels
	W, H    int32
	Primary bool
	DPI     int // effective DPI, 96 when unknown
}

// String is for logs and the status line.
func (d Display) String() string {
	return fmt.Sprintf("%s %dx%d at %d,%d dpi=%d", d.Device, d.W, d.H, d.X, d.Y, d.DPI)
}

// EnableDPIAwareness makes the process per-monitor DPI aware so captured pixel
// sizes and SendInput coordinates agree with the physical desktop. It is safe to
// call more than once.
func EnableDPIAwareness() {
	dpiAwareOnce.Do(func() {
		if setCtx.Find() == nil {
			if r, _, _ := setCtx.Call(dpiAwarePerMonitorV2); r != 0 {
				return
			}
		}
		// Fallback for Windows versions before the per-monitor context API.
		if p := user32.NewProc("SetProcessDPIAware"); p.Find() == nil {
			p.Call()
		}
	})
}

type rect struct{ Left, Top, Right, Bottom int32 }

type monitorInfoEx struct {
	Size    uint32
	Monitor rect
	Work    rect
	Flags   uint32
	Device  [32]uint16
}

// Displays lists the active monitors in the order EnumDisplayMonitors reports.
func Displays() ([]Display, error) {
	EnableDPIAwareness()
	var out []Display
	cb := syscall.NewCallback(func(hmon uintptr, _ uintptr, _ uintptr, _ uintptr) uintptr {
		var mi monitorInfoEx
		mi.Size = uint32(unsafe.Sizeof(mi))
		if r, _, _ := getMonInf.Call(hmon, uintptr(unsafe.Pointer(&mi))); r == 0 {
			return 1
		}
		d := Display{
			Index:   len(out),
			Device:  syscall.UTF16ToString(mi.Device[:]),
			X:       mi.Monitor.Left,
			Y:       mi.Monitor.Top,
			W:       mi.Monitor.Right - mi.Monitor.Left,
			H:       mi.Monitor.Bottom - mi.Monitor.Top,
			Primary: mi.Flags&monitorDefaultToPrimary != 0,
			DPI:     96,
		}
		var dx, dy uint32
		if getDPI.Find() == nil {
			// MDT_EFFECTIVE_DPI = 0
			if r, _, _ := getDPI.Call(hmon, 0, uintptr(unsafe.Pointer(&dx)), uintptr(unsafe.Pointer(&dy))); r == 0 && dx > 0 {
				d.DPI = int(dx)
			}
		}
		out = append(out, d)
		return 1
	})
	if r, _, err := enumDisp.Call(0, 0, cb, 0); r == 0 {
		return nil, fmt.Errorf("capture: EnumDisplayMonitors failed: %v", err)
	}
	if len(out) == 0 {
		return nil, errors.New("capture: no monitors reported")
	}
	return out, nil
}

// Open selects a monitor and prepares a reusable capture buffer.
//
// selector may be "", "primary", a zero-based index as reported by Displays, or
// a device name such as "\\\\.\\DISPLAY6" (the form NightDesk stores). The
// device name is the stable choice across reboots.
func Open(selector string) (*Screen, error) {
	all, err := Displays()
	if err != nil {
		return nil, err
	}
	d, err := selectDisplay(all, selector)
	if err != nil {
		return nil, err
	}
	s := &Screen{display: d, w: int(d.W), h: int(d.H)}
	s.screen, _, _ = getDC.Call(0)
	if s.screen == 0 {
		return nil, errors.New("capture: GetDC failed")
	}
	s.memDC, _, _ = createDC.Call(s.screen)
	if s.memDC == 0 {
		s.Close()
		return nil, errors.New("capture: CreateCompatibleDC failed")
	}
	bi := bitmapInfoHeader{
		Size: uint32(unsafe.Sizeof(bitmapInfoHeader{})), Width: int32(s.w),
		Height: -int32(s.h), Planes: 1, Bits: 32, // negative height = top-down
	}
	s.bmp, _, _ = createDIB.Call(s.memDC, uintptr(unsafe.Pointer(&bi)), 0,
		uintptr(unsafe.Pointer(&s.pixels)), 0, 0)
	if s.bmp == 0 || s.pixels == nil {
		s.Close()
		return nil, errors.New("capture: CreateDIBSection failed")
	}
	s.old, _, _ = selectObj.Call(s.memDC, s.bmp)
	return s, nil
}

func selectDisplay(all []Display, selector string) (Display, error) {
	sel := strings.TrimSpace(selector)
	switch strings.ToLower(sel) {
	case "", "primary":
		for _, d := range all {
			if d.Primary {
				return d, nil
			}
		}
		return all[0], nil
	}
	if i, err := strconv.Atoi(sel); err == nil {
		if i < 0 || i >= len(all) {
			return Display{}, fmt.Errorf("capture: display index %d out of range (found %d)", i, len(all))
		}
		return all[i], nil
	}
	want := sel
	upper := strings.ToUpper(want)
	if !strings.HasPrefix(upper, "\\\\.") && !strings.HasPrefix(upper, "DISPLAY") {
		want = "\\\\.\\" + want
	}
	for _, d := range all {
		if strings.EqualFold(d.Device, want) {
			return d, nil
		}
	}
	var names []string
	for _, d := range all {
		names = append(names, d.Device)
	}
	return Display{}, fmt.Errorf("capture: display %q not found (have %s)", selector, strings.Join(names, ", "))
}

type bitmapInfoHeader struct {
	Size          uint32
	Width, Height int32
	Planes, Bits  uint16
	Compression   uint32
	SizeImage     uint32
	XPels, YPels  int32
	ClrUsed       uint32
	ClrImportant  uint32
}

// Screen captures one monitor into a reusable BGRA buffer.
type Screen struct {
	mu      sync.Mutex // serializes Grab and Close
	display Display
	w, h    int
	screen  uintptr
	memDC   uintptr
	bmp     uintptr
	old     uintptr
	pixels  unsafe.Pointer
}

// Display returns the monitor this screen was opened for.
func (s *Screen) Display() Display { return s.display }

func (s *Screen) Size() (int, int) { return s.w, s.h }

// Valid reports whether the monitor still exists with the same geometry. Hosts
// call it between frames so a topology change becomes a clean shutdown instead
// of a silently wrong capture.
func (s *Screen) Valid() bool {
	all, err := Displays()
	if err != nil {
		return false
	}
	for _, d := range all {
		if d.Device == s.display.Device && d.X == s.display.X && d.Y == s.display.Y && d.W == s.display.W && d.H == s.display.H {
			return true
		}
	}
	return false
}

// Grab copies the current monitor contents into dst (which must be Size() large).
func (s *Screen) Grab(dst *tile.Frame) error {
	s.mu.Lock()
	defer s.mu.Unlock()
	if dst.W != s.w || dst.H != s.h {
		return errors.New("capture: frame size mismatch")
	}
	r, _, err := bitBlt.Call(s.memDC, 0, 0, uintptr(s.w), uintptr(s.h),
		s.screen, uintptr(s.display.X), uintptr(s.display.Y), srccopy|captureBlt)
	if r == 0 {
		return fmt.Errorf("capture: BitBlt failed: %v", err)
	}
	copy(dst.Pix, unsafe.Slice((*byte)(s.pixels), len(dst.Pix)))
	return nil
}

func (s *Screen) Close() {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.old != 0 {
		selectObj.Call(s.memDC, s.old)
		s.old = 0
	}
	if s.bmp != 0 {
		deleteObj.Call(s.bmp)
		s.bmp = 0
	}
	if s.memDC != 0 {
		deleteDC.Call(s.memDC)
		s.memDC = 0
	}
	if s.screen != 0 {
		releaseDC.Call(0, s.screen)
		s.screen = 0
	}
	s.pixels = nil
}

// VirtualScreen returns the virtual-desktop rectangle, which the injector needs
// to convert monitor-local coordinates back to absolute ones.
func VirtualScreen() (x, y, w, h int) {
	EnableDPIAwareness()
	rx, _, _ := getMetric.Call(smXScreen)
	ry, _, _ := getMetric.Call(smYScreen)
	rw, _, _ := getMetric.Call(smCXScreen)
	rh, _, _ := getMetric.Call(smCYScreen)
	return int(rx), int(ry), int(rw), int(rh)
}
