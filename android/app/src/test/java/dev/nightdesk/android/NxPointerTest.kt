package dev.nightdesk.android

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class NxPointerTest {
    @Test
    fun directModeMapsTheVisibleFrameAndRejectsLetterboxing() {
        val pointer = NxPointer()
        pointer.onViewSize(1000, 1000)
        pointer.onFrame(2000, 1000)
        assertEquals(NxPoint(0, 0), pointer.directPoint(0f, 250f))
        assertEquals(NxPoint(1998, 998), pointer.directPoint(999f, 749f))
        assertNull(pointer.directPoint(500f, 100f))
    }

    @Test
    fun trackpadModeStartsCenteredAndClampsAtEdges() {
        val pointer = NxPointer().apply { modeChanged(NxMouseMode.Trackpad) }
        pointer.onViewSize(1000, 1000)
        pointer.onFrame(1000, 1000)
        assertEquals(NxPoint(500, 500), pointer.cursorPoint())
        assertEquals(NxPoint(999, 999), pointer.moveRelativeBy(100000f, 100000f, 10L))
        assertEquals(NxPoint(0, 0), pointer.moveRelativeBy(-100000f, -100000f, 10L))
    }

    @Test
    fun fastMovementGainsMoreThanSlowMovement() {
        val slow = NxPointer().apply { modeChanged(NxMouseMode.Trackpad) }
        slow.onViewSize(1000, 1000)
        slow.onFrame(1000, 1000)
        val slowPoint = slow.moveRelativeBy(40f, 0f, 500L)!!

        val fast = NxPointer().apply { modeChanged(NxMouseMode.Trackpad) }
        fast.onViewSize(1000, 1000)
        fast.onFrame(1000, 1000)
        val fastPoint = fast.moveRelativeBy(40f, 0f, 8L)!!

        assertTrue("fast pointer should travel farther", fastPoint.x > slowPoint.x)
        assertTrue(NxPointerAcceleration.gain(40f, 0f, 8L) > NxPointerAcceleration.gain(40f, 0f, 500L))
    }

    @Test
    fun hostResolutionChangeKeepsTheCursorProportional() {
        val pointer = NxPointer()
        pointer.onViewSize(1000, 1000)
        pointer.onFrame(1000, 1000)
        pointer.directPoint(500f, 500f)
        pointer.onFrame(2000, 2000)
        assertEquals(NxPoint(1000, 1000), pointer.cursorPoint())
    }

    @Test
    fun modeParsingFallsBackToDirect() {
        assertEquals(NxMouseMode.Trackpad, NxMouseMode.from("trackpad"))
        assertEquals(NxMouseMode.Direct, NxMouseMode.from("TRACKPAD_NOPE"))
        assertEquals(NxMouseMode.Direct, NxMouseMode.from(null))
    }
}
