package dev.nightdesk.android

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test

class CoreTest {
    private val ticket = "ndp1_" + "a".repeat(43)
    private val origin = "https://minipc-xoh4t.tail9f7f1f.ts.net"

    @Test
    fun allowsOnlyTailscaleHttps() {
        assertEquals(origin, Endpoints.origin("$origin/"))
        listOf(
            "http://minipc.tail9f7f1f.ts.net",
            "https://example.com",
            "https://a.tail9f7f1f.ts.net:443",
            "https://a.tail9f7f1f.ts.net/path"
        ).forEach {
            expectFailure { Endpoints.origin(it) }
        }
    }

    @Test
    fun parsesPairingQrAndExpiry() {
        val now = 1_000_000L
        val parsed = PairingPayload.parse(pairingJson(now + 300).toString(), now)
        assertEquals(origin, parsed.origin)
        assertEquals(ticket, parsed.ticket)
        assertEquals(now + 300, parsed.expiresAt)
        expectFailure { PairingPayload.parse(pairingJson(now).toString(), now) }
        expectFailure { PairingPayload.parse(pairingJson(now + 300).put("kind", "other").toString(), now) }
        expectFailure { PairingPayload.parse(pairingJson(now + 300).put("v", 2).toString(), now) }
        expectFailure { PairingPayload.parse(pairingJson(now + 300).put("ticket", "ndp1_bad").toString(), now) }
        expectFailure { PairingPayload.parse(pairingJson(now + 300).put("origin", "https://example.com").toString(), now) }
        expectFailure { PairingPayload.parse("not-json", now) }
    }

    @Test
    fun migratesLegacyProfileToIndependentUpdateOrigin() {
        val legacy = JSONObject().put("origin", origin).put("token", "a".repeat(43))
        val migrated = ProfileCodec.decode(legacy)
        assertEquals(origin, migrated.controlOrigin)
        assertEquals("a".repeat(43), migrated.controlToken)
        assertEquals(DEFAULT_UPDATE_ORIGIN, migrated.updateOrigin)

        val independent = Profile(origin, "ndd1_" + "b".repeat(43), "https://tony-ai-pc-1.tail9f7f1f.ts.net")
        assertEquals(independent, ProfileCodec.decode(ProfileCodec.encode(independent)))
        expectFailure { ProfileCodec.decode(JSONObject().put("origin", origin).put("token", "short")) }
    }

    @Test
    fun sanitizesAndroidDeviceName() {
        assertEquals("Google Pixel 9", PairingClient.defaultDeviceName("Google", "Pixel 9"))
        assertEquals("Pixel 9", PairingClient.defaultDeviceName("Pixel", "Pixel 9"))
        assertEquals("A B", PairingClient.defaultDeviceName("A\u0000", " B "))
        assertEquals(64, PairingClient.defaultDeviceName("M".repeat(100), "X".repeat(100)).length)
    }

    @Test
    fun validatesUpdateManifestAndBothApkPaths() {
        val latest = UpdateFlowInstrumentedTestSupport.manifest(Release.LatestApkPath)
        assertEquals(2L, Release.parse(latest).versionCode)
        assertTrue(Release.parse(UpdateFlowInstrumentedTestSupport.manifest("/api/v1/android-apk?versionCode=2")).apkPath.endsWith("versionCode=2"))
        expectFailure { Release.parse(UpdateFlowInstrumentedTestSupport.manifest("/evil")) }
        expectFailure { Release.parse(UpdateFlowInstrumentedTestSupport.manifest("/api/v1/android-apk?versionCode=3")) }
    }

    @Test
    fun migratesSingleProfileToHosts() {
        val v2 = ProfileCodec.encode(Profile(origin, "a".repeat(43)))
        val hosts = HostsCodec.decode(v2)
        assertEquals(1, hosts.items.size)
        assertEquals(origin, hosts.active?.id)
        assertEquals("minipc-xoh4t", hosts.active?.label)
        assertEquals(origin, hosts.active?.profile?.controlOrigin)
    }

    @Test
    fun upsertsHostsByOriginAndKeepsLatestToken() {
        val otherOrigin = "https://other-host.tail9f7f1f.ts.net"
        var hosts = HostsCodec.upsert(Hosts(emptyList(), null), Profile(origin, "a".repeat(43)))
        assertEquals(1, hosts.items.size)
        assertEquals(origin, hosts.activeId)

        hosts = HostsCodec.upsert(hosts, Profile(otherOrigin, "b".repeat(43)))
        assertEquals(2, hosts.items.size)
        assertEquals(otherOrigin, hosts.activeId)

        hosts = HostsCodec.upsert(hosts, Profile(origin, "c".repeat(43)))
        assertEquals(2, hosts.items.size)
        assertEquals(origin, hosts.activeId)
        assertEquals("c".repeat(43), hosts.active?.profile?.controlToken)

        val decoded = HostsCodec.decode(HostsCodec.encode(hosts))
        assertEquals(hosts.items, decoded.items)
        assertEquals(hosts.activeId, decoded.activeId)
    }

    @Test
    fun removesHostsAndFallsBackToFirst() {
        var hosts = HostsCodec.upsert(Hosts(emptyList(), null), Profile(origin, "a".repeat(43)))
        val other = Profile("https://other-host.tail9f7f1f.ts.net", "b".repeat(43))
        hosts = HostsCodec.upsert(hosts, other)

        val removed = HostsCodec.remove(hosts, other.controlOrigin)
        assertEquals(1, removed.items.size)
        assertEquals(origin, removed.activeId)
        assertEquals(origin, removed.active?.id)

        val empty = HostsCodec.remove(removed, origin)
        assertTrue(empty.items.isEmpty())
        assertEquals(null, empty.activeId)
        assertEquals(null, empty.active)
    }

    @Test
    fun enforcesHostLimitAndRejectsInvalidTokens() {
        var hosts = Hosts(emptyList(), null)
        repeat(HostsCodec.MaxHosts) { index ->
            hosts = HostsCodec.upsert(hosts, Profile("https://host-$index.tail9f7f1f.ts.net", "a".repeat(43)))
        }
        assertEquals(HostsCodec.MaxHosts, hosts.items.size)
        expectFailure { HostsCodec.upsert(hosts, Profile("https://overflow.tail9f7f1f.ts.net", "a".repeat(43))) }
        expectFailure { HostsCodec.upsert(hosts, Profile("https://host-0.tail9f7f1f.ts.net", "short")) }
        expectFailure { HostsCodec.decode(JSONObject().put("schemaVersion", 4).put("hosts", org.json.JSONArray())) }
    }
    private fun pairingJson(expires: Long) = JSONObject()
        .put("v", 1)
        .put("kind", "nightdesk-pair")
        .put("origin", origin)
        .put("ticket", ticket)
        .put("expires", expires)

    private fun expectFailure(action: () -> Unit) {
        try {
            action()
            fail("expected failure")
        } catch (_: Exception) {
        }
    }
}

private object UpdateFlowInstrumentedTestSupport {
    fun manifest(apkPath: String) = JSONObject()
        .put("schemaVersion", 1)
        .put("packageName", Release.PackageName)
        .put("versionCode", 2L)
        .put("versionName", "1.0.1")
        .put("minSdk", 29)
        .put("size", 12L)
        .put("sha256", "a".repeat(64))
        .put("certificateSha256", "b".repeat(64))
        .put("apkPath", apkPath)
        .put("notes", "")
}