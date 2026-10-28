package dev.nightdesk.android

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class RemoteGestureTest {
    private val gesture = NxTouchGesture(longPressMillis = 400L, moveSlopPx = 12f)

    @Test
    fun shortStationaryTouchBecomesOneBalancedLeftClick() {
        assertEquals(listOf(NxTouchAction.Move(100, 200)), gesture.onDown(100f, 200f, NxPoint(100, 200), 0L))
        assertEquals(listOf(NxTouchAction.Click(100, 200)), gesture.onUp(NxPoint(100, 200)))
        assertFalse(gesture.isInputHeld)
    }

    @Test
    fun tapWithoutAFreshPointClicksTheLastKnownCursor() {
        gesture.onDown(100f, 200f, NxPoint(100, 200), 0L)
        assertEquals(listOf(NxTouchAction.Click(100, 200)), gesture.onUp(null))
    }

    @Test
    fun ordinaryFingerMovementMovesCursorWithoutHoldingLeftButton() {
        gesture.onDown(100f, 200f, NxPoint(100, 200), 0L)
        assertEquals(listOf(NxTouchAction.Move(160, 240)), gesture.onMove(160f, 240f, NxPoint(160, 240), 80L))
        assertEquals(emptyList<NxTouchAction>(), gesture.onUp(NxPoint(160, 240)))
        assertFalse(gesture.isInputHeld)
    }

    @Test
    fun longPressStartsDragAndReleaseBalancesMouseDown() {
        gesture.onDown(100f, 200f, NxPoint(100, 200), 0L)
        assertEquals(listOf(NxTouchAction.BeginDrag(100, 200)), gesture.onLongPress(400L))
        assertTrue(gesture.isInputHeld)
        assertEquals(listOf(NxTouchAction.Move(150, 220)), gesture.onMove(150f, 220f, NxPoint(150, 220), 450L))
        assertEquals(listOf(NxTouchAction.EndDrag(150, 220)), gesture.onUp(NxPoint(150, 220)))
        assertFalse(gesture.isInputHeld)
    }

    @Test
    fun movementBeforeLongPressCancelsDragAndDoesNotClickOnRelease() {
        gesture.onDown(100f, 200f, NxPoint(100, 200), 0L)
        assertEquals(listOf(NxTouchAction.Move(130, 200)), gesture.onMove(130f, 200f, NxPoint(130, 200), 100L))
        assertEquals(emptyList<NxTouchAction>(), gesture.onLongPress(400L))
        assertEquals(emptyList<NxTouchAction>(), gesture.onUp(NxPoint(130, 200)))
    }

    @Test
    fun delayedMoveAtLongPressBoundaryStartsDragBeforeMoving() {
        gesture.onDown(100f, 200f, NxPoint(100, 200), 0L)
        assertEquals(
            listOf(NxTouchAction.BeginDrag(130, 200), NxTouchAction.Move(130, 200)),
            gesture.onMove(130f, 200f, NxPoint(130, 200), 400L)
        )
        assertTrue(gesture.isInputHeld)
    }

    @Test
    fun quickTwoFingerTapRightClicksWithoutStartingAScroll() {
        gesture.onDown(100f, 200f, NxPoint(100, 200), 0L)
        assertEquals(emptyList<NxTouchAction>(), gesture.onPointerDown(160f, 240f, 40L))
        assertTrue(gesture.isTwoFingerGesture)
        assertEquals(emptyList<NxTouchAction>(), gesture.onTwoFingerMove(162f, 242f, 80L))
        assertEquals(listOf(NxTouchAction.RightClick(100, 200)), gesture.onPointerUp(200L, 1))
        assertEquals(emptyList<NxTouchAction>(), gesture.onUp(NxPoint(100, 200)))
        assertFalse(gesture.isScrolling)
    }

    @Test
    fun twoFingerMovementBecomesVerticalScrollInsteadOfRightClick() {
        gesture.onDown(100f, 200f, NxPoint(100, 200), 0L)
        gesture.onPointerDown(100f, 200f, 100L)
        assertEquals(emptyList<NxTouchAction>(), gesture.onTwoFingerMove(100f, 170f, 120L))
        assertEquals(listOf(NxTouchAction.Scroll(120)), gesture.onTwoFingerMove(100f, 130f, 300L))
        assertTrue(gesture.isScrolling)
        assertEquals(emptyList<NxTouchAction>(), gesture.onPointerUp(350L, 1))
    }

    @Test
    fun twoFingerMovementScrollsHorizontallyToo() {
        gesture.onDown(100f, 200f, NxPoint(100, 200), 0L)
        gesture.onPointerDown(100f, 200f, 100L)
        assertEquals(emptyList<NxTouchAction>(), gesture.onTwoFingerMove(130f, 200f, 120L))
        assertEquals(listOf(NxTouchAction.ScrollH(120)), gesture.onTwoFingerMove(170f, 200f, 140L))
    }

    @Test
    fun liftingAfterAScrollRequestsInertialScrolling() {
        gesture.onDown(100f, 200f, NxPoint(100, 200), 0L)
        gesture.onPointerDown(100f, 200f, 100L)
        assertEquals(emptyList<NxTouchAction>(), gesture.onTwoFingerMove(100f, 170f, 120L))
        assertTrue(gesture.onTwoFingerMove(100f, 90f, 140L).isNotEmpty())
        val fling = gesture.onPointerUp(160L, 1).single() as NxTouchAction.Fling
        assertTrue("expected upward fling, got $fling", fling.velocityY > 0f)
    }

    @Test
    fun secondPointerDuringDragReleasesLeftBeforeScrolling() {
        gesture.onDown(100f, 200f, NxPoint(100, 200), 0L)
        gesture.onLongPress(400L)
        assertEquals(listOf(NxTouchAction.EndDrag(100, 200)), gesture.onPointerDown(100f, 200f, 420L))
        assertFalse(gesture.isInputHeld)
        assertTrue(gesture.isTwoFingerGesture)
    }

    @Test
    fun draggingSecondPointerStartsScrollOnlyAfterSlop() {
        gesture.onDown(100f, 200f, NxPoint(100, 200), 0L)
        gesture.onLongPress(400L)
        gesture.onPointerDown(100f, 200f, 420L)
        assertEquals(emptyList<NxTouchAction>(), gesture.onTwoFingerMove(100f, 210f, 440L))
        assertEquals(emptyList<NxTouchAction>(), gesture.onTwoFingerMove(100f, 170f, 460L))
        assertEquals(listOf(NxTouchAction.Scroll(120)), gesture.onTwoFingerMove(100f, 130f, 480L))
    }

    @Test
    fun cancelAndExplicitReleaseAlwaysReturnToIdle() {
        gesture.onDown(100f, 200f, NxPoint(100, 200), 0L)
        gesture.onLongPress(400L)
        assertTrue(gesture.isInputHeld)
        assertEquals(listOf(NxTouchAction.ReleaseAll), gesture.onCancel())
        assertFalse(gesture.isInputHeld)
        assertEquals(emptyList<NxTouchAction>(), gesture.onUp(NxPoint(100, 200)))

        gesture.onDown(100f, 200f, NxPoint(100, 200), 600L)
        gesture.onLongPress(1000L)
        assertEquals(listOf(NxTouchAction.ReleaseAll), gesture.onReleaseAll())
        assertFalse(gesture.isInputHeld)
    }

    @Test
    fun releasingOutsideLetterboxedFrameStillEndsAnActiveDrag() {
        gesture.onDown(100f, 200f, NxPoint(100, 200), 0L)
        assertEquals(listOf(NxTouchAction.BeginDrag(100, 200)), gesture.onLongPress(400L))
        assertEquals(listOf(NxTouchAction.EndDrag(100, 200)), gesture.onUp(null))
        assertFalse(gesture.isInputHeld)
    }
}
