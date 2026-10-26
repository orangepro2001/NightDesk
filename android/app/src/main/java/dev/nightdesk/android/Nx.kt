package dev.nightdesk.android

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject
import java.net.URI
import java.nio.charset.StandardCharsets

/** Payload rendered by the Windows NX pairing window. It is intentionally separate from the HTTPS pair payload. */
data class NxPairing(
    val addr: String,
    val fingerprint: String,
    val code: String,
    val expiresAt: Long,
    val name: String,
    val width: Int,
    val height: Int
) {
    fun host(now: Long = System.currentTimeMillis()) = NxHost(
        addr = addr,
        fingerprint = fingerprint,
        name = name,
        width = width,
        height = height,
        lastConnected = now
    )

    companion object {
        private val fingerprintPattern = Regex("^[0-9a-fA-F]{64}$")
        private val codePattern = Regex("^[0-9]{6}$")

        fun parse(raw: String, nowSeconds: Long = System.currentTimeMillis() / 1000): NxPairing {
            require(raw.length in 1..8192) { "二维码内容无效" }
            val json = try { JSONObject(raw) } catch (_: Exception) {
                throw IllegalArgumentException("二维码不是 NightDesk 屏幕配对信息")
            }
            require(json.optInt("v", 0) == 1 && json.optString("kind") == "nightdesk-nx") {
                "二维码不是 NightDesk 屏幕配对信息"
            }
            val addr = validateAddress(json.optString("addr"))
            val fingerprint = json.optString("fingerprint").lowercase()
            require(fingerprintPattern.matches(fingerprint)) { "主机证书指纹无效" }
            val code = json.optString("code")
            require(codePattern.matches(code)) { "配对码必须是 6 位数字" }
            val expires = json.optLong("expires", 0)
            require(expires > nowSeconds) { "屏幕配对二维码已过期，请在桌面端重新生成" }
            val name = cleanName(json.optString("name"))
            val width = json.optInt("width", 0)
            val height = json.optInt("height", 0)
            require(width in 64..16384 && height in 64..16384) { "主机屏幕尺寸无效" }
            return NxPairing(addr, fingerprint, code, expires, name, width, height)
        }

        fun validateAddress(text: String): String {
            val value = text.trim()
            require(value.length in 7..21) { "主机地址无效" }
            val pieces = value.split(':')
            require(pieces.size == 2) { "主机地址必须为 Tailscale IPv4:端口" }
            val octets = pieces[0].split('.')
            require(octets.size == 4 && octets.all { it.isNotEmpty() && it.length <= 3 && it.all(Char::isDigit) }) {
                "主机地址必须为 Tailscale IPv4:端口"
            }
            val numbers = octets.map { it.toInt() }
            require(numbers.all { it in 0..255 }) { "主机 IPv4 地址无效" }
            require(numbers[0] == 100 && numbers[1] in 64..127) { "只允许 Tailscale 100.64.0.0/10 数据面地址" }
            val port = pieces[1].toIntOrNull() ?: throw IllegalArgumentException("主机端口无效")
            require(port in 1..65535) { "主机端口无效" }
            return "${numbers.joinToString(".")}:$port"
        }

        fun cleanName(text: String): String = text.map { if (it.isISOControl()) ' ' else it }
            .joinToString("")
            .replace(Regex("\\s+"), " ")
            .trim()
            .take(64)
            .ifBlank { "Windows 主机" }
    }
}

data class NxHost(
    val addr: String,
    val fingerprint: String,
    val name: String,
    val width: Int,
    val height: Int,
    val lastConnected: Long
) {
    val id: String get() = fingerprint
}

data class NxHosts(val items: List<NxHost>) {
    val first: NxHost? get() = items.firstOrNull()
}

object NxHostCodec {
    const val MaxHosts = 20

    fun decode(json: JSONObject): NxHosts {
        require(json.optInt("schemaVersion", 0) == 1) { "保存的屏幕主机数据版本不受支持" }
        val array = json.optJSONArray("hosts") ?: throw IllegalArgumentException("保存的屏幕主机列表无效")
        val items = (0 until array.length()).map { index ->
            val item = array.getJSONObject(index)
            val fingerprint = item.optString("fingerprint").lowercase()
            require(Regex("^[0-9a-f]{64}$").matches(fingerprint)) { "保存的主机指纹无效" }
            NxHost(
                addr = NxPairing.validateAddress(item.optString("addr")),
                fingerprint = fingerprint,
                name = NxPairing.cleanName(item.optString("name")),
                width = item.optInt("width", 0).also { require(it in 64..16384) },
                height = item.optInt("height", 0).also { require(it in 64..16384) },
                lastConnected = item.optLong("lastConnected", 0)
            )
        }
        require(items.size <= MaxHosts && items.map(NxHost::id).toSet().size == items.size) {
            "保存的屏幕主机列表无效"
        }
        return NxHosts(items.sortedByDescending(NxHost::lastConnected))
    }

    fun encode(hosts: NxHosts): JSONObject = JSONObject().apply {
        put("schemaVersion", 1)
        put("hosts", JSONArray().also { array ->
            hosts.items.forEach { host ->
                array.put(JSONObject().apply {
                    put("addr", host.addr)
                    put("fingerprint", host.fingerprint)
                    put("name", host.name)
                    put("width", host.width)
                    put("height", host.height)
                    put("lastConnected", host.lastConnected)
                })
            }
        })
    }

    /** Stores only public trust/address metadata. The short-lived pairing code is never included. */
    fun upsert(hosts: NxHosts, host: NxHost): NxHosts {
        NxPairing.validateAddress(host.addr)
        val fingerprint = host.fingerprint.lowercase()
        require(Regex("^[0-9a-f]{64}$").matches(fingerprint)) { "主机证书指纹无效" }
        require(host.width in 64..16384 && host.height in 64..16384) { "主机屏幕尺寸无效" }
        val existing = hosts.items.firstOrNull { it.id == fingerprint }
        require(existing != null || hosts.items.size < MaxHosts) {
            "最多保存 $MaxHosts 台屏幕主机，请先删除不用的主机"
        }
        val item = host.copy(fingerprint = fingerprint, name = NxPairing.cleanName(host.name))
        return NxHosts((hosts.items.filterNot { it.id == item.id } + item).sortedByDescending(NxHost::lastConnected))
    }

    fun remove(hosts: NxHosts, id: String): NxHosts = NxHosts(hosts.items.filterNot { it.id == id })
}

class NxProfileStore(context: Context) {
    private val store = context.applicationContext.getSharedPreferences("nx_profiles", Context.MODE_PRIVATE)

    fun load(): NxHosts {
        val raw = store.getString("data", null) ?: return NxHosts(emptyList())
        return NxHostCodec.decode(JSONObject(raw))
    }

    fun upsert(host: NxHost): NxHosts {
        val updated = NxHostCodec.upsert(runCatching { load() }.getOrDefault(NxHosts(emptyList())), host)
        save(updated)
        return updated
    }

    fun markConnected(id: String, width: Int, height: Int, now: Long = System.currentTimeMillis()): NxHosts {
        val hosts = load()
        val found = hosts.items.firstOrNull { it.id == id } ?: return hosts
        val updated = NxHosts(hosts.items.map {
            if (it.id == id) it.copy(width = width, height = height, lastConnected = now) else it
        }.sortedByDescending(NxHost::lastConnected))
        save(updated)
        return updated
    }

    fun remove(id: String): NxHosts {
        val updated = NxHostCodec.remove(load(), id)
        if (updated.items.isEmpty()) clear() else save(updated)
        return updated
    }

    fun clear() {
        store.edit().clear().apply()
    }

    private fun save(hosts: NxHosts) {
        store.edit().putString("data", NxHostCodec.encode(hosts).toString()).apply()
    }
}

data class NxPoint(val x: Int, val y: Int)
data class NxViewport(val left: Float, val top: Float, val right: Float, val bottom: Float) {
    val width: Float get() = right - left
    val height: Float get() = bottom - top
}

object NxGeometry {
    fun fit(viewWidth: Int, viewHeight: Int, hostWidth: Int, hostHeight: Int): NxViewport? {
        if (viewWidth <= 0 || viewHeight <= 0 || hostWidth <= 0 || hostHeight <= 0) return null
        val scale = minOf(viewWidth.toFloat() / hostWidth, viewHeight.toFloat() / hostHeight)
        val width = hostWidth * scale
        val height = hostHeight * scale
        val left = (viewWidth - width) / 2f
        val top = (viewHeight - height) / 2f
        return NxViewport(left, top, left + width, top + height)
    }

    fun mapTouch(viewWidth: Int, viewHeight: Int, hostWidth: Int, hostHeight: Int, x: Float, y: Float): NxPoint? {
        val viewport = fit(viewWidth, viewHeight, hostWidth, hostHeight) ?: return null
        if (x < viewport.left || x >= viewport.right || y < viewport.top || y >= viewport.bottom) return null
        val hostX = ((x - viewport.left) * hostWidth / viewport.width).toInt().coerceIn(0, hostWidth - 1)
        val hostY = ((y - viewport.top) * hostHeight / viewport.height).toInt().coerceIn(0, hostHeight - 1)
        return NxPoint(hostX, hostY)
    }
}

object NxText {
    /** Splits on Unicode code points so a surrogate pair is never cut at the protocol limit. */
    fun utf8Chunks(text: String, maxBytes: Int = 1024): List<String> {
        require(maxBytes >= 4) { "UTF-8 分块上限过小" }
        if (text.isEmpty()) return emptyList()
        val result = ArrayList<String>()
        val current = StringBuilder()
        var currentBytes = 0
        var index = 0
        while (index < text.length) {
            val codePoint = text.codePointAt(index)
            val piece = String(Character.toChars(codePoint))
            val bytes = piece.toByteArray(StandardCharsets.UTF_8).size
            if (currentBytes + bytes > maxBytes) {
                if (current.isNotEmpty()) result += current.toString()
                current.setLength(0)
                currentBytes = 0
            }
            current.append(piece)
            currentBytes += bytes
            index += Character.charCount(codePoint)
        }
        if (current.isNotEmpty()) result += current.toString()
        return result
    }
}
