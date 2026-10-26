package dev.nightdesk.android

import android.app.AlertDialog
import android.content.Context
import android.content.Intent
import android.graphics.Color
import android.os.Bundle
import android.text.InputType
import android.view.Gravity
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.view.WindowManager
import android.widget.Button
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.PopupMenu
import android.widget.TextView
import android.widget.Toast
import androidx.activity.ComponentActivity
import java.io.File
import java.util.concurrent.Executors
import kotlin.math.abs

class RemoteActivity : ComponentActivity() {
    private val io = Executors.newSingleThreadExecutor()
    private lateinit var store: NxProfileStore
    private lateinit var screen: RemoteScreenView
    private lateinit var status: TextView
    private lateinit var host: NxHost
    private var session: mobile.Session? = null
    private var generation = 0
    @Volatile private var destroyed = false
    @Volatile private var lastFrameWidth = 0
    @Volatile private var lastFrameHeight = 0
    private var currentCode = ""
    private var pairingDialogShowing = false

    private var primaryPointer = MotionEvent.INVALID_POINTER_ID
    private var leftDown = false
    private var scrolling = false
    private var lastScrollY = 0f
    private var scrollAccumulator = 0f

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
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
        top.addView(overlayButton("按键") { showKeyMenu(it) })
        top.addView(overlayButton("键盘") { showTextDialog() })
        top.addView(overlayButton("右键") { rightClick() })
        root.addView(top, FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.WRAP_CONTENT,
            Gravity.TOP
        ))

        val help = TextView(this).apply {
            text = "单指拖动鼠标 · 双指上下滚动 · 界面失焦或切后台会自动断开并释放按键"
            textSize = 11f
            setTextColor(0xffc6d8d3.toInt())
            setPadding(dp(12), dp(6), dp(12), dp(6))
            setBackgroundColor(0xAA111817.toInt())
        }
        root.addView(help, FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.WRAP_CONTENT,
            Gravity.BOTTOM
        ))
    }

    private fun connect(pairingCode: String) {
        stopSession(clearStatus = false)
        val request = ++generation
        status.text = "正在连接 ${host.addr}…"
        screen.clear()
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
                ui(request) { status.text = friendlyError(e.message ?: "连接失败") }
            }
        }
    }

    private inner class Callback(private val request: Int) : mobile.Callback {
        override fun onState(state: String) {
            ui(request) {
                when (state) {
                    mobile.Mobile.StateConnecting -> status.text = "正在建立 QUIC + TLS 1.3…"
                    mobile.Mobile.StateStreaming -> {
                        currentCode = ""
                        status.text = "已连接 · 拖动鼠标，双指滚动"
                        persistConnected()
                    }
                    mobile.Mobile.StateClosed -> status.text = "连接已断开"
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
                status.text = friendlyError(code)
                if (code == "bad-code" || code == "untrusted") showPairingCodeDialog()
            }
        }
    }

    private fun active(request: Int) = !destroyed && request == generation

    private fun ui(request: Int, block: () -> Unit) {
        runOnUiThread {
            if (!destroyed && request == generation) block()
        }
    }

    private fun persistConnected() {
        val width = lastFrameWidth
        val height = lastFrameHeight
        if (width <= 0 || height <= 0) return
        io.execute { runCatching { store.markConnected(host.id, width, height) } }
    }

    private fun friendlyError(code: String): String = when (code) {
        "bad-code" -> "配对码错误或已过期，请输入 Windows 端当前显示的 6 位代码"
        "untrusted" -> "主机尚未信任此设备，请重新配对"
        "host-identity" -> "主机证书指纹不匹配，已拒绝连接"
        "locked" -> "错误配对次数过多，请稍后重试"
        "replaced" -> "另一台设备接管了此会话"
        "identity" -> "无法读取本机客户端身份"
        else -> if (code.length <= 160) code else "连接失败"
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
                modifiers.forEach { active.keyDown(it.toLong()) }
                active.keyDown(key.toLong())
                active.keyUp(key.toLong())
                modifiers.reversed().forEach { active.keyUp(it.toLong()) }
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
                active.move(point.x.toLong(), point.y.toLong())
                active.mouseDown(1L)
                active.mouseUp(1L)
            } catch (e: Exception) {
                runOnUiThread { if (!destroyed) toast(e.message ?: "右键失败") }
            }
        }
    }

    private fun onRemoteTouch(view: View, event: MotionEvent): Boolean {
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                val point = screen.mapToHost(event.x, event.y) ?: return true
                primaryPointer = event.getPointerId(0)
                leftDown = true
                sendInput { it.move(point.x.toLong(), point.y.toLong()); it.mouseDown(0L) }
            }
            MotionEvent.ACTION_POINTER_DOWN -> {
                if (event.pointerCount == 2 && leftDown) {
                    leftDown = false
                    sendInput { it.mouseUp(0L) }
                    scrolling = true
                    scrollAccumulator = 0f
                    lastScrollY = centroidY(event)
                }
            }
            MotionEvent.ACTION_MOVE -> {
                if (scrolling && event.pointerCount >= 2) {
                    val y = centroidY(event)
                    val threshold = dp(28).toFloat()
                    scrollAccumulator += lastScrollY - y
                    val steps = (scrollAccumulator / threshold).toInt()
                    if (steps != 0) {
                        scrollAccumulator -= steps * threshold
                        val delta = steps * 120
                        sendInput { it.wheel(delta.toLong()) }
                    }
                    lastScrollY = y
                } else if (leftDown && !scrolling) {
                    val pointer = event.findPointerIndex(primaryPointer)
                    if (pointer >= 0) {
                        val point = screen.mapToHost(event.getX(pointer), event.getY(pointer))
                        if (point != null) sendInput { it.move(point.x.toLong(), point.y.toLong()) }
                    }
                }
            }
            MotionEvent.ACTION_UP -> {
                if (scrolling) {
                    scrolling = false
                    scrollAccumulator = 0f
                } else if (leftDown) {
                    screen.mapToHost(event.x, event.y)?.let { point ->
                        sendInput { it.move(point.x.toLong(), point.y.toLong()); it.mouseUp(0L) }
                    }
                    leftDown = false
                }
                primaryPointer = MotionEvent.INVALID_POINTER_ID
            }
            MotionEvent.ACTION_CANCEL -> {
                leftDown = false
                scrolling = false
                scrollAccumulator = 0f
                primaryPointer = MotionEvent.INVALID_POINTER_ID
                releaseAll()
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

    private fun sendInput(block: (mobile.Session) -> Unit) {
        io.execute {
            try {
                val active = session ?: return@execute
                block(active)
            } catch (_: Exception) {
            }
        }
    }

    private fun releaseAll() {
        val active = session ?: return
        io.execute { runCatching { active.releaseAll() } }
    }

    private fun stopSession(clearStatus: Boolean = true) {
        generation++
        val previous = session
        session = null
        leftDown = false
        scrolling = false
        screen.clear()
        if (clearStatus) status.text = "已断开"
        io.execute {
            previous?.let {
                runCatching { it.releaseAll() }
                runCatching { it.close() }
            }
        }
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (!hasFocus) {
            leftDown = false
            scrolling = false
            releaseAll()
        }
    }

    override fun onStop() {
        stopSession(clearStatus = false)
        super.onStop()
    }

    override fun onDestroy() {
        destroyed = true
        generation++
        val previous = session
        session = null
        io.execute {
            previous?.let {
                runCatching { it.releaseAll() }
                runCatching { it.close() }
            }
        }
        io.shutdown()
        screen.clear()
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