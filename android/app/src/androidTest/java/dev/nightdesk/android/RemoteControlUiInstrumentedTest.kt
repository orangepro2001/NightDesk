package dev.nightdesk.android

import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.atomic.AtomicInteger

@RunWith(AndroidJUnit4::class)
class RemoteControlUiInstrumentedTest {
    @Test
    fun gestureHelpAndReleaseButtonAreVisibleAndUsable() {
        ActivityScenario.launch(MainActivity::class.java).use { scenario ->
            val releaseCount = AtomicInteger()
            lateinit var controls: RemoteTouchControls
            scenario.onActivity { activity ->
                val parent = activity.findViewById<ViewGroup>(android.R.id.content)
                controls = RemoteTouchControls(activity) { releaseCount.incrementAndGet() }
                parent.addView(controls, FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT,
                    ViewGroup.LayoutParams.WRAP_CONTENT,
                    android.view.Gravity.BOTTOM
                ))
            }
            InstrumentationRegistry.getInstrumentation().waitForIdleSync()
            scenario.onActivity { activity ->
                assertTrue("gesture help is missing", controls.gestureHint.text.contains("长按拖动"))
                assertTrue("release control is not visible", controls.releaseButton.isShown && controls.releaseButton.isEnabled)
                assertTrue("release control is too small", controls.releaseButton.width >= 48 * activity.resources.displayMetrics.density)
                controls.releaseButton.performClick()
                assertTrue("release action did not fire", releaseCount.get() == 1)
            }
        }
    }
}
