package dev.nightdesk.android

import android.Manifest
import android.app.AlertDialog
import android.content.Intent
import android.content.pm.PackageManager
import android.content.res.ColorStateList
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.graphics.drawable.GradientDrawable
import android.widget.AdapterView
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.Spinner
import android.widget.Switch
import android.widget.TextView
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.content.FileProvider
import androidx.core.view.ViewCompat
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import org.json.JSONObject
import java.util.concurrent.Executors

class MainActivity : ComponentActivity() {
    private val io = Executors.newSingleThreadExecutor()
    private lateinit var profileStore: ProfileStore
    private lateinit var host: EditText
    private lateinit var tokenBox: EditText
    private lateinit var updateStatus: TextView
    private lateinit var connection: TextView
    private lateinit var state: TextView
    private lateinit var displays: TextView
    private lateinit var busySwitch: Switch
    private lateinit var width: EditText
    private lateinit var height: EditText
    private lateinit var rate: EditText
    private lateinit var index: EditText
    private lateinit var connect: Button
    private lateinit var scanButton: Button
    private lateinit var hostPicker: Spinner
    private lateinit var deleteHost: Button
    private lateinit var nxPicker: Spinner
    private lateinit var nxOpenButton: Button
    private lateinit var nxDeleteButton: Button
    private lateinit var nxHint: TextView
    private lateinit var nxStore: NxProfileStore
    private var hosts: Hosts? = null
    private var nxHosts = NxHosts(emptyList())
    private var pickerSignature = ""
    private var pickerGuard = false
    private var nxPickerSignature = ""
    private var profile: Profile? = null
    private var status: Status? = null
    private var working = false
    private var allowToggle = true
    private val green = 0xff0d6b60.toInt()
    private val pageNames = AppPages.names
    private val pageButtons = mutableListOf<Button>()
    private val pageViews = mutableListOf<View>()
    private lateinit var pageHost: FrameLayout
    private lateinit var pageTitle: TextView
    private var activePage = -1

    private val cameraPermission = registerForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) launchScanner() else toast("需要相机权限才能扫描桌面二维码")
    }
    private val scanner = registerForActivityResult(ScanContract()) { result ->
        if (result.contents != null) handleQr(result.contents)
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        profileStore = ProfileStore(this)
        nxStore = NxProfileStore(this)
        buildUi()
        hosts = try { profileStore.loadHosts() } catch (_: Exception) { profileStore.clear(); null }
        nxHosts = try { nxStore.load() } catch (_: Exception) {
            nxStore.clear()
            NxHosts(emptyList())
        }
        profile = hosts?.active?.profile
        renderHostPicker()
        renderNxHosts()
        profile?.let {
            host.setText(it.controlOrigin)
            tokenBox.setText(it.controlToken)
            connection.text = "已加密保存；正在连接…"
        }
        UpdateWorker.schedule(this)
        UpdateWorker.now(this)
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED)
            requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 7)
        profile?.let { saved ->
            execute("正在读取电脑状态…", { Api(saved).status() }, {
                status = it
                connection.text = "已连接 · ${saved.controlOrigin}"
                renderState()
                checkUpdate(false)
            })
        }
        renderState()
        showPage(savedInstanceState?.getInt("page", 0) ?: 0)
    }

    override fun onSaveInstanceState(outState: Bundle) {
        outState.putInt("page", activePage)
        super.onSaveInstanceState(outState)
    }

    private fun buildUi() {
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(0xfff3f5f1.toInt())
        }
        setContentView(root)
        WindowCompat.getInsetsController(window, root).isAppearanceLightStatusBars = true
        WindowCompat.getInsetsController(window, root).isAppearanceLightNavigationBars = true
        ViewCompat.setOnApplyWindowInsetsListener(root) { view, insets ->
            val bars = insets.getInsets(WindowInsetsCompat.Type.systemBars())
            view.setPadding(0, bars.top, 0, bars.bottom)
            insets
        }

        val header = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(20), dp(16), dp(20), dp(10))
        }
        header.addView(text("NIGHTDESK", 12, green, true))
        val titleRow = row()
        titleRow.addView(text("远程工作台", 29, 0xff17302c.toInt(), true), weight())
        pageTitle = text("首页", 13, green, true).apply {
            setPadding(dp(12), dp(6), dp(12), dp(6))
            background = rounded(0xffe2ece5.toInt(), 16)
        }
        titleRow.addView(pageTitle)
        header.addView(titleRow, params(6))
        header.addView(text("显示、被控与控制其它电脑已分页", 13, 0xff66736f.toInt()))
        root.addView(header)

        val nav = row().apply {
            setPadding(dp(12), dp(6), dp(12), dp(8))
            setBackgroundColor(0xffffffff.toInt())
        }
        pageNames.forEachIndexed { index, name ->
            val item = navButton(name) { showPage(index) }
            pageButtons.add(item)
            nav.addView(item, LinearLayout.LayoutParams(0, dp(48), 1f).apply { marginStart = dp(4); marginEnd = dp(4) })
        }

        pageHost = FrameLayout(this)
        root.addView(pageHost, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f))
        root.addView(nav)

        val home = newPage()
        val connectCard = addCard(home, "连接", "当前电脑", "扫码绑定电脑，或从已保存的设备切换。二维码同时支持 HTTPS 绑定和低带宽屏幕远控。")
        connection = text("尚未连接", 14, 0xff66736f.toInt())
        connectCard.addView(connection, params(6, 8))
        scanButton = button("扫描二维码") { requestScan() }
        connectCard.addView(scanButton, params(4))
        connectCard.addView(text("已保存的电脑", 13, 0xff66736f.toInt(), true), params(12, 4))
        hostPicker = Spinner(this).apply {
            onItemSelectedListener = object : AdapterView.OnItemSelectedListener {
                override fun onItemSelected(parent: AdapterView<*>?, view: View?, position: Int, id: Long) {
                    if (pickerGuard) return
                    val chosen = hosts?.items?.getOrNull(position) ?: return
                    if (chosen.id == hosts?.active?.id) return
                    switchHost(chosen.id)
                }
                override fun onNothingSelected(parent: AdapterView<*>?) {}
            }
        }
        connectCard.addView(hostPicker, params(2))

        val stateCard = addCard(home, "状态", "电脑显示状态", "连接后可快速熄屏、点亮、恢复布局和刷新状态。")
        state = text("连接电脑后显示实时显示状态", 14, 0xff66736f.toInt()).apply {
            setPadding(dp(12), dp(10), dp(12), dp(10))
            background = rounded(0xfff2f5f1.toInt(), 10)
        }
        stateCard.addView(state, params(4, 8))
        stateCard.addView(button("刷新状态") { refreshStatus() }, params(2))
        val quick1 = row()
        quick1.addView(secondaryButton("熄灭屏幕") { command("off") }, weight())
        quick1.addView(space(8))
        quick1.addView(secondaryButton("点亮 / 恢复") { command("on") }, weight())
        stateCard.addView(quick1, params(4))
        stateCard.addView(secondaryButton("恢复保存布局") { confirm("恢复最近一次保存的显示布局？") { command("restore") } }, params(2))
        busySwitch = Switch(this).apply {
            text = "保持电脑不休眠"
            textSize = 15f
            setTextColor(0xff17302c.toInt())
            setOnCheckedChangeListener { _, checked -> if (allowToggle && status != null) command("prevent-sleep", mapOf("enabled" to checked)) }
        }
        stateCard.addView(busySwitch, params(8))

        val display = newPage()
        val displayCard = addCard(display, "显示", "虚拟显示器", "这里只显示本程序创建的虚拟输出。增删会改变显示拓扑，请在可恢复现场操作。")
        displays = text("未连接", 14, 0xff66736f.toInt()).apply {
            setPadding(dp(12), dp(10), dp(12), dp(10))
            background = rounded(0xfff2f5f1.toInt(), 10)
        }
        displayCard.addView(displays, params(4, 10))
        val remove = row()
        index = edit("显示器索引").apply { inputType = 2; setText("0") }
        remove.addView(index, weight())
        remove.addView(space(8))
        remove.addView(secondaryButton("删除选中") {
            val number = index.text.toString().toIntOrNull()
            if (number == null) toast("请输入显示器索引") else confirm("删除虚拟显示器 #$number？") { command("remove", mapOf("index" to number)) }
        }, weight())
        displayCard.addView(remove, params(2))

        val modeCard = addCard(display, "新建", "添加虚拟显示器", "1920×1080 @ 60Hz 是默认值，实际支持范围由驱动决定。")
        val mode = row()
        width = edit("宽度").apply { inputType = 2; setText("1920") }
        height = edit("高度").apply { inputType = 2; setText("1080") }
        rate = edit("刷新率").apply { inputType = 2; setText("60") }
        mode.addView(width, weight())
        mode.addView(space(8))
        mode.addView(height, weight())
        mode.addView(space(8))
        mode.addView(rate, weight())
        modeCard.addView(mode, params(4, 8))
        modeCard.addView(button("添加虚拟显示器") {
            val w = width.text.toString().toIntOrNull()
            val h = height.text.toString().toIntOrNull()
            val r = rate.text.toString().toIntOrNull()
            if (w == null || h == null || r == null || w !in 640..7680 || h !in 480..4320 || r !in 24..360) toast("分辨率或刷新率无效")
            else confirm("添加 " + w + "×" + h + " @ " + r + "Hz？") { command("add", mapOf("width" to w, "height" to h, "refreshRate" to r)) }
        }, params(2))
        modeCard.addView(text("为保护恢复快照，虚拟接管期间需先点亮再增删显示器。", 12, 0xff8a5a35.toInt()), params(6))

        val remote = newPage()
        val nxCard = addCard(remote, "低带宽远控", "保存的屏幕远控主机", "首次扫码会校验证书指纹并立即连接；配对码只留在本次连接内存中。")
        nxHint = text("尚未保存屏幕远控主机", 12, 0xff66736f.toInt())
        nxCard.addView(secondaryButton("扫描屏幕远控二维码") { requestScan() }, params(4, 8))
        nxCard.addView(nxHint, params(2, 4))
        nxPicker = Spinner(this)
        nxCard.addView(nxPicker, params(2))
        val nxActions = row()
        nxOpenButton = button("打开选中主机") { openNxHost() }
        nxDeleteButton = secondaryButton("删除选中主机") { deleteNxHost() }
        nxActions.addView(nxOpenButton, weight())
        nxActions.addView(space(8))
        nxActions.addView(nxDeleteButton, weight())
        nxCard.addView(nxActions, params(4))
        nxCard.addView(text("适合静态桌面和低带宽网络，不适合视频或游戏画面。", 12, 0xff66736f.toInt()), params(4))

        val manualCard = addCard(remote, "兼容连接", "手动地址与访问令牌", "通常优先使用二维码。手动模式只用于兼容已有 HTTPS 控制地址。")
        host = edit("Tailscale HTTPS 地址").apply { setText("https://") }
        manualCard.addView(host, params(4))
        tokenBox = edit("桌面访问令牌").apply { inputType = 129 }
        manualCard.addView(tokenBox, params(4))
        connect = button("保存手动连接") { saveProfile() }
        manualCard.addView(connect, params(4))
        deleteHost = secondaryButton("删除当前主机") {
            val active = hosts?.active ?: return@secondaryButton toast("还没有保存的主机")
            confirm("从本机删除 " + active.label + "？桌面端的设备绑定不会被撤销。") {
                applyHosts(profileStore.remove(active.id), reload = true)
                if (hosts?.active == null) {
                    host.setText("https://")
                    tokenBox.text.clear()
                }
            }
        }
        manualCard.addView(deleteHost, params(2))
        manualCard.addView(secondaryButton("清除全部主机") {
            confirm("删除本机加密保存的全部电脑地址和令牌？") {
                profileStore.clear()
                hosts = null
                profile = null
                status = null
                host.setText("https://")
                tokenBox.text.clear()
                connection.text = "尚未连接"
                renderState()
            }
        }, params(2))

        val updates = newPage()
        val updateCard = addCard(updates, "更新", "NightDesk Android", "自动检查已开启，每 6 小时检查一次。下载后仍需 Android 系统安装确认。")
        updateCard.addView(text("当前版本 " + BuildConfig.VERSION_NAME, 14, 0xff17302c.toInt(), true), params(4))
        updateCard.addView(text("更新源：$DEFAULT_UPDATE_ORIGIN", 12, 0xff66736f.toInt()), params(4, 10))
        updateStatus = text("等待检查更新", 14, green)
        updateCard.addView(updateStatus, params(6, 10))
        updateCard.addView(button("现在检查更新") { checkUpdate(true) }, params(2))
        updateCard.addView(secondaryButton("安装已验证的更新") { installUpdate() }, params(2))
        updateCard.addView(text("低带宽远控不会在这里更新；远控主机由桌面端独立维护。", 12, 0xff66736f.toInt()), params(8))

        showPage(0)
    }

    private fun requestScan() {
        if (working) return toast("当前操作完成后再扫描")
        if (checkSelfPermission(Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) launchScanner()
        else cameraPermission.launch(Manifest.permission.CAMERA)
    }

    private fun launchScanner() {
        scanner.launch(ScanOptions().apply {
            setDesiredBarcodeFormats(ScanOptions.QR_CODE)
            setPrompt("扫描 NightDesk 绑定或屏幕远控二维码")
            setBeepEnabled(false)
            setOrientationLocked(true)
        })
    }

    private fun handleQr(raw: String) {
        val kind = try { JSONObject(raw).optString("kind") } catch (_: Exception) { "" }
        when (kind) {
            "nightdesk-pair" -> pairWithQr(raw)
            "nightdesk-nx" -> pairNx(raw)
            else -> toast("二维码不是 NightDesk 设备绑定或屏幕远控信息")
        }
    }

    private fun pairNx(raw: String) {
        if (working) return toast("当前操作完成后再连接屏幕远控")
        val pairing = try { NxPairing.parse(raw) } catch (e: Exception) {
            return toast(e.message ?: "屏幕远控二维码无效")
        }
        val host = pairing.host()
        nxHosts = try { nxStore.upsert(host) } catch (e: Exception) {
            return toast(e.message ?: "保存屏幕远控主机失败")
        }
        renderNxHosts()
        startActivity(RemoteActivity.intent(this, host, pairing.code))
    }

    private fun pairWithQr(raw: String) {
        if (working) return toast("当前操作完成后再绑定")
        val payload = try { PairingPayload.parse(raw) } catch (e: Exception) {
            return toast(e.message ?: "二维码无效")
        }
        working = true
        connection.text = "正在验证二维码并绑定…"
        renderState()
        io.execute {
            try {
                val saved = PairingClient.pair(payload, PairingClient.defaultDeviceName(Build.MANUFACTURER, Build.MODEL))
                val stored = profileStore.upsert(saved)
                val result = runCatching { Api(saved).status() }
                runOnUiThread {
                    working = false
                    hosts = stored
                    profile = saved
                    renderHostPicker()
                    status = result.getOrNull()
                    host.setText(saved.controlOrigin)
                    tokenBox.setText(saved.controlToken)
                    connection.text = if (status != null) "已扫码绑定 · ${saved.controlOrigin}"
                        else "绑定成功，但暂时无法读取状态：${result.exceptionOrNull()?.message}"
                    renderState()
                    UpdateWorker.now(this)
                    if (status != null) checkUpdate(false)
                }
            } catch (e: Exception) {
                runOnUiThread {
                    working = false
                    connection.text = e.message ?: "扫码绑定失败"
                    renderState()
                }
            }
        }
    }

    private fun saveProfile() {
        try {
            val saved = Profile(
                Endpoints.origin(host.text.toString()),
                tokenBox.text.toString().trim(),
                profile?.updateOrigin ?: DEFAULT_UPDATE_ORIGIN
            )
            require(ProfileCodec.validToken(saved.controlToken)) { "访问令牌格式无效" }
            hosts = profileStore.upsert(saved)
            profile = saved
            renderHostPicker()
            UpdateWorker.now(this)
            host.setText(saved.controlOrigin)
            connection.text = "正在安全连接…"
            execute("正在读取电脑状态…", { Api(saved).status() }, {
                status = it
                connection.text = "已连接 · ${saved.controlOrigin}"
                renderState()
                checkUpdate(false)
            })
        } catch (e: Exception) {
            toast(e.message ?: "地址或令牌无效")
        }
    }

    private fun refreshStatus() {
        val saved = profile ?: return toast("请先连接电脑")
        execute("正在读取电脑状态…", { Api(saved).status() }, {
            status = it
            connection.text = "已连接 · ${saved.controlOrigin}"
            renderState()
        })
    }

    private fun command(name: String, extra: Map<String, Any> = emptyMap()) {
        val saved = profile ?: return toast("请先连接电脑")
        execute("正在执行 $name…", { Api(saved).command(name, extra) }, {
            status = it
            connection.text = "命令完成"
            renderState()
        })
    }

    private fun execute(message: String, action: () -> Status, done: (Status) -> Unit) {
        if (working) return
        working = true
        connection.text = message
        renderState()
        io.execute {
            try {
                val value = action()
                runOnUiThread {
                    working = false
                    done(value)
                    renderState()
                }
            } catch (e: Exception) {
                runOnUiThread {
                    working = false
                    connection.text = e.message ?: "操作失败"
                    renderState()
                }
            }
        }
    }

    private fun checkUpdate(manual: Boolean) {
        val saved = profile ?: Profile(DEFAULT_UPDATE_ORIGIN, "", DEFAULT_UPDATE_ORIGIN)
        if (working) {
            if (manual) toast("操作完成后再试")
            return
        }
        working = true
        updateStatus.text = "正在检查并校验更新…"
        renderState()
        io.execute {
            try {
                val release = Updates(this).check(saved)
                runOnUiThread {
                    working = false
                    updateStatus.text = if (release == null) "已是最新版本" else "v${release.versionName} 已下载并验证，可以安装"
                    renderState()
                }
            } catch (e: Exception) {
                runOnUiThread {
                    working = false
                    updateStatus.text = e.message ?: "更新失败"
                    renderState()
                }
            }
        }
    }

    private fun installUpdate() {
        val updates = Updates(this)
        val release = updates.ready() ?: return toast("没有已验证的待安装更新")
        try {
            val file = updates.file()
            if (Build.VERSION.SDK_INT >= 26 && !packageManager.canRequestPackageInstalls()) {
                startActivity(Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:$packageName")))
                toast("请允许 NightDesk 安装更新，然后返回并再次点击安装")
                return
            }
            val uri = FileProvider.getUriForFile(this, "$packageName.files", file)
            startActivity(Intent(Intent.ACTION_INSTALL_PACKAGE).setData(uri).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION))
        } catch (e: Exception) {
            toast(e.message ?: "启动系统安装器失败")
        }
    }

    private fun renderHostPicker() {
        if (!::hostPicker.isInitialized) return
        val items = hosts?.items.orEmpty()
        val labels = items.map { if (it.id == (hosts?.active?.id ?: "")) "● ${it.label}" else "○ ${it.label}" }
        val signature = (hosts?.active?.id ?: "") + "|" + labels.joinToString("\u0000")
        pickerGuard = true
        if (signature != pickerSignature) {
            hostPicker.adapter = ArrayAdapter(this, android.R.layout.simple_spinner_dropdown_item, labels)
            pickerSignature = signature
        }
        val position = items.indexOfFirst { it.id == hosts?.active?.id }
        if (position >= 0 && hostPicker.selectedItemPosition != position) hostPicker.setSelection(position)
        hostPicker.isEnabled = !working && items.size > 1
        pickerGuard = false
        if (::deleteHost.isInitialized) deleteHost.isEnabled = !working && items.isNotEmpty()
    }

    private fun renderNxHosts() {
        if (!::nxPicker.isInitialized) return
        val items = nxHosts.items
        val selectedId = items.getOrNull(nxPicker.selectedItemPosition)?.id
        val labels = items.map { "${it.name} · ${it.addr}" }
        val signature = labels.joinToString("\u0000")
        if (signature != nxPickerSignature) {
            nxPicker.adapter = ArrayAdapter(this, android.R.layout.simple_spinner_dropdown_item, labels)
            nxPickerSignature = signature
        }
        val position = items.indexOfFirst { it.id == selectedId }.let { if (it >= 0) it else 0 }
        if (items.isNotEmpty() && nxPicker.selectedItemPosition != position) nxPicker.setSelection(position)
        nxPicker.isEnabled = items.size > 1
        nxOpenButton.isEnabled = items.isNotEmpty()
        nxDeleteButton.isEnabled = items.isNotEmpty()
        nxHint.text = if (items.isEmpty()) "尚未保存屏幕远控主机"
        else "${items.size} 台主机 · 证书指纹已固定；点击打开会优先尝试无码重连"
    }

    private fun openNxHost() {
        val chosen = nxHosts.items.getOrNull(nxPicker.selectedItemPosition)
            ?: return toast("还没有保存的屏幕远控主机")
        startActivity(RemoteActivity.intent(this, chosen, null))
    }

    private fun deleteNxHost() {
        val chosen = nxHosts.items.getOrNull(nxPicker.selectedItemPosition)
            ?: return toast("还没有保存的屏幕远控主机")
        confirm("从本机删除屏幕远控主机 ${chosen.name}？密码学信任仍保留在 Windows 端。") {
            nxHosts = try { nxStore.remove(chosen.id) } catch (e: Exception) {
                toast(e.message ?: "删除屏幕远控主机失败")
                return@confirm
            }
            nxPickerSignature = ""
            renderNxHosts()
        }
    }

    private fun applyHosts(updated: Hosts, reload: Boolean = false) {
        hosts = updated
        profile = updated.active?.profile
        status = null
        renderHostPicker()
        val current = profile
        if (current == null) {
            connection.text = "尚未连接"
            renderState()
            return
        }
        host.setText(current.controlOrigin)
        tokenBox.setText(current.controlToken)
        connection.text = "已切换到 ${updated.active?.label} · ${current.controlOrigin}"
        if (reload) refreshStatus() else renderState()
    }

    private fun switchHost(id: String) {
        val updated = try { profileStore.setActive(id) } catch (e: Exception) {
            toast(e.message ?: "切换主机失败")
            return
        }
        applyHosts(updated, reload = true)
    }

    private fun renderState() {
        if (!::state.isInitialized) return
        renderHostPicker()
        renderNxHosts()
        val current = status
        state.text = if (current == null) "连接电脑后显示实时显示状态" else
            "${if (current.remoteMode) "远程虚拟屏模式" else "本机显示模式"} · ${if (current.busy) "电脑忙碌" else "就绪"}\n" +
            "显示模式 ${current.configuredMode}\n活动输出 ${current.monitors.count { it.active }} · Parsec " +
            "${current.monitors.count { it.active && it.isParsec }} · ${if (current.recoverable) "有恢复快照" else "无恢复快照"}"
        displays.text = current?.managedDisplays?.joinToString("\n") { "#${it.index}  ${it.deviceName} · ${it.mode}" } ?: "未连接"
        allowToggle = false
        busySwitch.isChecked = current?.preventSleep ?: false
        allowToggle = true
        connect.isEnabled = !working
        scanButton.isEnabled = !working
        listOf(host, tokenBox, width, height, rate, index, busySwitch).forEach {
            it.isEnabled = !working && (it !== busySwitch || status != null)
        }
    }

    override fun onResume() {
        super.onResume()
        if (::nxStore.isInitialized) {
            runCatching { nxStore.load() }.onSuccess {
                nxHosts = it
                nxPickerSignature = ""
                renderNxHosts()
            }
        }
    }

    private fun confirm(message: String, action: () -> Unit) = AlertDialog.Builder(this)
        .setMessage(message)
        .setNegativeButton("取消", null)
        .setPositiveButton("继续") { _, _ -> action() }
        .show()

    private fun newPage(): LinearLayout {
        val scroll = ScrollView(this).apply { isFillViewport = true }
        val content = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(20), dp(16), dp(20), dp(30))
            setBackgroundColor(0xfff3f5f1.toInt())
        }
        scroll.addView(content)
        pageHost.addView(scroll, FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))
        pageViews.add(scroll)
        return content
    }

    fun showPage(index: Int) {
        if (index !in pageViews.indices) return
        activePage = index
        pageViews.forEachIndexed { i, view -> view.visibility = if (i == index) View.VISIBLE else View.GONE }
        pageButtons.forEachIndexed { i, button ->
            button.background = rounded(if (i == index) green else 0xffffffff.toInt(), 18, if (i == index) null else 0xffdbe4dc.toInt())
            button.setTextColor(if (i == index) 0xffffffff.toInt() else green)
        }
        pageTitle.text = pageNames[index]
    }

    private fun navButton(value: String, action: () -> Unit) = Button(this).apply {
        text = value
        isAllCaps = false
        textSize = 14f
        minHeight = 0
        minimumHeight = 0
        setPadding(dp(6), 0, dp(6), 0)
        stateListAnimator = null
        setOnClickListener { action() }
        layoutParams = LinearLayout.LayoutParams(0, dp(38), 1f).apply { marginEnd = dp(4); marginStart = dp(4) }
    }

    private fun rounded(fill: Int, radius: Int, stroke: Int? = null) = GradientDrawable().apply {
        setColor(fill)
        cornerRadius = dp(radius).toFloat()
        if (stroke != null) setStroke(dp(1), stroke)
    }

    private fun addCard(parent: LinearLayout, kicker: String, title: String, body: String? = null): LinearLayout {
        val card = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(16), dp(16), dp(16), dp(16))
            background = rounded(0xffffffff.toInt(), 16, 0xffe0e7e1.toInt())
        }
        parent.addView(card, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(12) })
        card.addView(text(kicker.uppercase(), 11, green, true))
        card.addView(text(title, 20, 0xff17302c.toInt(), true), params(4, 4))
        if (body != null) card.addView(text(body, 12, 0xff66736f.toInt()), params(2, 8))
        return card
    }

    private fun toast(message: String) = Toast.makeText(this, message, Toast.LENGTH_LONG).show()
    private fun dp(value: Int) = (value * resources.displayMetrics.density).toInt()
    private fun text(value: String, size: Int, color: Int, bold: Boolean = false) = TextView(this).apply {
        text = value
        textSize = size.toFloat()
        setTextColor(color)
        if (bold) typeface = android.graphics.Typeface.DEFAULT_BOLD
    }
    private fun edit(hintText: String) = EditText(this).apply {
        hint = hintText
        setSingleLine(true)
        textSize = 14f
        minHeight = dp(48)
        setPadding(dp(12), dp(10), dp(12), dp(10))
        background = rounded(0xfff2f5f1.toInt(), 10, 0xffd8e1da.toInt())
    }
    private fun card(value: String) = text(value, 14, 0xff17302c.toInt()).apply {
        setPadding(dp(14), dp(12), dp(14), dp(12))
        setBackgroundColor(-1)
    }
    private fun button(value: String, action: () -> Unit) = Button(this).apply {
        text = value
        isAllCaps = false
        textSize = 14f
        minHeight = dp(46)
        setTextColor(0xffffffff.toInt())
        backgroundTintList = ColorStateList.valueOf(green)
        setOnClickListener { action() }
    }

    private fun secondaryButton(value: String, action: () -> Unit) = Button(this).apply {
        text = value
        isAllCaps = false
        textSize = 14f
        minHeight = dp(46)
        setTextColor(green)
        backgroundTintList = ColorStateList.valueOf(0xffffffff.toInt())
        setOnClickListener { action() }
    }
    private fun row() = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL }
    private fun space(value: Int) = View(this).apply { layoutParams = LinearLayout.LayoutParams(dp(value), 1) }
    private fun params(top: Int = 0, bottom: Int = 0) =
        LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            topMargin = dp(top)
            bottomMargin = dp(bottom)
        }
    private fun weight() = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)

    override fun onDestroy() {
        io.shutdownNow()
        super.onDestroy()
    }
}
