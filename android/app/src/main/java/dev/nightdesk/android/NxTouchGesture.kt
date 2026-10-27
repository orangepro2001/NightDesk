package dev.nightdesk.android

/** Input commands emitted by the mobile remote-control touch reducer. */
internal sealed interface NxTouchAction {
    data class Move(val x: Int, val y: Int) : NxTouchAction
    data class Click(val x: Int, val y: Int) : NxTouchAction
    data class BeginDrag(val x: Int, val y: Int) : NxTouchAction
    data class EndDrag(val x: Int, val y: Int) : NxTouchAction
    data class Scroll(val delta: Int) : NxTouchAction
    data object ReleaseAll : NxTouchAction
}

/**
 * Touch interaction: movement moves the pointer, a short tap clicks, a long press
 * begins a drag, and a second pointer switches the gesture to scrolling.
 */
internal class NxTouchGesture(
    private val longPressMillis: Long = 450L,
    private val moveSlopPx: Float = 12f,
    private val wheelStepPx: Float = 28f
) {
    private enum class State { Idle, TapPending, Moving, Dragging, Scrolling }

    private var state = State.Idle
    private var downX = 0f
    private var downY = 0f
    private var downAtMillis = 0L
    private var lastPoint: NxPoint? = null
    private var lastScrollY = 0f
    private var scrollAccumulator = 0f

    val isInputHeld: Boolean get() = state == State.Dragging
    val isScrolling: Boolean get() = state == State.Scrolling
    val isTapPending: Boolean get() = state == State.TapPending

    fun onDown(x: Float, y: Float, point: NxPoint, timeMillis: Long): List<NxTouchAction> {
        val cleanup = if (state == State.Idle) emptyList() else onCancel()
        state = State.TapPending
        downX = x
        downY = y
        downAtMillis = timeMillis
        lastPoint = point
        return cleanup + NxTouchAction.Move(point.x, point.y)
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
            State.Idle, State.Scrolling -> emptyList()
        }
    }

    fun onLongPress(timeMillis: Long): List<NxTouchAction> {
        if (state != State.TapPending || timeMillis - downAtMillis < longPressMillis) return emptyList()
        state = State.Dragging
        val point = lastPoint ?: return emptyList()
        return listOf(NxTouchAction.BeginDrag(point.x, point.y))
    }

    fun onPointerDown(centroidY: Float): List<NxTouchAction> {
        if (state == State.Idle) return emptyList()
        val actions = if (state == State.Dragging) {
            lastPoint?.let { listOf(NxTouchAction.EndDrag(it.x, it.y)) }.orEmpty()
        } else {
            emptyList()
        }
        state = State.Scrolling
        lastScrollY = centroidY
        scrollAccumulator = 0f
        return actions
    }

    fun onTwoFingerMove(centroidY: Float): List<NxTouchAction> {
        if (state != State.Scrolling) return emptyList()
        scrollAccumulator += lastScrollY - centroidY
        lastScrollY = centroidY
        val steps = (scrollAccumulator / wheelStepPx).toInt()
        if (steps == 0) return emptyList()
        scrollAccumulator -= steps * wheelStepPx
        return listOf(NxTouchAction.Scroll(steps * 120))
    }

    fun onPointerUp(): List<NxTouchAction> = emptyList()

    fun onUp(point: NxPoint?): List<NxTouchAction> {
        val target = point ?: lastPoint
        val action = when (state) {
            State.TapPending -> target?.let { NxTouchAction.Click(it.x, it.y) }
            State.Dragging -> target?.let { NxTouchAction.EndDrag(it.x, it.y) }
            State.Idle, State.Moving, State.Scrolling -> null
        }
        reset()
        return action?.let(::listOf).orEmpty()
    }

    fun onCancel(): List<NxTouchAction> {
        reset()
        return listOf(NxTouchAction.ReleaseAll)
    }

    fun onReleaseAll(): List<NxTouchAction> = onCancel()

    private fun reset() {
        state = State.Idle
        lastPoint = null
        scrollAccumulator = 0f
    }

    companion object {
        const val DefaultLongPressMillis = 450L
    }
}
