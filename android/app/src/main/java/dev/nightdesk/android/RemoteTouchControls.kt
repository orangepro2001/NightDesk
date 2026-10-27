package dev.nightdesk.android

import android.content.Context
import android.graphics.Color
import android.graphics.drawable.GradientDrawable
import android.view.Gravity
import android.view.View
import android.widget.Button
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView

internal enum class RemoteControlAction { Toggle, Keyboard, SpecialKeys, RightClick, ReleaseAll, Retry, PairAgain, Disconnect }

/** A collapsible edge sidebar; only its small handle overlays the remote image while idle. */
internal class RemoteTouchControls(
    context: Context,
    private val onAction: (RemoteControlAction, View) -> Unit
) : FrameLayout(context) {
    private val menuContent = LinearLayout(context).apply {
        orientation = LinearLayout.VERTICAL
        setPadding(dp(12), dp(76), dp(12), dp(12))
    }
    private val scroll = ScrollView(context).apply {
        isFillViewport = true
        addView(menuContent)
        visibility = GONE
    }
    val status = TextView(context).apply {
        text = "正在准备加密连接…"
        textSize = 13f
        setTextColor(0xffd9eee9.toInt())
        maxLines = 4
        setPadding(dp(4), dp(4), dp(4), dp(10))
        contentDescription = "远控连接状态"
    }
    val gestureHint = TextView(context).apply {
        text = DefaultHint
        textSize = 12f
        setTextColor(0xffc6d8d3.toInt())
        setPadding(dp(4), dp(6), dp(4), dp(10))
    }
    val menuButton = TextView(context).apply {
        text = "⋮"
        textSize = 24f
        gravity = Gravity.CENTER
        setTextColor(Color.WHITE)
        contentDescription = "打开远控控制侧栏；硬件键盘 F1 打开菜单，F2 呼出键盘"
        isClickable = true
        isFocusable = true
        setOnClickListener { onAction(RemoteControlAction.Toggle, it) }
    }
    val releaseButton: Button
    private lateinit var retryButton: Button
    private lateinit var pairButton: Button
    private var expanded = false
    val isExpanded: Boolean get() = expanded

    init {
        menuContent.addView(status)
        menuContent.addView(gestureHint)
        addAction("键盘输入…", RemoteControlAction.Keyboard)
        addAction("常用按键…", RemoteControlAction.SpecialKeys)
        addAction("鼠标右键", RemoteControlAction.RightClick)
        releaseButton = addAction("释放全部按键", RemoteControlAction.ReleaseAll).apply {
            contentDescription = "释放所有鼠标按钮和键盘按键"
        }
        retryButton = addAction("重新连接", RemoteControlAction.Retry).apply { visibility = GONE }
        pairButton = addAction("重新配对…", RemoteControlAction.PairAgain).apply { visibility = GONE }
        addAction("断开并退出", RemoteControlAction.Disconnect)
        menuContent.addView(TextView(context).apply {
            text = "硬件键盘：F1 显示/隐藏控制栏 · F2 呼出键盘"
            textSize = 11f
            setTextColor(0xff9eafa9.toInt())
            setPadding(dp(4), dp(10), dp(4), 0)
        })
        addView(scroll, LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.MATCH_PARENT))
        addView(menuButton, LayoutParams(dp(48), dp(64), Gravity.END or Gravity.CENTER_VERTICAL))
        layoutParams = LayoutParams(dp(48), dp(64))
        background = handleBackground()
        elevation = dp(8).toFloat()
        isClickable = true
    }

    fun toggleMenu() = if (expanded) collapseMenu() else expandMenu()

    fun collapseMenu() {
        expanded = false
        scroll.visibility = GONE
        menuButton.text = "⋮"
        val params = layoutParams
        params.width = dp(48)
        params.height = dp(64)
        layoutParams = params
        setMenuButtonPosition(expanded = false)
        background = handleBackground()
    }

    fun setStatus(message: String) { status.text = message }
    fun setRetryVisible(visible: Boolean) { retryButton.visibility = if (visible) VISIBLE else GONE }
    fun setPairVisible(visible: Boolean) { pairButton.visibility = if (visible) VISIBLE else GONE }

    fun showDragging() {
        gestureHint.text = "正在拖动 · 抬手结束 · 可点释放强制松开"
        menuButton.text = "↻"
        menuButton.contentDescription = "正在拖动；打开控制栏并释放鼠标按钮"
    }

    fun showDefaultHint() {
        gestureHint.text = DefaultHint
        menuButton.text = "⋮"
        menuButton.contentDescription = "打开远控控制侧栏；硬件键盘 F1 打开菜单，F2 呼出键盘"
    }

    private fun expandMenu() {
        expanded = true
        scroll.visibility = VISIBLE
        menuButton.text = "×"
        val params = layoutParams
        params.width = dp(248)
        params.height = LayoutParams.MATCH_PARENT
        layoutParams = params
        setMenuButtonPosition(expanded = true)
        background = GradientDrawable().apply {
            cornerRadius = dp(16).toFloat()
            setColor(0xF217211F.toInt())
            setStroke(dp(1), 0x664A625B)
        }
    }

    private fun setMenuButtonPosition(expanded: Boolean) {
        val params = menuButton.layoutParams as LayoutParams
        params.gravity = Gravity.END or if (expanded) Gravity.TOP else Gravity.CENTER_VERTICAL
        params.topMargin = if (expanded) dp(8) else 0
        menuButton.layoutParams = params
    }

    private fun addAction(label: String, action: RemoteControlAction) = Button(context).apply {
        text = label
        isAllCaps = false
        textSize = 14f
        gravity = Gravity.START or Gravity.CENTER_VERTICAL
        minHeight = dp(48)
        setPadding(dp(12), dp(6), dp(8), dp(6))
        setTextColor(Color.WHITE)
        background = GradientDrawable().apply {
            cornerRadius = dp(8).toFloat()
            setColor(0xff273531.toInt())
        }
        setOnClickListener { onAction(action, it) }
        menuContent.addView(this, LinearLayout.LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.WRAP_CONTENT).apply {
            topMargin = dp(4)
        })
    }

    private fun handleBackground() = GradientDrawable().apply {
        cornerRadius = dp(18).toFloat()
        setColor(0xDD17211F.toInt())
        setStroke(dp(1), 0x664A625B)
    }

    private fun dp(value: Int) = (value * resources.displayMetrics.density).toInt()

    companion object {
        const val DefaultHint = "轻点=左键 · 单指滑动=移动 · 长按拖动 · 双指滚动"
    }
}
