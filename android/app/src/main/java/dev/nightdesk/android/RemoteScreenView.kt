package dev.nightdesk.android

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.RectF
import android.util.AttributeSet
import android.view.View
import java.nio.ByteBuffer

/**
 * Draws a persistent BGRA host frame. Go sends a full frame first and then only
 * dirty rectangles; the bitmap therefore must survive across OnFrameBegin calls
 * when the dimensions are unchanged.
 */
class RemoteScreenView @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null
) : View(context, attrs) {
    private val lock = Any()
    private val paint = Paint(Paint.FILTER_BITMAP_FLAG or Paint.DITHER_FLAG)
    private var frame: Bitmap? = null
    private var frameCanvas: Canvas? = null
    private var patch: Bitmap? = null
    private var patchCanvas: Canvas? = null
    private var hostWidth = 0
    private var hostHeight = 0

    @Volatile
    var lastPoint: NxPoint? = null
        private set

    var onViewportChanged: ((Int, Int) -> Unit)? = null

    override fun onSizeChanged(w: Int, h: Int, oldw: Int, oldh: Int) {
        super.onSizeChanged(w, h, oldw, oldh)
        onViewportChanged?.invoke(w, h)
    }

    fun beginFrame(width: Int, height: Int) {
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384) return
        synchronized(lock) {
            if (frame == null || hostWidth != width || hostHeight != height) {
                frame?.recycle()
                frame = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888).apply {
                    setHasAlpha(false)
                }
                frameCanvas = Canvas(frame!!).apply { drawColor(Color.BLACK) }
                patch?.recycle()
                patch = null
                patchCanvas = null
                hostWidth = width
                hostHeight = height
                lastPoint = null
            }
        }
        postInvalidateOnAnimation()
    }

    fun applyTile(x: Int, y: Int, width: Int, height: Int, pixels: ByteArray) {
        if (width <= 0 || height <= 0 || pixels.size < width * height * 4) return
        synchronized(lock) {
            val target = frame ?: return
            val targetCanvas = frameCanvas ?: return
            if (x < 0 || y < 0 || x + width > hostWidth || y + height > hostHeight) return

            // Go/GDI sends BGRA; ARGB_8888's packed buffer layout on Android is
            // RGBA. Convert channels and discard GDI's undefined alpha together.
            for (index in 0 until width * height * 4 step 4) {
                val blue = pixels[index]
                pixels[index] = pixels[index + 2]
                pixels[index + 2] = blue
                pixels[index + 3] = 0xFF.toByte()
            }

            if (patch == null || patch!!.width != width || patch!!.height != height) {
                patch?.recycle()
                patch = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888).apply {
                    setHasAlpha(false)
                }
                patchCanvas = Canvas(patch!!)
            }
            val source = ByteBuffer.wrap(pixels, 0, width * height * 4)
            patch!!.copyPixelsFromBuffer(source)
            targetCanvas.drawBitmap(patch!!, x.toFloat(), y.toFloat(), paint)
        }
        postInvalidateOnAnimation()
    }

    fun clear() {
        synchronized(lock) {
            frame?.recycle()
            frame = null
            frameCanvas = null
            patch?.recycle()
            patch = null
            patchCanvas = null
            hostWidth = 0
            hostHeight = 0
            lastPoint = null
        }
        postInvalidate()
    }

    fun mapToHost(x: Float, y: Float): NxPoint? {
        val point = synchronized(lock) {
            NxGeometry.mapTouch(width, height, hostWidth, hostHeight, x, y)
        }
        if (point != null) lastPoint = point
        return point
    }

    fun centerPoint(): NxPoint? = synchronized(lock) {
        if (hostWidth <= 0 || hostHeight <= 0) null else NxPoint(hostWidth / 2, hostHeight / 2)
    }

    override fun onDraw(canvas: Canvas) {
        canvas.drawColor(Color.BLACK)
        synchronized(lock) {
            val bitmap = frame ?: return
            val viewport = NxGeometry.fit(width, height, bitmap.width, bitmap.height) ?: return
            canvas.drawBitmap(
                bitmap,
                null,
                RectF(viewport.left, viewport.top, viewport.right, viewport.bottom),
                paint
            )
        }
    }
}
