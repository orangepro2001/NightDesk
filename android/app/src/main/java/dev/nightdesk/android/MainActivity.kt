package dev.nightdesk.android

import android.Manifest
import android.app.AlertDialog
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.AdapterView
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.Spinner
import android.widget.Switch
import android.widget.TextView
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.content.FileProvider
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import java.util.concurrent.Executors

class MainActivity : ComponentActivity() {
    private val io = Executors.newSingleThreadExecutor()
    private lateinit var profileStore: ProfileStore
    private lateinit var host: EditText
    private lateinit var tokenBox: EditText
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
    private var hosts: Hosts? = null
    private var pickerSignature = ""
    private var pickerGuard = false
    private var profile: Profile? = null
    private var status: Status? = null
    private var working = false
    private var allowToggle = true
    private val green = 0xff0d6b60.toInt()

    private val cameraPermission = registerForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) launchScanner() else toast("需要相机权限才能扫描桌面二维码")
    }
    private val scanner = registerForActivityResult(ScanContract()) { result ->
        if (result.contents != null) pairWithQr(result.contents)
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        profileStore = ProfileStore(this)
        buildUi()
        hosts = try { profileStore.loadHosts() } catch (_: Exception) { profileStore.clear(); null }
        profile = hosts?.active?.profile
        renderHostPicker()
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
    }

    private fun buildUi() {
        val body = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(22), dp(16), dp(22), dp(30))
            setBackgroundColor(0xfff3f5f1.toInt())
        }
        setContentView(ScrollView(this).apply { addView(body) })
        body.addView(text("NIGHTDESK", 12, green, true))
        body.addView(text("远程工作台", 30, 0xff17302c.toInt(), true), params(0, 4))
        body.addView(text("Windows 显示与电源控制", 14, 0xff66736f.toInt()))

        body.addView(section("连接设备"))
        scanButton = button("扫描桌面二维码绑定") { requestScan() }
        body.addView(scanButton, params(10))
        body.addView(text("二维码 5 分钟内有效且只能使用一次；绑定令牌只在桌面端撤销时失效。", 12, 0xff66736f.toInt()), params(4))

        body.addView(text("已保存的主机", 14, 0xff17302c.toInt(), true), params(18))
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
        body.addView(hostPicker, params(6))
        deleteHost = button("删除当前主机") {
            val active = hosts?.active ?: return@button toast("还没有保存的主机")
            confirm("从本机删除 ${active.label}？桌面端的设备绑定不会被撤销。") {
                applyHosts(profileStore.remove(active.id), reload = true)
                if (hosts?.active == null) {
                    host.setText("https://")
                    tokenBox.text.clear()
                }
            }
        }
        body.addView(deleteHost, params(4))

        body.addView(text("兼容方式：手动输入地址和访问令牌", 14, 0xff17302c.toInt(), true), params(18))
        host = edit("Tailscale HTTPS 地址").apply { setText("https://") }
        body.addView(host, params(8))
        tokenBox = edit("桌面访问令牌").apply { inputType = 129 }
        body.addView(tokenBox, params(8))
        connect = button("保存手动连接") { saveProfile() }
        body.addView(connect, params(10))
        body.addView(button("清除全部主机") {
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
        }, params(4))
        connection = text("尚未连接", 14, 0xff66736f.toInt())
        body.addView(connection, params(8))

        body.addView(section("电脑状态"))
        state = card("连接电脑后显示实时显示状态")
        body.addView(state, params(8))
        body.addView(button("刷新状态") { refreshStatus() }, params(8))
        val pair = row()
        pair.addView(button("熄屏") { command("off") }, weight())
        pair.addView(space(8))
        pair.addView(button("点亮 / 恢复") { command("on") }, weight())
        body.addView(pair, params(8))
        body.addView(button("恢复保存布局") { confirm("恢复最近一次保存的显示布局？") { command("restore") } }, params(6))
        busySwitch = Switch(this).apply {
            text = "保持电脑不休眠"
            textSize = 15f
            setTextColor(0xff17302c.toInt())
            setOnCheckedChangeListener { _, checked ->
                if (allowToggle && status != null) command("prevent-sleep", mapOf("enabled" to checked))
            }
        }
        body.addView(busySwitch, params(6))

        body.addView(section("虚拟显示器"))
        displays = text("未连接", 14, 0xff66736f.toInt())
        body.addView(displays, params(8))
        val remove = row()
        index = edit("显示器索引").apply { inputType = 2; setText("0") }
        remove.addView(index, weight())
        remove.addView(space(8))
        remove.addView(button("删除选中") {
            val number = index.text.toString().toIntOrNull()
            if (number == null) toast("请输入显示器索引") else confirm("删除虚拟显示器 #$number？") {
                command("remove", mapOf("index" to number))
            }
        }, weight())
        body.addView(remove, params(8))
        val mode = row()
        width = edit("宽度").apply { inputType = 2; setText("1920") }
        height = edit("高度").apply { inputType = 2; setText("1080") }
        rate = edit("刷新率").apply { inputType = 2; setText("60") }
        mode.addView(width, weight())
        mode.addView(space(8))
        mode.addView(height, weight())
        mode.addView(space(8))
        mode.addView(rate, weight())
        body.addView(mode, params(8))
        body.addView(button("添加虚拟显示器") {
            val w = width.text.toString().toIntOrNull()
            val h = height.text.toString().toIntOrNull()
            val r = rate.text.toString().toIntOrNull()
            if (w == null || h == null || r == null || w !in 640..7680 || h !in 480..4320 || r !in 24..360)
                toast("分辨率或刷新率无效")
            else confirm("添加 ${w}×${h} @ ${r}Hz？") {
                command("add", mapOf("width" to w, "height" to h, "refreshRate" to r))
            }
        }, params(8))

        body.addView(section("应用更新"))
        body.addView(text("自动检查已开启 · 每 6 小时检查", 13, 0xff66736f.toInt()), params(6))
        body.addView(text("独立更新源：$DEFAULT_UPDATE_ORIGIN", 12, 0xff66736f.toInt()), params(4))
        body.addView(button("现在检查更新") { checkUpdate(true) }, params(6))
        body.addView(button("安装已验证的更新") { installUpdate() }, params(4))
        body.addView(text("当前版本 ${BuildConfig.VERSION_NAME} · 安装须经 Android 系统确认", 12, 0xff66736f.toInt()), params(8))
    }

    private fun requestScan() {
        if (working) return toast("当前操作完成后再扫描")
        if (checkSelfPermission(Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) launchScanner()
        else cameraPermission.launch(Manifest.permission.CAMERA)
    }

    private fun launchScanner() {
        scanner.launch(ScanOptions().apply {
            setDesiredBarcodeFormats(ScanOptions.QR_CODE)
            setPrompt("扫描 NightDesk 桌面二维码")
            setBeepEnabled(false)
            setOrientationLocked(true)
        })
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
        val saved = profile ?: return
        if (working) {
            if (manual) toast("操作完成后再试")
            return
        }
        working = true
        connection.text = "正在检查并校验更新…"
        renderState()
        io.execute {
            try {
                val release = Updates(this).check(saved)
                runOnUiThread {
                    working = false
                    connection.text = if (release == null) "已是最新版本" else "v${release.versionName} 已下载并验证，可以安装"
                    renderState()
                }
            } catch (e: Exception) {
                runOnUiThread {
                    working = false
                    connection.text = e.message ?: "更新失败"
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

    private fun confirm(message: String, action: () -> Unit) = AlertDialog.Builder(this)
        .setMessage(message)
        .setNegativeButton("取消", null)
        .setPositiveButton("继续") { _, _ -> action() }
        .show()

    private fun toast(message: String) = Toast.makeText(this, message, Toast.LENGTH_LONG).show()
    private fun dp(value: Int) = (value * resources.displayMetrics.density).toInt()
    private fun text(value: String, size: Int, color: Int, bold: Boolean = false) = TextView(this).apply {
        text = value
        textSize = size.toFloat()
        setTextColor(color)
        if (bold) typeface = android.graphics.Typeface.DEFAULT_BOLD
    }
    private fun section(value: String) = text(value, 18, 0xff17302c.toInt(), true).apply { setPadding(0, dp(22), 0, 0) }
    private fun edit(hintText: String) = EditText(this).apply {
        hint = hintText
        setSingleLine(true)
        textSize = 14f
        setPadding(dp(12), dp(10), dp(12), dp(10))
        setBackgroundColor(-1)
    }
    private fun card(value: String) = text(value, 14, 0xff17302c.toInt()).apply {
        setPadding(dp(14), dp(12), dp(14), dp(12))
        setBackgroundColor(-1)
    }
    private fun button(value: String, action: () -> Unit) = Button(this).apply {
        text = value
        isAllCaps = false
        textSize = 14f
        setTextColor(green)
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