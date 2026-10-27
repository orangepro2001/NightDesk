package cursor

import "testing"

func TestDrawPositionUsesMonitorOriginAndCursorHotspot(t *testing.T) {
	for _, tc := range []struct {
		name               string
		monitorX, monitorY int32
		cursorX, cursorY   int32
		hotspotX, hotspotY uint32
		wantX, wantY       int32
	}{
		{name: "primary", cursorX: 120, cursorY: 90, hotspotX: 4, hotspotY: 6, wantX: 116, wantY: 84},
		{name: "secondary", monitorX: 1920, cursorX: 2000, cursorY: 40, hotspotX: 8, hotspotY: 3, wantX: 72, wantY: 37},
		{name: "negative origin", monitorX: -1280, monitorY: -200, cursorX: -1200, cursorY: -100, hotspotX: 5, hotspotY: 9, wantX: 75, wantY: 91},
	} {
		t.Run(tc.name, func(t *testing.T) {
			x, y := DrawPosition(tc.monitorX, tc.monitorY, tc.cursorX, tc.cursorY, tc.hotspotX, tc.hotspotY)
			if x != tc.wantX || y != tc.wantY {
				t.Fatalf("DrawPosition() = (%d, %d), want (%d, %d)", x, y, tc.wantX, tc.wantY)
			}
		})
	}
}
