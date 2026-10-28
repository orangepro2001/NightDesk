package dev.nightdesk.android

import android.content.Context
import kotlin.math.hypot
import kotlin.math.roundToInt

/** How one-finger input reaches the remote cursor. */
internal enum class NxMouseMode {
    /** The touched point is the remote point: natural for direct manipulation. */
    Direct,

    /** Relative, touchpad-style movement with acceleration and a persistent cursor. */
    Trackpad;

    companion object {
        fun from(value: String?): NxMouseMode =
            values().firstOrNull { it.name.equals(value, ignoreCase = true) } ?: Direct
    }
}

internal class NxRemotePreferences(context: Context) {
    private val store = context.applicationContext.getSharedPreferences("nx_remote", Context.MODE_PRIVATE)

    var mouseMode: NxMouseMode
        get() = NxMouseMode.from(store.getString(KeyMouseMode, null))
        set(value) = store.edit().putString(KeyMouseMode, value.name).apply()

    private companion object {
        const val KeyMouseMode = "mouse_mode"
    }
}

/**
 * Keeps the remote cursor for trackpad mode and maps absolute touches for direct
 * mode. This is deliberately free of Android APIs so the movement curve and
 * clamping can be tested on the JVM.
 */
internal class NxPointer {
    var mode: NxMouseMode = NxMouseMode.Direct

    private var hostWidth = 0
    private var hostHeight = 0
    private var viewWidth = 0
    private var viewHeight = 0
    private var cursorX = 0
    private var cursorY = 0
    private var cursorKnown = false

    @Synchronized
    fun onFrame(width: Int, height: Int) {
        if (width <= 0 || height <= 0) {
            hostWidth = 0
            hostHeight = 0
            cursorKnown = false
            return
        }
        if (cursorKnown && hostWidth > 0 && hostHeight > 0) {
            cursorX = scaleCursor(cursorX, hostWidth, width)
            cursorY = scaleCursor(cursorY, hostHeight, height)
        }
        hostWidth = width
        hostHeight = height
        if (!cursorKnown) {
            cursorX = width / 2
            cursorY = height / 2
            cursorKnown = true
        }
    }

    @Synchronized
    fun onViewSize(width: Int, height: Int) {
        viewWidth = width
        viewHeight = height
    }

    @Synchronized
    fun cursorPoint(): NxPoint? =
        if (cursorKnown && hostWidth > 0 && hostHeight > 0) NxPoint(cursorX, cursorY) else null

    @Synchronized
    fun modeChanged(mode: NxMouseMode) {
        this.mode = mode
        if (mode == NxMouseMode.Trackpad) {
            cursorPoint()?.let { cursorX = it.x; cursorY = it.y }
        }
    }

    /** Maps a direct touch. Returns null over letterboxing or before the first frame. */
    @Synchronized
    fun directPoint(screenX: Float, screenY: Float): NxPoint? {
        val point = NxGeometry.mapTouch(viewWidth, viewHeight, hostWidth, hostHeight, screenX, screenY)
            ?: return null
        cursorX = point.x
        cursorY = point.y
        cursorKnown = true
        return point
    }

    /**
     * Moves the tracked cursor by a screen-space delta. Slow movements keep a
     * near 1:1 precision; fast flicks gain speed so a short swipe can cross a
     * large desktop.
     */
    @Synchronized
    fun moveRelativeBy(dxScreen: Float, dyScreen: Float, elapsedMillis: Long): NxPoint? {
        if (hostWidth <= 0 || hostHeight <= 0) return null
        val viewport = NxGeometry.fit(viewWidth, viewHeight, hostWidth, hostHeight) ?: return null
        if (!cursorKnown) {
            cursorX = hostWidth / 2
            cursorY = hostHeight / 2
            cursorKnown = true
        }
        val hostPerViewPx = hostWidth / viewport.width
        val gain = NxPointerAcceleration.gain(dxScreen, dyScreen, elapsedMillis)
        val nextX = cursorX + (dxScreen * hostPerViewPx * gain).roundToInt()
        val nextY = cursorY + (dyScreen * hostPerViewPx * gain).roundToInt()
        cursorX = nextX.coerceIn(0, hostWidth - 1)
        cursorY = nextY.coerceIn(0, hostHeight - 1)
        return NxPoint(cursorX, cursorY)
    }

    private fun scaleCursor(value: Int, oldSize: Int, newSize: Int): Int =
        if (oldSize <= 0) newSize / 2 else (value.toLong() * newSize / oldSize).toInt().coerceIn(0, newSize - 1)
}

internal object NxPointerAcceleration {
    const val SlowGain = 1.15f
    const val FastGain = 3.35f

    /** Pointer acceleration curve: speed in screen px/ms, squared for a soft knee. */
    fun gain(dxPx: Float, dyPx: Float, elapsedMillis: Long): Float {
        if (elapsedMillis <= 0) return SlowGain
        val speed = hypot(dxPx.toDouble(), dyPx.toDouble()).toFloat() / elapsedMillis
        val normalized = (speed / SpeedAtFullGain).coerceIn(0f, 1f)
        return SlowGain + (FastGain - SlowGain) * normalized * normalized
    }

    private const val SpeedAtFullGain = 1.1f
}
