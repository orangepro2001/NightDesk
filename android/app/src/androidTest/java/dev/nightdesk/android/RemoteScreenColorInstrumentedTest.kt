package dev.nightdesk.android

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.view.View
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertEquals
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class RemoteScreenColorInstrumentedTest {
    @Test
    fun rendersBgraColorsAndIgnoresUndefinedDibAlpha() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val context = instrumentation.targetContext
        val view = RemoteScreenView(context)
        val hostPixels = byteArrayOf(
            0, 0, 0xFF.toByte(), 0,             // red, alpha undefined
            0, 0xFF.toByte(), 0, 0x7F,           // green, alpha undefined
            0xFF.toByte(), 0, 0, 0,             // blue, alpha undefined
            0x33, 0x80.toByte(), 0xD1.toByte(), 0xFF.toByte() // distinct BGRA color
        )

        instrumentation.runOnMainSync {
            view.measure(
                View.MeasureSpec.makeMeasureSpec(4, View.MeasureSpec.EXACTLY),
                View.MeasureSpec.makeMeasureSpec(1, View.MeasureSpec.EXACTLY)
            )
            view.layout(0, 0, 4, 1)
            view.beginFrame(4, 1)
            view.applyTile(0, 0, 4, 1, hostPixels)
        }

        val rendered = Bitmap.createBitmap(4, 1, Bitmap.Config.ARGB_8888)
        instrumentation.runOnMainSync { view.draw(Canvas(rendered)) }
        assertEquals(Color.RED, rendered.getPixel(0, 0))
        assertEquals(Color.GREEN, rendered.getPixel(1, 0))
        assertEquals(Color.BLUE, rendered.getPixel(2, 0))
        assertEquals(Color.rgb(0xD1, 0x80, 0x33), rendered.getPixel(3, 0))
        rendered.recycle()
        instrumentation.runOnMainSync { view.clear() }
    }
}
