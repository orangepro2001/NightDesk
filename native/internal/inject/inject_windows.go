//go:build windows

// Package inject performs mouse and keyboard input on the local desktop.
package inject

import (
	"errors"
	"syscall"
	"unicode/utf16"
	"unsafe"

	"github.com/orangepro2001/nightdesk/native/internal/capture"
	"github.com/orangepro2001/nightdesk/native/internal/proto"
)

var (
	user32        = syscall.NewLazyDLL("user32.dll")
	sendInput     = user32.NewProc("SendInput")
	mapVirtualKey = user32.NewProc("MapVirtualKeyW")
)

const (
	inputMouse    = 0
	inputKeyboard = 1

	mouseMove        = 0x0001
	mouseLeftDown    = 0x0002
	mouseLeftUp      = 0x0004
	mouseRightDown   = 0x0008
	mouseRightUp     = 0x0010
	mouseMiddleDown  = 0x0020
	mouseMiddleUp    = 0x0040
	mouseWheel       = 0x0800
	mouseAbsolute    = 0x8000
	mouseVirtualDesk = 0x4000

	keyExtended = 0x0001
	keyUp       = 0x0002
	keyUnicode  = 0x0004
)

type mouseInput struct {
	dx, dy int32
	data   uint32
	flags  uint32
	time   uint32
	_      uint32
	extra  uintptr
}

type keybdInput struct {
	vk, scan uint16
	flags    uint32
	time     uint32
	_        uint32
	extra    uintptr
	_        [8]byte // pad to the size of mouseInput
}

type mouseEvent struct {
	typ uint32
	_   uint32
	mi  mouseInput
}

type keyEvent struct {
	typ uint32
	_   uint32
	ki  keybdInput
}

// Injector turns monitor-local input coordinates into desktop actions.
//
// go-nx mapped coordinates onto the primary screen only. Because NightDesk can
// drive a secondary or virtual display, the mapping here is relative to the
// captured monitor inside the whole virtual desktop and uses
// MOUSEEVENTF_VIRTUALDESK.
type Injector struct {
	w, h        int
	originX     int
	originY     int
	virtualW    int
	virtualH    int
	virtualX    int
	virtualY    int
	virtualDesk bool
}

// New builds an injector for a monitor at the virtual-desktop origin.
//
// Deprecated: prefer NewDisplay, which also handles secondary monitors.
func New(w, h int) *Injector {
	x, y, vw, vh := capture.VirtualScreen()
	return NewDisplay(w, h, 0, 0, x, y, vw, vh)
}

// NewDisplay builds an injector for a captured monitor. The monitor's top-left
// corner is (originX, originY) in virtual-desktop coordinates.
func NewDisplay(w, h, originX, originY, virtualX, virtualY, virtualW, virtualH int) *Injector {
	return &Injector{
		w: w, h: h,
		originX: originX, originY: originY,
		virtualX: virtualX, virtualY: virtualY,
		virtualW: max(virtualW, 1), virtualH: max(virtualH, 1),
		virtualDesk: virtualW > 0 && virtualH > 0,
	}
}

// Inject applies one input action.
func (j *Injector) Inject(in proto.Input) error {
	switch in.Kind {
	case proto.KindMouseMove:
		return j.mouse(in, mouseMove, 0)
	case proto.KindMouseDown, proto.KindMouseUp:
		down := in.Kind == proto.KindMouseDown
		flag := map[int32][2]uint32{
			0: {mouseLeftDown, mouseLeftUp},
			1: {mouseRightDown, mouseRightUp},
			2: {mouseMiddleDown, mouseMiddleUp},
		}[in.Arg]
		if flag == ([2]uint32{}) {
			return errors.New("inject: unknown mouse button")
		}
		f := flag[1]
		if down {
			f = flag[0]
		}
		return j.mouse(in, f, 0)
	case proto.KindWheel:
		return j.mouse(in, mouseWheel, uint32(in.Arg))
	case proto.KindKeyDown, proto.KindKeyUp:
		// Arg: virtual-key code in the low byte, 0x100 set for an extended key.
		return key(uint16(in.Arg&0xFF), in.Arg&0x100 != 0, in.Kind == proto.KindKeyUp)
	}
	return errors.New("inject: unknown input kind")
}

// InjectText types literal Unicode text with KEYEVENTF_UNICODE, so it does not
// depend on the host keyboard layout or an IME being open.
func (j *Injector) InjectText(text string) error {
	units := utf16.Encode([]rune(text))
	if len(units) == 0 {
		return nil
	}
	// SendInput is documented to accept arrays; chunk so a huge paste cannot
	// allocate an unbounded stack buffer in the win32 call.
	const chunk = 64
	for start := 0; start < len(units); start += chunk {
		end := min(start+chunk, len(units))
		ev := make([]keyEvent, 0, (end-start)*2)
		for _, u := range units[start:end] {
			ev = append(ev,
				keyEvent{typ: inputKeyboard, ki: keybdInput{scan: u, flags: keyUnicode}},
				keyEvent{typ: inputKeyboard, ki: keybdInput{scan: u, flags: keyUnicode | keyUp}},
			)
		}
		if err := send(unsafe.Pointer(&ev[0]), len(ev), unsafe.Sizeof(ev[0])); err != nil {
			return err
		}
	}
	return nil
}

// mouse moves the cursor to (X, Y) and then applies action in one SendInput
// call. action is mouseMove itself, a button flag or mouseWheel.
func (j *Injector) mouse(in proto.Input, action, data uint32) error {
	x := clamp(int(in.X), j.w)
	y := clamp(int(in.Y), j.h)
	screenX := x + j.originX
	screenY := y + j.originY
	moveFlags := uint32(mouseMove | mouseAbsolute)
	dx, dy := int32(0), int32(0)
	if j.virtualDesk {
		moveFlags |= mouseVirtualDesk
		dx = int32((screenX - j.virtualX) * 65535 / max(j.virtualW-1, 1))
		dy = int32((screenY - j.virtualY) * 65535 / max(j.virtualH-1, 1))
	} else {
		dx = int32(screenX * 65535 / max(j.w-1, 1))
		dy = int32(screenY * 65535 / max(j.h-1, 1))
	}
	ev := [2]mouseEvent{
		{typ: inputMouse, mi: mouseInput{dx: dx, dy: dy, flags: moveFlags}},
		{typ: inputMouse, mi: mouseInput{flags: action, data: data}},
	}
	n := 2
	if action == mouseMove {
		n = 1
	}
	return send(unsafe.Pointer(&ev[0]), n, unsafe.Sizeof(ev[0]))
}

func key(vk uint16, ext, up bool) error {
	scan, _, _ := mapVirtualKey.Call(uintptr(vk), 0)
	var flags uint32
	if up {
		flags |= keyUp
	}
	if ext || extended(vk) {
		flags |= keyExtended
	}
	ev := keyEvent{typ: inputKeyboard, ki: keybdInput{vk: vk, scan: uint16(scan), flags: flags}}
	return send(unsafe.Pointer(&ev), 1, unsafe.Sizeof(ev))
}

func send(p unsafe.Pointer, n int, size uintptr) error {
	r, _, err := sendInput.Call(uintptr(n), uintptr(p), size)
	if int(r) != n {
		return errors.New("inject: SendInput failed: " + err.Error())
	}
	return nil
}

func clamp(v, n int) int { return min(max(v, 0), n-1) }

// extended reports whether vk needs KEYEVENTF_EXTENDEDKEY (arrows, navigation
// block, right-hand Ctrl/Alt, numpad divide, Win keys).
func extended(vk uint16) bool {
	switch vk {
	case 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, // PgUp PgDn End Home arrows
		0x2D, 0x2E, // Insert Delete
		0xA3, 0xA5, // RCtrl RAlt
		0x6F,       // numpad /
		0x5B, 0x5C: // LWin RWin
		return true
	}
	return false
}

// Compile-time check: both events must match sizeof(INPUT) on amd64 (40 bytes).
var (
	_ [40]byte = [unsafe.Sizeof(mouseEvent{})]byte{}
	_ [40]byte = [unsafe.Sizeof(keyEvent{})]byte{}
)
