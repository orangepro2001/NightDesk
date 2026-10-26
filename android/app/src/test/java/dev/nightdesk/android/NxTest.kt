package dev.nightdesk.android

import org.json.JSONArray
import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test
import java.nio.charset.StandardCharsets

class NxTest {
    private val fingerprint = "a".repeat(64)
    private val address = "100.100.1.2:7000"

    @Test
    fun parsesPinnedNxPairingAndNormalizesFingerprint() {
        val now = 1_000_000L
        val parsed = NxPairing.parse(pairingJson(now + 300).put("fingerprint", fingerprint.uppercase()).toString(), now)
        assertEquals(address, parsed.addr)
        assertEquals(fingerprint, parsed.fingerprint)
        assertEquals("123456", parsed.code)
        assertEquals("PC-TEST", parsed.name)
        assertEquals(1920, parsed.width)
        assertEquals(1080, parsed.height)
        assertEquals(fingerprint, parsed.host().id)
    }

    @Test
    fun rejectsInvalidOrExpiredNxPairing() {
        val now = 1_000_000L
        expectFailure { NxPairing.parse(pairingJson(now).toString(), now) }
        expectFailure { NxPairing.parse(pairingJson(now + 300).put("kind", "other").toString(), now) }
        expectFailure { NxPairing.parse(pairingJson(now + 300).put("v", 2).toString(), now) }
        expectFailure { NxPairing.parse(pairingJson(now + 300).put("fingerprint", "abc").toString(), now) }
        expectFailure { NxPairing.parse(pairingJson(now + 300).put("code", "12345").toString(), now) }
        expectFailure { NxPairing.parse(pairingJson(now + 300).put("width", 63).toString(), now) }
        expectFailure { NxPairing.parse(pairingJson(now + 300).put("height", 16385).toString(), now) }
        expectFailure { NxPairing.parse("not-json", now) }
    }

    @Test
    fun acceptsOnlyCanonicalTailscaleIpv4Addresses() {
        assertEquals("100.64.0.1:1", NxPairing.validateAddress(" 100.64.0.1:1 "))
        assertEquals("100.127.255.254:65535", NxPairing.validateAddress("100.127.255.254:65535"))
        listOf(
            "192.168.1.2:7000",
            "100.63.255.255:7000",
            "100.128.0.1:7000",
            "[fd7a:115c:a1e0::1]:7000",
            "100.64.0.1",
            "100.64.0.1:0",
            "100.64.0.1:65536"
        ).forEach { expectFailure { NxPairing.validateAddress(it) } }
    }

    @Test
    fun storesHostsWithoutPairingCodeAndDeduplicatesByFingerprint() {
        val older = host(lastConnected = 10)
        val newer = host(lastConnected = 20).copy(name = "Renamed", width = 2560, height = 1440, fingerprint = fingerprint.uppercase())
        var hosts = NxHostCodec.upsert(NxHosts(emptyList()), older)
        hosts = NxHostCodec.upsert(hosts, newer)

        assertEquals(1, hosts.items.size)
        assertEquals(fingerprint, hosts.first?.fingerprint)
        assertEquals("Renamed", hosts.first?.name)
        assertEquals(2560, hosts.first?.width)
        assertEquals(20L, hosts.first?.lastConnected)

        val encoded = NxHostCodec.encode(hosts).toString()
        assertTrue(!encoded.contains("123456"))
        assertEquals(hosts.items, NxHostCodec.decode(JSONObject(encoded)).items)
    }

    @Test
    fun enforcesHostLimitAndSavedHostValidation() {
        var hosts = NxHosts(emptyList())
        repeat(NxHostCodec.MaxHosts) { index ->
            hosts = NxHostCodec.upsert(hosts, host(index.toLong()).copy(fingerprint = "%064x".format(index)))
        }
        assertEquals(NxHostCodec.MaxHosts, hosts.items.size)
        expectFailure { NxHostCodec.upsert(hosts, host(99)) }
        expectFailure { NxHostCodec.upsert(NxHosts(emptyList()), host().copy(width = 63)) }
        expectFailure { NxHostCodec.upsert(NxHosts(emptyList()), host().copy(height = 16385)) }
        expectFailure { NxHostCodec.decode(JSONObject().put("schemaVersion", 2).put("hosts", JSONArray())) }
    }

    @Test
    fun fitsPersistentFrameAndMapsTouchToHostPixels() {
        val viewport = NxGeometry.fit(100, 100, 200, 100)!!
        assertEquals(25f, viewport.top)
        assertEquals(75f, viewport.bottom)
        assertEquals(NxPoint(100, 50), NxGeometry.mapTouch(100, 100, 200, 100, 50f, 50f))
        assertEquals(null, NxGeometry.mapTouch(100, 100, 200, 100, 50f, 5f))
        assertEquals(null, NxGeometry.fit(0, 100, 200, 100))
    }

    @Test
    fun chunksUtf8WithoutSplittingUnicodeCodePoints() {
        val text = "中😀a"
        val chunks = NxText.utf8Chunks(text, 4)
        assertEquals(listOf("中", "😀", "a"), chunks)
        assertTrue(chunks.all { it.toByteArray(StandardCharsets.UTF_8).size <= 4 })
        assertEquals(text, chunks.joinToString(""))
        assertTrue(NxText.utf8Chunks("", 4).isEmpty())
        expectFailure { NxText.utf8Chunks(text, 3) }
    }

    private fun host(lastConnected: Long = 20) = NxHost(address, fingerprint, "PC-TEST", 1920, 1080, lastConnected)

    private fun pairingJson(expires: Long) = JSONObject()
        .put("v", 1)
        .put("kind", "nightdesk-nx")
        .put("addr", address)
        .put("fingerprint", fingerprint)
        .put("code", "123456")
        .put("expires", expires)
        .put("name", "PC-TEST")
        .put("width", 1920)
        .put("height", 1080)

    private fun expectFailure(action: () -> Unit) {
        try {
            action()
            fail("expected failure")
        } catch (_: Exception) {
        }
    }
}
