package dev.nightdesk.android

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import org.json.JSONArray
import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.io.File
import java.net.HttpURLConnection
import java.net.URI
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.spec.GCMParameterSpec

const val DEFAULT_UPDATE_ORIGIN = "https://tony-ai-pc-1.tail9f7f1f.ts.net"

data class Profile(
    val controlOrigin: String,
    val controlToken: String,
    val updateOrigin: String = DEFAULT_UPDATE_ORIGIN
)

data class PairingPayload(val origin: String, val ticket: String, val expiresAt: Long) {
    companion object {
        private val ticketPattern = Regex("^ndp1_[A-Za-z0-9_-]{43}$")

        fun parse(raw: String, nowSeconds: Long = System.currentTimeMillis() / 1000): PairingPayload {
            require(raw.length in 1..8192) { "二维码内容无效" }
            val json = try { JSONObject(raw) } catch (_: Exception) { throw IllegalArgumentException("二维码不是 NightDesk 配对信息") }
            require(json.optInt("v", 0) == 1 && json.optString("kind") == "nightdesk-pair") {
                "二维码不是 NightDesk 配对信息"
            }
            val ticket = json.optString("ticket")
            require(ticketPattern.matches(ticket)) { "二维码配对票据无效" }
            val expires = json.optLong("expires", 0)
            require(expires > nowSeconds) { "二维码已过期，请在桌面端重新生成" }
            return PairingPayload(Endpoints.origin(json.optString("origin")), ticket, expires)
        }
    }
}

data class Monitor(val deviceName: String, val description: String, val active: Boolean, val isParsec: Boolean)
data class ManagedDisplay(val index: Int, val deviceName: String, val mode: String)
data class Status(
    val remoteMode: Boolean,
    val preventSleep: Boolean,
    val busy: Boolean,
    val recoverable: Boolean,
    val configuredMode: String,
    val monitors: List<Monitor>,
    val managedDisplays: List<ManagedDisplay>
)

object Endpoints {
    private val hostPattern = Regex("^[a-z0-9-]{1,63}\\.tail[0-9a-f]{5,10}\\.ts\\.net$")

    fun origin(text: String): String {
        val uri = try { URI(text.trim()) } catch (_: Exception) { throw IllegalArgumentException("只允许 https://<电脑>.<tailnet>.ts.net") }
        val host = uri.host ?: ""
        if (uri.scheme != "https" || uri.port != -1 || uri.userInfo != null || !uri.query.isNullOrEmpty() || !uri.fragment.isNullOrEmpty() ||
            !(uri.path.isNullOrEmpty() || uri.path == "/") || !hostPattern.matches(host)
        ) throw IllegalArgumentException("只允许 https://<电脑>.<tailnet>.ts.net")
        return "https://$host"
    }
}

object ProfileCodec {
    fun validToken(token: String): Boolean = token.length in 32..256 && token.none(Char::isWhitespace)

    fun decode(json: JSONObject): Profile {
        val controlOrigin = Endpoints.origin(json.optString("controlOrigin", json.optString("origin")))
        val token = json.optString("controlToken", json.optString("token"))
        require(validToken(token)) { "保存的访问令牌无效" }
        val updateOrigin = Endpoints.origin(json.optString("updateOrigin", DEFAULT_UPDATE_ORIGIN))
        return Profile(controlOrigin, token, updateOrigin)
    }

    fun encode(profile: Profile): JSONObject = JSONObject().apply {
        put("schemaVersion", 2)
        put("controlOrigin", profile.controlOrigin)
        put("controlToken", profile.controlToken)
        put("updateOrigin", profile.updateOrigin)
    }
}

data class HostProfile(
    val id: String,
    val label: String,
    val profile: Profile,
    val addedAt: Long = 0
)

data class Hosts(val items: List<HostProfile>, val activeId: String?) {
    val active: HostProfile? get() = items.firstOrNull { it.id == activeId } ?: items.firstOrNull()
}

object HostsCodec {
    const val MaxHosts = 20

    fun label(origin: String): String =
        (runCatching { URI(origin).host }.getOrNull() ?: origin).substringBefore('.').take(48).ifBlank { "NightDesk" }

    fun decode(json: JSONObject): Hosts {
        val version = json.optInt("schemaVersion", 2)
        if (version < 3) {
            val profile = ProfileCodec.decode(json)
            val host = HostProfile(profile.controlOrigin, label(profile.controlOrigin), profile, 0)
            return Hosts(listOf(host), host.id)
        }
        require(version == 3) { "保存的连接数据版本不受支持" }
        val array = json.optJSONArray("hosts") ?: throw IllegalArgumentException("保存的主机列表无效")
        val items = (0 until array.length()).map { index ->
            val item = array.getJSONObject(index)
            val profile = ProfileCodec.decode(item)
            HostProfile(
                item.getString("id"),
                item.optString("label").ifBlank { label(profile.controlOrigin) },
                profile,
                item.optLong("addedAt", 0)
            )
        }
        require(items.size <= MaxHosts && items.map { it.id }.toSet().size == items.size) { "保存的主机列表无效" }
        val active = json.optString("activeId").takeIf { id -> items.any { it.id == id } } ?: items.firstOrNull()?.id
        return Hosts(items, active)
    }

    fun encode(hosts: Hosts): JSONObject = JSONObject().apply {
        put("schemaVersion", 3)
        put("activeId", hosts.active?.id ?: "")
        put("hosts", JSONArray().also { array ->
            hosts.items.forEach { host ->
                array.put(JSONObject().apply {
                    put("id", host.id)
                    put("label", host.label)
                    put("addedAt", host.addedAt)
                    put("controlOrigin", host.profile.controlOrigin)
                    put("controlToken", host.profile.controlToken)
                    put("updateOrigin", host.profile.updateOrigin)
                })
            }
        })
    }

    fun upsert(hosts: Hosts, profile: Profile, label: String? = null): Hosts {
        require(ProfileCodec.validToken(profile.controlToken)) { "访问令牌格式无效" }
        val existing = hosts.items.firstOrNull { it.id == profile.controlOrigin }
        require(existing != null || hosts.items.size < MaxHosts) { "最多保存 $MaxHosts 台主机，请先删除不用的主机" }
        val name = label?.trim()?.take(48)?.ifBlank { null }
        val item = if (existing != null) existing.copy(profile = profile, label = name ?: existing.label)
        else HostProfile(profile.controlOrigin, name ?: label(profile.controlOrigin), profile, System.currentTimeMillis())
        return Hosts(hosts.items.filterNot { it.id == item.id } + item, item.id)
    }

    fun remove(hosts: Hosts, id: String): Hosts {
        val items = hosts.items.filterNot { it.id == id }
        val active = items.firstOrNull { it.id == hosts.activeId }?.id ?: items.firstOrNull()?.id
        return Hosts(items, active)
    }
}

class ProfileStore(context: Context) {
    private val store = context.applicationContext.getSharedPreferences("profile", Context.MODE_PRIVATE)

    private fun key() = (KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        .getEntry("nightdesk", null) as? KeyStore.SecretKeyEntry)?.secretKey
        ?: KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore").apply {
            init(KeyGenParameterSpec.Builder("nightdesk", KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .build())
        }.generateKey()

    fun loadHosts(): Hosts? {
        val packed = store.getString("data", null)?.let { Base64.decode(it, Base64.NO_WRAP) } ?: return null
        if (packed.size <= 12) throw IllegalStateException("保存的连接数据无效")
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, packed.copyOfRange(0, 12)))
        val json = JSONObject(String(cipher.doFinal(packed.copyOfRange(12, packed.size)), Charsets.UTF_8))
        return HostsCodec.decode(json).takeIf { it.items.isNotEmpty() }
    }

    fun saveHosts(hosts: Hosts) {
        hosts.items.forEach { require(ProfileCodec.validToken(it.profile.controlToken)) { "访问令牌格式无效" } }
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, key())
        val data = cipher.doFinal(HostsCodec.encode(hosts).toString().toByteArray(Charsets.UTF_8))
        store.edit().putString("data", Base64.encodeToString(cipher.iv + data, Base64.NO_WRAP)).apply()
    }

    fun upsert(profile: Profile, label: String? = null): Hosts {
        val updated = HostsCodec.upsert(loadHosts() ?: Hosts(emptyList(), null), profile, label)
        saveHosts(updated)
        return updated
    }

    fun setActive(id: String): Hosts {
        val hosts = loadHosts() ?: Hosts(emptyList(), null)
        require(hosts.items.any { it.id == id }) { "主机不存在" }
        val updated = hosts.copy(activeId = id)
        saveHosts(updated)
        return updated
    }

    fun remove(id: String): Hosts {
        val updated = HostsCodec.remove(loadHosts() ?: Hosts(emptyList(), null), id)
        if (updated.items.isEmpty()) clear() else saveHosts(updated)
        return updated
    }

    fun clear() = store.edit().clear().apply()
}
class Api(private val profile: Profile) {
    private fun open(path: String, method: String = "GET"): HttpURLConnection {
        require(path.startsWith("/api/v1/"))
        return (URI(profile.controlOrigin).resolve(path).toURL().openConnection() as HttpURLConnection).apply {
            requestMethod = method
            connectTimeout = 8000
            readTimeout = 15000
            instanceFollowRedirects = false
            setRequestProperty("Authorization", "Bearer ${profile.controlToken}")
            setRequestProperty("Accept", "application/json")
            if (method == "POST") {
                doOutput = true
                setRequestProperty("Content-Type", "application/json; charset=utf-8")
            }
        }
    }

    fun status(): Status = open("/api/v1/status").useApi { connection ->
        checkControl(connection)
        status(JSONObject(read(connection, 262144)))
    }

    fun command(name: String, extra: Map<String, Any> = emptyMap()): Status {
        val body = JSONObject().put("command", name).also { json ->
            extra.forEach { (key, value) -> json.put(key, value) }
        }.toString().toByteArray(Charsets.UTF_8)
        return open("/api/v1/commands", "POST").useApi { connection ->
            connection.outputStream.use { it.write(body) }
            checkControl(connection)
            status(JSONObject(read(connection, 262144)).getJSONObject("status"))
        }
    }

    private fun checkControl(connection: HttpURLConnection) {
        if (connection.responseCode == 401) throw ApiException("设备绑定已被撤销，请重新扫码")
        if (connection.responseCode !in 200..299) {
            val message = runCatching { JSONObject(readError(connection)).getString("error") }
                .getOrDefault("HTTP ${connection.responseCode}")
            throw ApiException(message)
        }
    }

    private fun status(json: JSONObject): Status {
        fun array(name: String) = (0 until json.getJSONArray(name).length()).map { json.getJSONArray(name).getJSONObject(it) }
        val mode = json.getJSONObject("configuredMode")
        return Status(
            json.getBoolean("remoteMode"), json.getBoolean("preventSleep"), json.getBoolean("busy"), json.getBoolean("recoverable"),
            "${mode.getInt("width")}×${mode.getInt("height")} @ ${mode.getInt("refreshRate")}Hz",
            array("monitors").map {
                Monitor(it.getString("deviceName"), it.getString("description"), it.getBoolean("active"), it.getBoolean("isParsec"))
            },
            array("managedDisplays").map {
                ManagedDisplay(it.getInt("index"), it.getString("deviceName"), it.getString("mode"))
            }
        )
    }
}

object PairingClient {
    fun defaultDeviceName(manufacturer: String?, model: String?): String {
        val maker = manufacturer?.trim().orEmpty()
        val device = model?.trim().orEmpty()
        val joined = when {
            device.isBlank() -> maker
            maker.isBlank() || device.startsWith(maker, ignoreCase = true) -> device
            else -> "$maker $device"
        }
        val safe = joined.map { if (it.isISOControl()) ' ' else it }.joinToString("")
            .replace(Regex("\\s+"), " ")
            .trim()
            .take(64)
        return safe.ifBlank { "Android 设备" }
    }

    fun pair(payload: PairingPayload, deviceName: String): Profile {
        val name = deviceName.map { if (it.isISOControl()) ' ' else it }.joinToString("")
            .replace(Regex("\\s+"), " ")
            .trim()
            .take(64)
            .ifBlank { "Android 设备" }
        val connection = (URI(payload.origin).resolve("/api/v1/pair").toURL().openConnection() as HttpURLConnection).apply {
            requestMethod = "POST"
            connectTimeout = 10000
            readTimeout = 15000
            instanceFollowRedirects = false
            doOutput = true
            setRequestProperty("Content-Type", "application/json; charset=utf-8")
            setRequestProperty("Accept", "application/json")
        }
        return try {
            val body = JSONObject().put("ticket", payload.ticket).put("deviceName", name).toString().toByteArray(Charsets.UTF_8)
            connection.outputStream.use { it.write(body) }
            if (connection.responseCode == 401) throw ApiException("二维码已失效或已使用，请在桌面端重新生成")
            if (connection.responseCode == 409) throw ApiException("设备绑定保存冲突，请重试")
            if (connection.responseCode != 201) {
                val message = runCatching { JSONObject(readError(connection)).getString("error") }
                    .getOrDefault("配对失败（HTTP ${connection.responseCode}）")
                throw ApiException(message)
            }
            val json = JSONObject(read(connection, 65536))
            val token = json.optString("token")
            require(Regex("^ndd1_[A-Za-z0-9_-]{43}$").matches(token)) { "桌面端返回的设备令牌无效" }
            require(json.optString("deviceName").isNotBlank()) { "桌面端返回的设备名无效" }
            Profile(payload.origin, token, DEFAULT_UPDATE_ORIGIN)
        } finally {
            connection.disconnect()
        }
    }
}

object UpdateClient {
    fun manifest(profile: Profile): JSONObject? {
        val connection = open(profile, "/api/v1/android-update")
        return connection.useApi {
            if (it.responseCode == 204) return@useApi null
            checkUpdate(it)
            JSONObject(read(it, 65536))
        }
    }

    fun download(profile: Profile, path: String, target: File, expectedSize: Long) {
        require(path.startsWith("/")) { "更新路径无效" }
        val connection = open(profile, path)
        connection.useApi {
            checkUpdate(it)
            target.outputStream().use { output ->
                var total = 0L
                val buffer = ByteArray(32768)
                while (true) {
                    val count = it.inputStream.read(buffer)
                    if (count < 0) break
                    total += count
                    if (total > expectedSize) throw IllegalStateException("APK 超出清单大小")
                    output.write(buffer, 0, count)
                }
            }
        }
    }

    private fun open(profile: Profile, path: String): HttpURLConnection =
        (URI(profile.updateOrigin).resolve(path).toURL().openConnection() as HttpURLConnection).apply {
            requestMethod = "GET"
            connectTimeout = 8000
            readTimeout = 30000
            instanceFollowRedirects = false
            setRequestProperty("Accept", "application/json, application/octet-stream")
            setRequestProperty("User-Agent", "NightDesk-Android/${BuildConfig.VERSION_NAME}")
        }

    private fun checkUpdate(connection: HttpURLConnection) {
        if (connection.responseCode !in 200..299) {
            val message = runCatching { JSONObject(readError(connection)).getString("error") }
                .getOrDefault("更新源暂不可用（HTTP ${connection.responseCode}）")
            throw ApiException(message)
        }
    }
}

private fun read(connection: HttpURLConnection, limit: Int): String = connection.inputStream.use { readBounded(it, limit) }

private fun readError(connection: HttpURLConnection): String =
    connection.errorStream?.use { readBounded(it, 65536) }.orEmpty()

private fun readBounded(input: java.io.InputStream, limit: Int): String {
    val output = ByteArrayOutputStream()
    val buffer = ByteArray(4096)
    while (true) {
        val count = input.read(buffer)
        if (count < 0) break
        if (output.size() + count > limit) throw IllegalStateException("响应内容过大")
        output.write(buffer, 0, count)
    }
    return String(output.toByteArray(), Charsets.UTF_8)
}

private inline fun <T> HttpURLConnection.useApi(block: (HttpURLConnection) -> T): T =
    try { block(this) } finally { disconnect() }

class ApiException(message: String) : Exception(message)
