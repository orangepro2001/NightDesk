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
import android.view.HapticFeedbackConstants
import android.view.KeyEvent
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.view.ViewConfiguration
import android.view.WindowManager
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.PopupMenu
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsControllerCompat
import androidx.core.view.WindowInsetsCompat
import java.io.File
import java.util.concurrent.Executors
import kotlin.math.hypot

class RemoteActivity : ComponentActivity() {
    private val io = Executors.newSingleThreadExecutor()
    private lateinit var store: NxProfileStore
    private lateinit var screen: RemoteScreenView
    private lateinit var touchControls: RemoteTouchControls
    private lateinit var host: NxHost
    private val connectionState = NxConnectionModel()
    @Volatile private var session: mobile.Session? = null
    @Volatile private var generation = 0L
    @Volatile private var destroyed = false
    @Volatile private var lastFrameWidth = 0
    @Volatile private var lastFrameHeight = 0
    @Volatile private var frameCount = 0
    private var persistedFrameWidth = -1
    private var persistedFrameHeight = -1
    private val timeoutHandler = Handler(Looper.getMainLooper())
    private val gestureHandler = Handler(Looper.getMainLooper())
    private val flingHandler = Handler(Looper.getMainLooper())
    private val statsHandler = Handler(Looper.getMainLooper())
    private lateinit var touchGesture: NxTouchGesture
    private lateinit var pointer: NxPointer
    private lateinit var preferences: NxRemotePreferences
    private var mouseMode = NxMouseMode.Direct
    private var longPressTimeoutMillis = NxTouchGesture.DefaultLongPressMillis
    private var wheelStepPx = 28f
    private var currentCode = ""
    private var pairingDialogShowing = false
    private var textDialogShowing = false
    private var touchSequence = 0L
    private val localKeys = mutableSetOf<Int>()

    private val moveLock = Any()
    private var pendingMove: NxPoint? = null
    private var moveScheduled = false

    private var lastTouchX = 0f
    private var lastTouchY = 0f
    private var lastTouchAt = 0L
    private var flingActive = false
    private var flingVelocityX = 0f
    private var flingVelocityY = 0f
    private var flingAccumulatorX = 0f
    private var flingAccumulatorY = 0f
    private var lastFlingAt = 0L

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val touchConfig = ViewConfiguration.get(this)
        longPressTimeoutMillis = ViewConfiguration.getLongPressTimeout().toLong()
        wheelStepPx = dp(28).toFloat()
        touchGesture = NxTouchGesture(
            longPressMillis = longPressTimeoutMillis,
            moveSlopPx = touchConfig.scaledTouchSlop.toFloat(),
            wheelStepPx = wheelStepPx,
            scrollStartSlopPx = touchConfig.scaledTouchSlop * 1.5f
        )
        preferences = NxRemotePreferences(this)
        mouseMode = preferences.mouseMode
        pointer = NxPointer().apply { modeChanged(mouseMode) }
        store = NxProfileStore(this)
        host = resolveHost(intent) ?: return
        currentCode = intent.getStringExtra(EXTRA_CODE).orEmpty()
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        buildUi()
        connect(currentCode)
        statsHandler.postDelayed(statsRunnable, 1000)
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
        WindowCompat.setDecorFitsSystemWindows(window, false)
        hideSystemBars(root)

        screen = RemoteScreenView(this)
        root.addView(screen, FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.MATCH_PARENT
        ))
        screen.onViewportChanged = { width, height -> pointer.onViewSize(width, height) }
        screen.setOnTouchListener(::onRemoteTouch)

        touchControls = RemoteTouchControls(this, ::onSessionControl)
        root.addView(touchControls, FrameLayout.LayoutParams(
            dp(48),
            dp(64),
            Gravity.END or Gravity.CENTER_VERTICAL
        ))
        touchControls.setMouseMode(mouseMode)
    }

    private fun onSessionControl(action: RemoteControlAction, anchor: View) {
        if (action != RemoteControlAction.Toggle && action != RemoteControlAction.ToggleMouseMode) {
            touchControls.collapseMenu()
        }
        when (action) {
            RemoteControlAction.Toggle -> {
                releaseAll()
                touchControls.toggleMenu()
            }
            RemoteControlAction.ToggleMouseMode -> toggleMouseMode()
            RemoteControlAction.Keyboard -> showTextDialog()
            RemoteControlAction.SpecialKeys -> showKeyMenu(touchControls.menuButton)
            RemoteControlAction.RightClick -> rightClick()
            RemoteControlAction.ReleaseAll -> releaseAll()
            RemoteControlAction.Retry -> retryConnection()
            RemoteControlAction.PairAgain -> showPairingCodeDialog()
            RemoteControlAction.Disconnect -> finish()
        }
    }

    private fun toggleMouseMode() {
        releaseAll()
        mouseMode = if (mouseMode == NxMouseMode.Direct) NxMouseMode.Trackpad else NxMouseMode.Direct
        preferences.mouseMode = mouseMode
        pointer.modeChanged(mouseMode)
        touchControls.setMouseMode(mouseMode)
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
            frameCount++
            pointer.onFrame(w, h)
            screen.beginFrame(w, h)
        }

        override fun onTile(x: Long, y: Long, width: Long, height: Long, pixels: ByteArray) {
            if (!active(request)) return
            screen.applyTile(x.toInt(), y.toInt(), width.toInt(), height.toInt(), pixels)
        }

        override fun onReceipt(opID: Long, result: Long) {
            if (!active(request)) return
            if (result != mobile.Mobile.ReceiptExecuted) {
                ui(request) { touchControls.setStatus("主机拒绝了输入操作") }
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
        if (!::touchControls.isInitialized) return
        val snapshot = connectionState.snapshot()
        touchControls.setStatus(when (snapshot.phase) {
            NxConnectionPhase.Connecting ->
                if (snapshot.pairingCodeProvided) "正在使用短期配对码连接 ${host.addr}…"
                else "正在连接 ${host.addr}…"
            NxConnectionPhase.Streaming -> "已连接 · 轻点单击，长按拖动"
            NxConnectionPhase.Disconnected -> "连接已断开，可点击“重试”"
            NxConnectionPhase.Background -> "已暂停：连接和按键已释放，返回后点击“重试”"
            NxConnectionPhase.Failed -> snapshot.failure?.let { friendlyError(it.code) } ?: "连接失败"
        })
        touchControls.setRetryVisible(snapshot.canRetry)
        touchControls.setPairVisible(snapshot.canPairAgain)
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
        if (textDialogShowing || isFinishing || destroyed) return
        textDialogShowing = true
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
        val dialog = AlertDialog.Builder(this)
            .setTitle("发送文字")
            .setMessage("将弹出手机键盘。输入完成后点击“发送”；每次最多 1024 UTF-8 字节，长文本会自动分段。")
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
                        runOnUiThread { if (!destroyed) touchControls.setStatus("已发送 ${chunks.size} 段 Unicode 文字") }
                    } catch (e: Exception) {
                        runOnUiThread { if (!destroyed) toast(e.message ?: "发送文字失败") }
                    }
                }
            }
            .create()
        dialog.setOnDismissListener { textDialogShowing = false }
        dialog.setOnShowListener {
            input.requestFocus()
            input.postDelayed({
                if (!destroyed && dialog.isShowing) {
                    val keyboard = getSystemService(Context.INPUT_METHOD_SERVICE) as android.view.inputmethod.InputMethodManager
                    keyboard.showSoftInput(input, android.view.inputmethod.InputMethodManager.SHOW_IMPLICIT)
                }
            }, 120)
        }
        dialog.show()
    }

    private fun showKeyMenu(anchor: View) {
        val menu = PopupMenu(this, anchor)
        fun add(label: String, action: () -> Unit) {
            menu.menu.add(label).setOnMenuItemClickListener { action(); true }
        }
        add("Enter") { tapKey(0x0D) }
        add("F1") { tapKey(0x70) }
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
        val point = pointer.cursorPoint() ?: screen.centerPoint() ?: return
        cancelFling()
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
        if (touchControls.isExpanded) {
            if (event.actionMasked == MotionEvent.ACTION_DOWN) {
                releaseAll()
                touchControls.collapseMenu()
            }
            return true
        }
        val now = event.eventTime
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                cancelLongPress()
                cancelFling()
                touchSequence++
                lastTouchX = event.x
                lastTouchY = event.y
                lastTouchAt = now
                val point = hostPointAt(event.x, event.y)
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
                cancelFling()
                touchSequence++
                sendGestureActions(touchGesture.onPointerDown(centroidX(event), centroidY(event), now))
            }
            MotionEvent.ACTION_MOVE -> {
                if (event.pointerCount >= 2 && touchGesture.isTwoFingerGesture) {
                    sendGestureActions(touchGesture.onTwoFingerMove(centroidX(event), centroidY(event), now))
                } else if (event.pointerCount == 1) {
                    val point = singleFingerPoint(event, now)
                    sendGestureActions(touchGesture.onMove(event.x, event.y, point, now))
                    if (!touchGesture.isTapPending) cancelLongPress()
                }
            }
            MotionEvent.ACTION_POINTER_UP -> {
                cancelLongPress()
                touchSequence++
                sendGestureActions(touchGesture.onPointerUp(now, event.pointerCount - 1))
            }
            MotionEvent.ACTION_UP -> {
                cancelLongPress()
                touchSequence++
                sendGestureActions(touchGesture.onUp(hostPointAt(event.x, event.y)))
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

    private fun hostPointAt(x: Float, y: Float): NxPoint? =
        if (mouseMode == NxMouseMode.Trackpad) pointer.cursorPoint() else pointer.directPoint(x, y)

    private fun singleFingerPoint(event: MotionEvent, now: Long): NxPoint? {
        if (mouseMode != NxMouseMode.Trackpad) return pointer.directPoint(event.x, event.y)
        val dx = event.x - lastTouchX
        val dy = event.y - lastTouchY
        val elapsed = (now - lastTouchAt).coerceIn(1L, 100L)
        lastTouchX = event.x
        lastTouchY = event.y
        lastTouchAt = now
        if (dx == 0f && dy == 0f) return pointer.cursorPoint()
        return pointer.moveRelativeBy(dx, dy, elapsed) ?: pointer.cursorPoint()
    }

    private fun centroidX(event: MotionEvent): Float {
        val count = minOf(2, event.pointerCount)
        if (count == 0) return 0f
        var total = 0f
        for (index in 0 until count) total += event.getX(index)
        return total / count
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
        var onlyMove: NxTouchAction.Move? = null
        if (actions.size == 1 && actions[0] is NxTouchAction.Move) {
            onlyMove = actions[0] as NxTouchAction.Move
        }
        if (onlyMove != null) {
            enqueueMove(NxPoint(onlyMove.x, onlyMove.y))
            return
        }
        val fling = actions.filterIsInstance<NxTouchAction.Fling>().firstOrNull()
        if (fling != null) startFling(fling)
        if (actions.any { it is NxTouchAction.BeginDrag }) {
            runOnUiThread { if (!destroyed && ::touchControls.isInitialized) touchControls.showDragging() }
            haptic(HapticFeedbackConstants.LONG_PRESS)
        } else if (actions.any { it is NxTouchAction.EndDrag || it is NxTouchAction.ReleaseAll }) {
            runOnUiThread { if (!destroyed && ::touchControls.isInitialized) touchControls.showDefaultHint() }
        }
        if (actions.any { it is NxTouchAction.Click || it is NxTouchAction.RightClick }) {
            haptic(HapticFeedbackConstants.VIRTUAL_KEY)
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
                        is NxTouchAction.RightClick -> {
                            active.move(action.x.toLong(), action.y.toLong())
                            active.mouseDown(1L)
                            active.mouseUp(1L)
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
                        is NxTouchAction.ScrollH -> active.wheelH(action.delta.toLong())
                        is NxTouchAction.Fling -> Unit
                        NxTouchAction.ReleaseAll -> active.releaseAll()
                    }
                }
            } catch (_: Exception) {
                runCatching { active?.releaseAll() }
            }
        }
    }

    private fun enqueueMove(point: NxPoint) {
        synchronized(moveLock) {
            pendingMove = point
            if (moveScheduled) return
            moveScheduled = true
        }
        io.execute {
            val next = synchronized(moveLock) {
                val value = pendingMove
                pendingMove = null
                moveScheduled = false
                value
            } ?: return@execute
            runCatching { session?.move(next.x.toLong(), next.y.toLong()) }
        }
    }

    private fun startFling(action: NxTouchAction.Fling) {
        val speed = hypot(action.velocityX.toDouble(), action.velocityY.toDouble()).toFloat()
        if (speed < NxTouchGesture.MinFlingSpeed) return
        flingVelocityX = action.velocityX.coerceIn(-MaxFlingVelocity, MaxFlingVelocity)
        flingVelocityY = action.velocityY.coerceIn(-MaxFlingVelocity, MaxFlingVelocity)
        flingAccumulatorX = 0f
        flingAccumulatorY = 0f
        lastFlingAt = SystemClock.uptimeMillis()
        if (!flingActive) {
            flingActive = true
            flingHandler.post(flingRunnable)
        }
    }

    private fun cancelFling() {
        flingActive = false
        flingHandler.removeCallbacks(flingRunnable)
        flingVelocityX = 0f
        flingVelocityY = 0f
        flingAccumulatorX = 0f
        flingAccumulatorY = 0f
    }

    private val flingRunnable = object : Runnable {
        override fun run() {
            if (!flingActive || destroyed) return
            val now = SystemClock.uptimeMillis()
            val elapsed = (now - lastFlingAt).coerceIn(1L, 40L)
            lastFlingAt = now
            flingAccumulatorX += flingVelocityX * elapsed * FlingWheelScale
            flingAccumulatorY += flingVelocityY * elapsed * FlingWheelScale
            val actions = ArrayList<NxTouchAction>(2)
            val vertical = (flingAccumulatorY / wheelStepPx).toInt()
            if (vertical != 0) {
                flingAccumulatorY -= vertical * wheelStepPx
                actions += NxTouchAction.Scroll(vertical * NxTouchGesture.WheelDelta)
            }
            val horizontal = (flingAccumulatorX / wheelStepPx).toInt()
            if (horizontal != 0) {
                flingAccumulatorX -= horizontal * wheelStepPx
                actions += NxTouchAction.ScrollH(horizontal * NxTouchGesture.WheelDelta)
            }
            if (actions.isNotEmpty()) sendGestureActions(actions)
            val decay = Math.pow(0.93, elapsed / 16.0).toFloat()
            flingVelocityX *= decay
            flingVelocityY *= decay
            if (hypot(flingVelocityX.toDouble(), flingVelocityY.toDouble()) >= MinFlingVelocity) {
                flingHandler.postDelayed(this, 16)
            } else {
                flingActive = false
            }
        }
    }

    private fun haptic(constant: Int) {
        if (::screen.isInitialized) screen.performHapticFeedback(constant)
    }

    private fun releaseAll() {
        cancelLongPress()
        cancelFling()
        touchSequence++
        sendGestureActions(touchGesture.onReleaseAll())
    }

    private val statsRunnable = object : Runnable {
        override fun run() {
            if (destroyed) return
            val snapshot = connectionState.snapshot()
            if (snapshot.phase == NxConnectionPhase.Streaming) {
                val frames = frameCount
                frameCount = 0
                val width = lastFrameWidth
                val height = lastFrameHeight
                if (width > 0 && height > 0) {
                    touchControls.setStatus("已连接 · ${width}×${height} · ${frames} FPS")
                }
            } else {
                frameCount = 0
            }
            statsHandler.postDelayed(this, 1000)
        }
    }

    private fun cancelLongPress() {
        gestureHandler.removeCallbacksAndMessages(null)
    }

    private fun closeTransport(clearScreen: Boolean) {
        val previous = session
        session = null
        cancelLongPress()
        cancelFling()
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
        if (::touchControls.isInitialized) renderConnection()
    }

    override fun onKeyDown(keyCode: Int, event: KeyEvent): Boolean {
        if (::touchControls.isInitialized) {
            when (keyCode) {
                KeyEvent.KEYCODE_F1 -> {
                    localKeys += keyCode
                    if (event.repeatCount == 0) onSessionControl(RemoteControlAction.Toggle, touchControls.menuButton)
                    return true
                }
                KeyEvent.KEYCODE_F2 -> {
                    localKeys += keyCode
                    if (event.repeatCount == 0) {
                        releaseAll()
                        touchControls.collapseMenu()
                        showTextDialog()
                    }
                    return true
                }
                KeyEvent.KEYCODE_ESCAPE -> if (touchControls.isExpanded) {
                    if (event.action == KeyEvent.ACTION_DOWN) localKeys += keyCode
                    if (event.repeatCount == 0) {
                        releaseAll()
                        touchControls.collapseMenu()
                    }
                    return true
                }
            }
        }
        return super.onKeyDown(keyCode, event)
    }

    override fun onKeyUp(keyCode: Int, event: KeyEvent): Boolean {
        if (localKeys.remove(keyCode)) return true
        return super.onKeyUp(keyCode, event)
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (!hasFocus) {
            releaseAll()
        } else if (::screen.isInitialized) {
            hideSystemBars(screen.rootView)
        }
    }

    private fun hideSystemBars(view: View) {
        WindowCompat.getInsetsController(window, view).apply {
            isAppearanceLightStatusBars = false
            isAppearanceLightNavigationBars = false
            systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            hide(WindowInsetsCompat.Type.systemBars())
        }
    }

    override fun onStop() {
        timeoutHandler.removeCallbacksAndMessages(null)
        localKeys.clear()
        cancelFling()
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
        statsHandler.removeCallbacksAndMessages(null)
        localKeys.clear()
        gestureHandler.removeCallbacksAndMessages(null)
        flingHandler.removeCallbacksAndMessages(null)
        val attempt = connectionState.snapshot().attempt
        if (attempt > 0) connectionState.background(attempt)
        destroyed = true
        generation++
        closeTransport(clearScreen = true)
        io.shutdown()
        super.onDestroy()
    }

    private fun dp(value: Int) = (value * resources.displayMetrics.density).toInt()
    private fun toast(message: String) = Toast.makeText(this, message, Toast.LENGTH_LONG).show()

    companion object {
        private const val MaxFlingVelocity = 2.2f
        private const val MinFlingVelocity = 0.04f
        private const val FlingWheelScale = 0.5f
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
