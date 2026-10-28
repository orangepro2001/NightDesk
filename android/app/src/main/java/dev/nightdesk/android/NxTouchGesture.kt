package dev.nightdesk.android

/** Input commands emitted by the mobile remote-control touch reducer. */
internal sealed interface NxTouchAction {
    data class Move(val x: Int, val y: Int) : NxTouchAction
    data class Click(val x: Int, val y: Int) : NxTouchAction
    data class RightClick(val x: Int, val y: Int) : NxTouchAction
    data class BeginDrag(val x: Int, val y: Int) : NxTouchAction
    data class EndDrag(val x: Int, val y: Int) : NxTouchAction
    data class Scroll(val delta: Int) : NxTouchAction
    data class ScrollH(val delta: Int) : NxTouchAction
    /** Screen-space velocity in px/ms, used for inertial scrolling. */
    data class Fling(val velocityX: Float, val velocityY: Float) : NxTouchAction
    data object ReleaseAll : NxTouchAction
}

/**
 * Touch interaction:
 * - a short tap clicks, a long press begins a drag
 * - one-finger movement moves the pointer (absolute or trackpad depending on the mode)
 * - a quick two-finger tap right-clicks
 * - two-finger movement scrolls horizontally and vertically, with fling velocity on release
 */
internal class NxTouchGesture(
    private val longPressMillis: Long = DefaultLongPressMillis,
    private val moveSlopPx: Float = 12f,
    private val wheelStepPx: Float = 28f,
    private val twoFingerTapMillis: Long = 320L,
    private val scrollStartSlopPx: Float = 18f
) {
    private enum class State { Idle, TapPending, Moving, Dragging, TwoFingerPending, Scrolling, Consumed }

    private var state = State.Idle
    private var downX = 0f
    private var downY = 0f
    private var downAtMillis = 0L
    private var twoDownAtMillis = 0L
    private var twoStartX = 0f
    private var twoStartY = 0f
    private var lastPoint: NxPoint? = null
    private var lastScrollX = 0f
    private var lastScrollY = 0f
    private var lastScrollAtMillis = 0L
    private var scrollAccumulatorX = 0f
    private var scrollAccumulatorY = 0f
    private var scrollVelocityX = 0f
    private var scrollVelocityY = 0f

    val isInputHeld: Boolean get() = state == State.Dragging
    val isScrolling: Boolean get() = state == State.Scrolling
    val isTwoFingerGesture: Boolean get() = state == State.TwoFingerPending || state == State.Scrolling
    val isTapPending: Boolean get() = state == State.TapPending

    fun onDown(x: Float, y: Float, point: NxPoint?, timeMillis: Long): List<NxTouchAction> {
        val cleanup = if (state == State.Idle) emptyList() else onCancel()
        state = State.TapPending
        downX = x
        downY = y
        downAtMillis = timeMillis
        lastPoint = point
        return cleanup + listOfNotNull(point?.let { NxTouchAction.Move(it.x, it.y) })
    }

    fun onMove(x: Float, y: Float, point: NxPoint?, timeMillis: Long): List<NxTouchAction> {
        if (point != null) lastPoint = point
        return when (state) {
            State.TapPending -> {
                val dx = x - downX
                val dy = y - downY
                if (timeMillis - downAtMillis >= longPressMillis) {
                    state = State.Dragging
                    buildList {
                        lastPoint?.let { add(NxTouchAction.BeginDrag(it.x, it.y)) }
                        point?.let { add(NxTouchAction.Move(it.x, it.y)) }
                    }
                } else if (dx * dx + dy * dy <= moveSlopPx * moveSlopPx) {
                    emptyList()
                } else {
                    state = State.Moving
                    point?.let { listOf(NxTouchAction.Move(it.x, it.y)) }.orEmpty()
                }
            }
            State.Moving, State.Dragging ->
                point?.let { listOf(NxTouchAction.Move(it.x, it.y)) }.orEmpty()
            State.Idle, State.TwoFingerPending, State.Scrolling, State.Consumed -> emptyList()
        }
    }

    fun onLongPress(timeMillis: Long): List<NxTouchAction> {
        if (state != State.TapPending || timeMillis - downAtMillis < longPressMillis) return emptyList()
        state = State.Dragging
        val point = lastPoint ?: return emptyList()
        return listOf(NxTouchAction.BeginDrag(point.x, point.y))
    }

    fun onPointerDown(centroidX: Float, centroidY: Float, timeMillis: Long): List<NxTouchAction> {
        val actions = if (state == State.Dragging) {
            lastPoint?.let { listOf(NxTouchAction.EndDrag(it.x, it.y)) }.orEmpty()
        } else {
            emptyList()
        }
        state = State.TwoFingerPending
        twoDownAtMillis = timeMillis
        twoStartX = centroidX
        twoStartY = centroidY
        lastScrollX = centroidX
        lastScrollY = centroidY
        lastScrollAtMillis = timeMillis
        scrollAccumulatorX = 0f
        scrollAccumulatorY = 0f
        scrollVelocityX = 0f
        scrollVelocityY = 0f
        return actions
    }

    fun onTwoFingerMove(centroidX: Float, centroidY: Float, timeMillis: Long): List<NxTouchAction> {
        when (state) {
            State.TwoFingerPending -> {
                val dx = centroidX - twoStartX
                val dy = centroidY - twoStartY
                if (dx * dx + dy * dy >= scrollStartSlopPx * scrollStartSlopPx) {
                    state = State.Scrolling
                    lastScrollX = centroidX
                    lastScrollY = centroidY
                    lastScrollAtMillis = timeMillis
                }
                return emptyList()
            }
            State.Scrolling -> Unit
            else -> return emptyList()
        }

        val dx = centroidX - lastScrollX
        val dy = centroidY - lastScrollY
        val elapsed = (timeMillis - lastScrollAtMillis).coerceAtLeast(1L)
        scrollVelocityX = scrollVelocityX * 0.6f + (dx / elapsed) * 0.4f
        scrollVelocityY = scrollVelocityY * 0.6f + (-dy / elapsed) * 0.4f
        lastScrollX = centroidX
        lastScrollY = centroidY
        lastScrollAtMillis = timeMillis

        // Finger up (negative dy) is a positive wheel notch, matching mouse-wheel feel.
        scrollAccumulatorY += -dy
        scrollAccumulatorX += dx

        return buildList {
            val verticalSteps = (scrollAccumulatorY / wheelStepPx).toInt()
            if (verticalSteps != 0) {
                scrollAccumulatorY -= verticalSteps * wheelStepPx
                add(NxTouchAction.Scroll(verticalSteps * WheelDelta))
            }
            val horizontalSteps = (scrollAccumulatorX / wheelStepPx).toInt()
            if (horizontalSteps != 0) {
                scrollAccumulatorX -= horizontalSteps * wheelStepPx
                add(NxTouchAction.ScrollH(horizontalSteps * WheelDelta))
            }
        }
    }

    /**
     * Called on ACTION_POINTER_UP. Returns a right click for a quick, unmoved
     * two-finger tap, or a fling after a genuine scroll.
     */
    fun onPointerUp(timeMillis: Long, remainingPointers: Int): List<NxTouchAction> {
        val action = when (state) {
            State.TwoFingerPending ->
                if (timeMillis - twoDownAtMillis <= twoFingerTapMillis) rightClick() else null
            State.Scrolling -> {
                val speed = kotlin.math.hypot(scrollVelocityX.toDouble(), scrollVelocityY.toDouble()).toFloat()
                if (speed >= MinFlingSpeed) NxTouchAction.Fling(scrollVelocityX, scrollVelocityY) else null
            }
            else -> null
        }
        if (state == State.TwoFingerPending || state == State.Scrolling) {
            state = State.Consumed
        }
        if (remainingPointers <= 1) {
            // Scroll velocity belongs to this gesture only; never leak it to the next one.
            scrollVelocityX = 0f
            scrollVelocityY = 0f
        }
        return action?.let(::listOf).orEmpty()
    }

    fun onUp(point: NxPoint?): List<NxTouchAction> {
        val target = point ?: lastPoint
        val action = when (state) {
            State.TapPending -> target?.let { NxTouchAction.Click(it.x, it.y) }
            State.Dragging -> target?.let { NxTouchAction.EndDrag(it.x, it.y) }
            State.TwoFingerPending -> rightClick()
            State.Idle, State.Moving, State.Scrolling, State.Consumed -> null
        }
        reset()
        return action?.let(::listOf).orEmpty()
    }

    fun onCancel(): List<NxTouchAction> {
        reset()
        return listOf(NxTouchAction.ReleaseAll)
    }

    fun onReleaseAll(): List<NxTouchAction> = onCancel()

    private fun rightClick(): NxTouchAction? =
        lastPoint?.let { NxTouchAction.RightClick(it.x, it.y) }

    private fun reset() {
        state = State.Idle
        lastPoint = null
        scrollAccumulatorX = 0f
        scrollAccumulatorY = 0f
        scrollVelocityX = 0f
        scrollVelocityY = 0f
    }

    companion object {
        const val DefaultLongPressMillis = 450L
        const val WheelDelta = 120
        const val MinFlingSpeed = 0.12f
    }
}
