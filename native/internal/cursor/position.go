// Package cursor contains platform-independent cursor geometry used by capture backends.
package cursor

// DrawPosition converts a virtual-desktop cursor hotspot into the top-left of
// the cursor image within a captured monitor's local pixel coordinates.
func DrawPosition(monitorX, monitorY, cursorX, cursorY int32, hotspotX, hotspotY uint32) (int32, int32) {
	return cursorX - monitorX - int32(hotspotX), cursorY - monitorY - int32(hotspotY)
}
