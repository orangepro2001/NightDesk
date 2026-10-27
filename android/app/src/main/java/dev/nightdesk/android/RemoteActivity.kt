package dev.nightdesk.android

import android.app.AlertDialog
import android.content.Context
import android.content.Intent
import android.graphics.Color
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.text.InputType
import android.view.Gravity
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.view.ViewConfiguration
import android.view.WindowManager
import android.widget.Button
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.PopupMenu
import android.widget.TextView
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.core.view.ViewCompat
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import java.io.File
import java.util.concurrent.Executors

class RemoteActivity : ComponentActivity() {
    private val io = Executors.newSingleThreadExecutor()
    private lateinit var store: NxProfileStore
    private lateinit var screen: RemoteScreenView
    private lateinit var status: TextView
    private lateinit var retryButton: Button
    private lateinit var pairButton: Button
    private lateinit var touchControls: RemoteTouchControls
    private lateinit var host: NxHost
    private val connectionState = NxConnectionModel()
    @Volatile private var session: mobile.Session? = null
    @Volatile private var generation = 0L
    @Volatile private var destroyed = false
    @Volatile private var lastFrameWidth = 0
    @Volatile private var lastFrameHeight = 0
    private var persistedFrameWidth = -1
    private var persistedFrameHeight = -1
    private val timeoutHandler = Handler(Looper.getMainLooper())
    private val gestureHandler = Handler(Looper.getMainLooper())
    private lateinit var touchGesture: NxTouchGesture
    private var longPressTimeoutMillis = NxTouchGesture.DefaultLongPressMillis
    private var currentCode = ""
    private var pairingDialogShowing = false
    private var touchSequence = 0L

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val touchConfig = ViewConfiguration.get(this)
        longPressTimeoutMillis = ViewConfiguration.getLongPressTimeout().toLong()
        touchGesture = NxTouchGesture(
            longPressMillis = longPressTimeoutMillis,
            moveSlopPx = touchConfig.scaledTouchSlop.toFloat(),
            wheelStepPx = dp(28).toFloat()
        )
        store = NxProfileStore(this)
        host = resolveHost(intent) ?: return
        currentCode = intent.getStringExtra(EXTRA_CODE).orEmpty()
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        buildUi()
        connect(currentCode)
    }

    private fun resolveHost(source: Intent): NxHost? {
        val fingerprint = source.getStringExtra(EXTRA_FINGERPRINT)?.lowercase().orEmpty()
        val saved = runCatching { store.load().items.firstOrNull { it.id == fingerprint } }.getOrNull()
        if (saved != null) return saved
        return try {
            NxHost(
                addr = NxPairing.validateAddress(source.getStringExtra(EXTRA_ADDR).orEmpty()),
                fingerprint = fingerprint.also { require(Regex("^[0-9a-f]{64}$").matches(it)) },
                name = NxPairing.cleanName(source.getStringExtra(EXTRA_NAME).orEmpty()),
                width = source.getIntExtra(EXTRA_WIDTH, 0).also { require(it in 64..16384) },
                height = source.getIntExtra(EXTRA_HEIGHT, 0).also { require(it in 64..16384) },
                lastConnected = System.currentTimeMillis()
            )
        } catch (e: Exception) {
            toast(e.message ?: "屏幕主机信息无效")
            finish()
            null
        }
    }

    private fun buildUi() {
        val root = FrameLayout(this).apply { setBackgroundColor(Color.BLACK) }
        setContentView(root)
        WindowCompat.getInsetsController(window, root).isAppearanceLightStatusBars = false
        WindowCompat.getInsetsController(window, root).isAppearanceLightNavigationBars = false
        ViewCompat.setOnApplyWindowInsetsListener(root) { view, insets ->
            val bars = insets.getInsets(WindowInsetsCompat.Type.systemBars())
            view.setPadding(bars.left, bars.top, bars.right, bars.bottom)
            insets
        }

        screen = RemoteScreenView(this)
        root.addView(screen, FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.MATCH_PARENT
        ))
        screen.setOnTouchListener(::onRemoteTouch)

        val top = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(dp(6), dp(4), dp(6), dp(4))
            setBackgroundColor(0xCC111817.toInt())
        }
        top.addView(overlayButton("返回") { finish() })
        status = TextView(this).apply {
            text = "正在准备加密连接…"
            textSize = 13f
            setTextColor(0xffd9eee9.toInt())
            maxLines = 2
            setPadding(dp(8), 0, dp(8), 0)
        }
        top.addView(status, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
        retryButton = overlayButton("重试") { retryConnection() }.apply { visibility = View.GONE }
        pairButton = overlayButton("配对码") { showPairingCodeDialog() }.apply { visibility = View.GONE }
        top.addView(retryButton)
        top.addView(pairButton)
        top.addView(overlayButton("按键") { showKeyMenu(it) })
        top.addView(overlayButton("键盘") { showTextDialog() })
        top.addView(overlayButton("右键") { rightClick() })
        root.addView(top, FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.WRAP_CONTENT,
            Gravity.TOP
        ))

        touchControls = RemoteTouchControls(this) { releaseAll() }
        root.addView(touchControls, FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.WRAP_CONTENT,
            Gravity.BOTTOM
        ))
    }

    private fun connect(pairingCode: String) {
        val request = connectionState.begin(pairingCode.isNotBlank())
        generation = request
        persistedFrameWidth = -1
        persistedFrameHeight = -1
        closeTransport(clearScreen = true)
        timeoutHandler.removeCallbacksAndMessages(null)
        renderConnection()
        timeoutHandler.postDelayed({
            if (!destroyed && request == generation && connectionState.timeout(request)) {
                // Invalidate callbacks from a handshake that exceeded our UI deadline. NewSession
                // still has its own 15 s Go timeout; if it returns later, its handle is closed.
                generation++
                closeTransport(clearScreen = false)
                renderConnection()
            }
        }, NxConnectionModel.DefaultTimeoutMillis + 500)
        io.execute {
            try {
                val identityDir = File(filesDir, "nx-identity").apply { mkdirs() }
                val opened = mobile.Mobile.newSession(
                    identityDir.absolutePath,
                    host.addr,
                    host.fingerprint,
                    pairingCode,
                    Callback(request)
                )
                var keep = false
                synchronized(this) {
                    if (!destroyed && request == generation) {
                        session = opened
                        keep = true
                    }
                }
                if (!keep) runCatching { opened.close() }
            } catch (e: Exception) {
                ui(request) {
                    connectionState.error(request, "disconnected", e.message.orEmpty())
                    renderConnection()
                }
            }
        }
    }

    private inner class Callback(private val request: Long) : mobile.Callback {
        override fun onState(state: String) {
            ui(request) {
                when (state) {
                    mobile.Mobile.StateConnecting -> {
                        connectionState.state(request, state)
                        renderConnection()
                    }
                    mobile.Mobile.StateStreaming -> {
                        connectionState.streaming(request)
                        currentCode = ""
                        timeoutHandler.removeCallbacksAndMessages(null)
                        renderConnection()
                        persistConnected()
                    }
                    mobile.Mobile.StateClosed -> {
                        connectionState.closed(request)
                        closeTransport(clearScreen = false)
                        renderConnection()
                    }
                }
            }
        }

        override fun onFrameBegin(width: Long, height: Long) {
            if (!active(request)) return
            val w = width.toInt()
            val h = height.toInt()
            if (w !in 1..16384 || h !in 1..16384) return
            lastFrameWidth = w
            lastFrameHeight = h
            screen.beginFrame(w, h)
        }

        override fun onTile(x: Long, y: Long, width: Long, height: Long, pixels: ByteArray) {
            if (!active(request)) return
            screen.applyTile(x.toInt(), y.toInt(), width.toInt(), height.toInt(), pixels)
        }

        override fun onReceipt(opID: Long, result: Long) {
            if (!active(request)) return
            if (result != mobile.Mobile.ReceiptExecuted) {
                ui(request) { status.text = "主机拒绝了输入操作" }
            }
        }

        override fun onError(code: String, message: String) {
            ui(request) {
                connectionState.error(request, code, message)
                val snapshot = connectionState.snapshot()
                if (snapshot.canPairAgain) {
                    currentCode = ""
                    renderConnection()
                    showPairingCodeDialog()
                } else {
                    renderConnection()
                }
            }
        }
    }

    private fun active(request: Long) = !destroyed && request == generation

    private fun ui(request: Long, block: () -> Unit) {
        runOnUiThread {
            if (!destroyed && request == generation) block()
        }
    }

    private fun persistConnected() {
        val width = lastFrameWidth.takeIf { it > 0 } ?: host.width
        val height = lastFrameHeight.takeIf { it > 0 } ?: host.height
        if (width <= 0 || height <= 0) return
        if (width == persistedFrameWidth && height == persistedFrameHeight) return
        persistedFrameWidth = width
        persistedFrameHeight = height
        io.execute { runCatching { store.markConnected(host.id, width, height) } }
    }

    private fun friendlyError(code: String): String = when (code) {
        "bad-code" -> "配对码错误或已过期，请输入 Windows 端当前显示的 6 位代码"
        "untrusted" -> "主机尚未信任此设备，请重新配对"
        "host-identity" -> "主机证书指纹不匹配，已拒绝连接"
        "locked" -> "错误配对次数过多，请稍后重试"
        "replaced" -> "另一台设备接管了此会话"
        "identity" -> "无法读取本机客户端身份"
        "timeout" -> "连接超时，请检查 Tailscale 和 Windows 主机后重试"
        "disconnected", "link-error" -> "连接已断开，可点击“重试”"
        else -> if (code.length <= 160) code else "连接失败"
    }

    private fun renderConnection() {
        if (!::status.isInitialized) return
        val snapshot = connectionState.snapshot()
        status.text = when (snapshot.phase) {
            NxConnectionPhase.Connecting ->
                if (snapshot.pairingCodeProvided) "正在使用短期配对码连接 ${host.addr}…"
                else "正在连接 ${host.addr}…"
            NxConnectionPhase.Streaming -> "已连接 · 轻点单击，长按拖动"
            NxConnectionPhase.Disconnected -> "连接已断开，可点击“重试”"
            NxConnectionPhase.Background -> "已暂停：连接和按键已释放，返回后点击“重试”"
            NxConnectionPhase.Failed -> snapshot.failure?.let { friendlyError(it.code) } ?: "连接失败"
        }
        retryButton.visibility = if (snapshot.canRetry) View.VISIBLE else View.GONE
        pairButton.visibility = if (snapshot.canPairAgain) View.VISIBLE else View.GONE
    }

    private fun retryConnection() {
        if (!connectionState.snapshot().canRetry) return
        connect(currentCode)
    }

    private fun showPairingCodeDialog() {
        if (pairingDialogShowing || isFinishing || destroyed) return
        pairingDialogShowing = true
        val input = EditText(this).apply {
            hint = "6 位配对码"
            inputType = InputType.TYPE_CLASS_NUMBER
            filters = arrayOf(android.text.InputFilter.LengthFilter(6))
            setSingleLine(true)
            setTextColor(Color.BLACK)
            setPadding(dp(16), dp(12), dp(16), dp(12))
            if (currentCode.matches(Regex("^[0-9]{6}$"))) setText(currentCode)
        }
        val wrapper = FrameLayout(this).apply {
            setPadding(dp(20), dp(4), dp(20), 0)
            addView(input)
        }
        AlertDialog.Builder(this)
            .setTitle("输入 Windows 配对码")
            .setMessage("配对码只在内存中使用，不写入长期存储。")
            .setView(wrapper)
            .setNegativeButton("取消") { _, _ -> pairingDialogShowing = false }
            .setPositiveButton("连接") { _, _ ->
                pairingDialogShowing = false
                val code = input.text.toString().trim()
                if (!code.matches(Regex("^[0-9]{6}$"))) toast("配对码必须是 6 位数字")
                else {
                    currentCode = code
                    connect(code)
                }
            }
            .setOnDismissListener { pairingDialogShowing = false }
            .show()
    }

    private fun showTextDialog() {
        val input = EditText(this).apply {
            hint = "输入要发送到 Windows 的文字"
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_MULTI_LINE
            minLines = 3
            maxLines = 8
            setTextColor(Color.BLACK)
            setPadding(dp(16), dp(12), dp(16), dp(12))
        }
        val wrapper = FrameLayout(this).apply {
            setPadding(dp(20), dp(4), dp(20), 0)
            addView(input)
        }
        AlertDialog.Builder(this)
            .setTitle("发送文字")
            .setMessage("使用 Unicode 文本注入，不受 Windows 键盘布局影响；每次最多 1024 UTF-8 字节，长文本会自动分段。")
            .setView(wrapper)
            .setNegativeButton("取消", null)
            .setPositiveButton("发送") { _, _ ->
                val chunks = try { NxText.utf8Chunks(input.text.toString()) } catch (e: Exception) {
                    return@setPositiveButton toast(e.message ?: "文字无效")
                }
                if (chunks.isEmpty()) return@setPositiveButton
                io.execute {
                    try {
                        val active = session ?: throw IllegalStateException("尚未连接")
                        chunks.forEach { active.text(it) }
                        runOnUiThread { if (!destroyed) status.text = "已发送 ${chunks.size} 段 Unicode 文字" }
                    } catch (e: Exception) {
                        runOnUiThread { if (!destroyed) toast(e.message ?: "发送文字失败") }
                    }
                }
            }
            .show()
    }

    private fun showKeyMenu(anchor: View) {
        val menu = PopupMenu(this, anchor)
        fun add(label: String, action: () -> Unit) {
            menu.menu.add(label).setOnMenuItemClickListener { action(); true }
        }
        add("Enter") { tapKey(0x0D) }
        add("Tab") { tapKey(0x09) }
        add("Esc") { tapKey(0x1B) }
        add("Backspace") { tapKey(0x08) }
        add("Delete") { tapKey(0x2E) }
        add("方向键 ↑") { tapKey(0x26) }
        add("方向键 ↓") { tapKey(0x28) }
        add("方向键 ←") { tapKey(0x25) }
        add("方向键 →") { tapKey(0x27) }
        add("Win") { tapKey(0x5B) }
        add("Alt+Tab") { combo(intArrayOf(0x12), 0x09) }
        add("Ctrl+C") { combo(intArrayOf(0x11), 0x43) }
        add("Ctrl+V") { combo(intArrayOf(0x11), 0x56) }
        add("Ctrl+S") { combo(intArrayOf(0x11), 0x53) }
        add("Ctrl+Z") { combo(intArrayOf(0x11), 0x5A) }
        add("Ctrl+Shift+Esc") { combo(intArrayOf(0x11, 0x10), 0x1B) }
        menu.show()
    }

    private fun tapKey(vk: Int) = combo(intArrayOf(), vk)

    private fun combo(modifiers: IntArray, key: Int) {
        io.execute {
            try {
                val active = session ?: throw IllegalStateException("尚未连接")
                try {
                    modifiers.forEach { active.keyDown(it.toLong()) }
                    active.keyDown(key.toLong())
                    active.keyUp(key.toLong())
                    modifiers.reversed().forEach { active.keyUp(it.toLong()) }
                } finally {
                    runCatching { active.releaseAll() }
                }
            } catch (e: Exception) {
                runOnUiThread { if (!destroyed) toast(e.message ?: "发送按键失败") }
            }
        }
    }

    private fun rightClick() {
        val point = screen.lastPoint ?: screen.centerPoint() ?: return
        io.execute {
            try {
                val active = session ?: throw IllegalStateException("尚未连接")
                try {
                    active.move(point.x.toLong(), point.y.toLong())
                    active.mouseDown(1L)
                    active.mouseUp(1L)
                } finally {
                    runCatching { active.releaseAll() }
                }
            } catch (e: Exception) {
                runOnUiThread { if (!destroyed) toast(e.message ?: "右键失败") }
            }
        }
    }

    private fun onRemoteTouch(view: View, event: MotionEvent): Boolean {
        val now = event.eventTime
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                cancelLongPress()
                touchSequence++
                val point = screen.mapToHost(event.x, event.y) ?: run {
                    sendGestureActions(touchGesture.onCancel())
                    return true
                }
                sendGestureActions(touchGesture.onDown(event.x, event.y, point, now))
                val sequence = touchSequence
                gestureHandler.postDelayed({
                    if (!destroyed && sequence == touchSequence) {
                        sendGestureActions(touchGesture.onLongPress(SystemClock.uptimeMillis()))
                    }
                }, longPressTimeoutMillis)
            }
            MotionEvent.ACTION_POINTER_DOWN -> {
                cancelLongPress()
                touchSequence++
                sendGestureActions(touchGesture.onPointerDown(centroidY(event)))
            }
            MotionEvent.ACTION_MOVE -> {
                if (event.pointerCount >= 2 && touchGesture.isScrolling) {
                    sendGestureActions(touchGesture.onTwoFingerMove(centroidY(event)))
                } else if (event.pointerCount == 1) {
                    val point = screen.mapToHost(event.x, event.y)
                    sendGestureActions(touchGesture.onMove(event.x, event.y, point, now))
                    if (!touchGesture.isTapPending) cancelLongPress()
                }
            }
            MotionEvent.ACTION_POINTER_UP -> {
                cancelLongPress()
                touchSequence++
                sendGestureActions(touchGesture.onPointerUp())
            }
            MotionEvent.ACTION_UP -> {
                cancelLongPress()
                touchSequence++
                sendGestureActions(touchGesture.onUp(screen.mapToHost(event.x, event.y)))
            }
            MotionEvent.ACTION_CANCEL -> {
                cancelLongPress()
                touchSequence++
                sendGestureActions(touchGesture.onCancel())
            }
            MotionEvent.ACTION_OUTSIDE -> {
                cancelLongPress()
                touchSequence++
                sendGestureActions(touchGesture.onCancel())
            }
        }
        return true
    }

    private fun centroidY(event: MotionEvent): Float {
        val count = minOf(2, event.pointerCount)
        if (count == 0) return 0f
        var total = 0f
        for (index in 0 until count) total += event.getY(index)
        return total / count
    }

    private fun sendGestureActions(actions: List<NxTouchAction>) {
        if (actions.isEmpty()) return
        if (actions.any { it is NxTouchAction.BeginDrag }) {
            runOnUiThread { if (!destroyed && ::touchControls.isInitialized) touchControls.showDragging() }
        } else if (actions.any { it is NxTouchAction.EndDrag || it is NxTouchAction.ReleaseAll }) {
            runOnUiThread { if (!destroyed && ::touchControls.isInitialized) touchControls.showDefaultHint() }
        }
        io.execute {
            val active = session
            try {
                active ?: return@execute
                actions.forEach { action ->
                    when (action) {
                        is NxTouchAction.Move -> active.move(action.x.toLong(), action.y.toLong())
                        is NxTouchAction.Click -> {
                            active.move(action.x.toLong(), action.y.toLong())
                            active.mouseDown(0L)
                            active.mouseUp(0L)
                        }
                        is NxTouchAction.BeginDrag -> {
                            active.move(action.x.toLong(), action.y.toLong())
                            active.mouseDown(0L)
                        }
                        is NxTouchAction.EndDrag -> {
                            active.move(action.x.toLong(), action.y.toLong())
                            active.mouseUp(0L)
                        }
                        is NxTouchAction.Scroll -> active.wheel(action.delta.toLong())
                        NxTouchAction.ReleaseAll -> active.releaseAll()
                    }
                }
            } catch (_: Exception) {
                runCatching { active?.releaseAll() }
            }
        }
    }

    private fun releaseAll() {
        cancelLongPress()
        touchSequence++
        sendGestureActions(touchGesture.onReleaseAll())
    }

    private fun cancelLongPress() {
        gestureHandler.removeCallbacksAndMessages(null)
    }

    private fun closeTransport(clearScreen: Boolean) {
        val previous = session
        session = null
        cancelLongPress()
        touchSequence++
        touchGesture.onReleaseAll()
        if (clearScreen && ::screen.isInitialized) screen.clear()
        io.execute {
            previous?.let {
                runCatching { it.releaseAll() }
                runCatching { it.close() }
            }
        }
    }

    override fun onStart() {
        super.onStart()
        if (::status.isInitialized) renderConnection()
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (!hasFocus) {
            releaseAll()
        }
    }

    override fun onStop() {
        timeoutHandler.removeCallbacksAndMessages(null)
        val attempt = connectionState.snapshot().attempt
        if (attempt > 0 && connectionState.background(attempt)) {
            // Invalidate every late Go callback and release held mouse buttons/keys before the
            // activity leaves the foreground. Resuming requires an explicit user retry.
            generation++
            closeTransport(clearScreen = true)
            renderConnection()
        }
        super.onStop()
    }

    override fun onDestroy() {
        timeoutHandler.removeCallbacksAndMessages(null)
        gestureHandler.removeCallbacksAndMessages(null)
        val attempt = connectionState.snapshot().attempt
        if (attempt > 0) connectionState.background(attempt)
        destroyed = true
        generation++
        closeTransport(clearScreen = true)
        io.shutdown()
        super.onDestroy()
    }

    private fun overlayButton(label: String, action: (View) -> Unit) = Button(this).apply {
        text = label
        isAllCaps = false
        textSize = 12f
        minWidth = 0
        minimumWidth = 0
        setPadding(dp(8), dp(2), dp(8), dp(2))
        setTextColor(Color.WHITE)
        background = null
        setOnClickListener(action)
    }

    private fun dp(value: Int) = (value * resources.displayMetrics.density).toInt()
    private fun toast(message: String) = Toast.makeText(this, message, Toast.LENGTH_LONG).show()

    companion object {
        private const val EXTRA_ADDR = "nx.addr"
        private const val EXTRA_FINGERPRINT = "nx.fingerprint"
        private const val EXTRA_NAME = "nx.name"
        private const val EXTRA_WIDTH = "nx.width"
        private const val EXTRA_HEIGHT = "nx.height"
        private const val EXTRA_CODE = "nx.code"

        fun intent(context: Context, host: NxHost, pairingCode: String?): Intent =
            Intent(context, RemoteActivity::class.java).apply {
                putExtra(EXTRA_ADDR, host.addr)
                putExtra(EXTRA_FINGERPRINT, host.fingerprint)
                putExtra(EXTRA_NAME, host.name)
                putExtra(EXTRA_WIDTH, host.width)
                putExtra(EXTRA_HEIGHT, host.height)
                if (!pairingCode.isNullOrBlank()) putExtra(EXTRA_CODE, pairingCode)
            }
    }
}
