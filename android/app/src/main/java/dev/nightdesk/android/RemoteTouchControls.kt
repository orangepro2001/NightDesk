package dev.nightdesk.android

import android.content.Context
import android.graphics.drawable.GradientDrawable
import android.view.Gravity
import android.view.View
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView

internal class RemoteTouchControls(
    context: Context,
    onRelease: (View) -> Unit
) : LinearLayout(context) {
    val gestureHint = TextView(context).apply {
        text = DefaultHint
        textSize = 11f
        setTextColor(0xffc6d8d3.toInt())
        maxLines = 2
        setPadding(0, dp(3), dp(6), dp(3))
    }

    val releaseButton = Button(context).apply {
        text = "释放"
        contentDescription = "释放所有鼠标按钮和键盘按键"
        isAllCaps = false
        textSize = 12f
        minWidth = dp(64)
        minHeight = dp(48)
        setPadding(dp(12), dp(6), dp(12), dp(6))
        setTextColor(0xffffb4a9.toInt())
        background = GradientDrawable().apply {
            shape = GradientDrawable.RECTANGLE
            cornerRadius = dp(8).toFloat()
            setColor(0xff452625.toInt())
        }
        setOnClickListener { onRelease(it) }
    }

    init {
        orientation = HORIZONTAL
        gravity = Gravity.CENTER_VERTICAL
        setPadding(dp(8), dp(3), dp(6), dp(3))
        setBackgroundColor(0xCC111817.toInt())
        addView(gestureHint, LayoutParams(0, LayoutParams.WRAP_CONTENT, 1f))
        addView(releaseButton)
    }

    fun showDragging() {
        gestureHint.text = "正在拖动 · 抬手结束 · 也可点击“释放”强制松开"
    }

    fun showDefaultHint() {
        gestureHint.text = DefaultHint
    }

    private fun dp(value: Int) = (value * resources.displayMetrics.density).toInt()

    companion object {
        const val DefaultHint = "轻点=左键 · 单指滑动=移动光标 · 长按拖动 · 双指=滚动"
    }
}
