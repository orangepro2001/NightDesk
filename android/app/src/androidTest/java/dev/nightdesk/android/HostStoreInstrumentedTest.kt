package dev.nightdesk.android

import android.content.Context
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

// 多主机存储的真机/模拟器验证：AES-GCM 加密落盘、切换 active、删除回落、令牌不落明文。
@RunWith(AndroidJUnit4::class)
class HostStoreInstrumentedTest {
    private val context = InstrumentationRegistry.getInstrumentation().targetContext
    private lateinit var store: ProfileStore

    @Before
    fun setUp() {
        store = ProfileStore(context)
        store.clear()
    }

    @After
    fun tearDown() {
        store.clear()
    }

    @Test
    fun persistsMultipleHostsAndSwitchesActive() {
        val alpha = Profile("https://alpha-host.tail9f7f1f.ts.net", "a".repeat(43))
        val beta = Profile("https://beta-host.tail9f7f1f.ts.net", "b".repeat(43), "https://tony-ai-pc-1.tail9f7f1f.ts.net")
        store.upsert(alpha)
        store.upsert(beta)

        var hosts = store.loadHosts() ?: throw AssertionError("hosts not persisted")
        assertEquals(setOf(alpha.controlOrigin, beta.controlOrigin), hosts.items.map { it.id }.toSet())
        assertEquals(beta.controlOrigin, hosts.activeId)

        store.setActive(alpha.controlOrigin)
        assertEquals(alpha.controlOrigin, store.loadHosts()?.activeId)

        hosts = store.remove(alpha.controlOrigin)
        assertEquals(listOf(beta.controlOrigin), hosts.items.map { it.id })
        val reloaded = store.loadHosts() ?: throw AssertionError("hosts lost after removal")
        assertEquals(listOf(beta.controlOrigin), reloaded.items.map { it.id })
        assertEquals(beta, reloaded.active?.profile)
    }

    @Test
    fun upsertReplacesTokenForSameHost() {
        val alpha = Profile("https://alpha-host.tail9f7f1f.ts.net", "a".repeat(43))
        val other = Profile("https://other-host.tail9f7f1f.ts.net", "b".repeat(43))
        store.upsert(alpha)
        store.upsert(other)
        val fresh = "c".repeat(43)
        store.upsert(alpha.copy(controlToken = fresh))

        val hosts = store.loadHosts() ?: throw AssertionError("hosts not persisted")
        assertEquals(2, hosts.items.size)
        assertEquals(alpha.controlOrigin, hosts.activeId)
        assertEquals(fresh, hosts.active?.profile?.controlToken)
    }

    @Test
    fun neverStoresPlaintextToken() {
        val token = "z".repeat(43)
        store.upsert(Profile("https://gamma-host.tail9f7f1f.ts.net", token))
        val prefs = context.getSharedPreferences("profile", Context.MODE_PRIVATE).all.values.joinToString(" ") { it.toString() }
        assertTrue(prefs.isNotEmpty())
        assertFalse("token stored in cleartext", prefs.contains(token))
    }
}