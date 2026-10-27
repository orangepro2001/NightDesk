package dev.nightdesk.android

import android.view.Gravity
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.ScrollView
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.atomic.AtomicInteger

@RunWith(AndroidJUnit4::class)
class RemoteControlUiInstrumentedTest {
    @Test
    fun toolbarIsCompactUntilOpenedAndReleaseRemainsReachable() {
        ActivityScenario.launch(MainActivity::class.java).use { scenario ->
            val releaseCount = AtomicInteger()
            lateinit var controls: RemoteTouchControls
            scenario.onActivity { activity ->
                val parent = activity.findViewById<ViewGroup>(android.R.id.content)
                val density = activity.resources.displayMetrics.density
                controls = RemoteTouchControls(activity) { action, _ ->
                    if (action == RemoteControlAction.Toggle) controls.toggleMenu()
                    else {
                        if (action == RemoteControlAction.ReleaseAll) releaseCount.incrementAndGet()
                        controls.collapseMenu()
                    }
                }
                parent.addView(controls, FrameLayout.LayoutParams(
                    (48 * density).toInt(),
                    (64 * density).toInt(),
                    Gravity.END or Gravity.CENTER_VERTICAL
                ))
            }
            scenario.onActivity { activity ->
                val density = activity.resources.displayMetrics.density
                assertTrue("idle toolbar is not a small edge handle", controls.width <= (52 * density).toInt())
                assertTrue("edge handle is not available", controls.menuButton.isShown && controls.menuButton.isClickable)
                assertTrue("keyboard access is not discoverable", controls.menuButton.contentDescription.toString().contains("F1"))
                controls.menuButton.performClick()
            }
            InstrumentationRegistry.getInstrumentation().waitForIdleSync()
            scenario.onActivity { activity ->
                val density = activity.resources.displayMetrics.density
                assertTrue("side menu did not expand", controls.width >= (240 * density).toInt())
                assertTrue("gesture guidance is missing", controls.gestureHint.text.contains("长按拖动"))
                val content = (controls.getChildAt(0) as ScrollView).getChildAt(0) as ViewGroup
                assertTrue("keyboard launcher is missing", hasButton(content, "键盘输入"))
                assertTrue("special-key controls are missing", hasButton(content, "常用按键"))
                assertTrue("release action is unavailable", controls.releaseButton.isShown && controls.releaseButton.isEnabled)
                assertTrue("release target is too small", controls.releaseButton.height >= (48 * density).toInt())
                controls.releaseButton.performClick()
                assertEquals("release action did not fire", 1, releaseCount.get())
                assertEquals("toolbar did not collapse after an action", (48 * density).toInt(), controls.layoutParams.width)
            }
        }
    }

    private fun hasButton(group: ViewGroup, labelPrefix: String): Boolean {
        for (index in 0 until group.childCount) {
            val child = group.getChildAt(index)
            if (child is android.widget.Button && child.text.startsWith(labelPrefix)) return true
            if (child is ViewGroup && hasButton(child, labelPrefix)) return true
        }
        return false
    }
}
