package dev.nightdesk.android

import android.os.Bundle
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File
import java.security.MessageDigest

// 独立更新源端到端验证：Tailscale HTTPS -> 清单 -> APK 下载 -> 签名校验。
// 控制地址/令牌故意不可用，以证明更新流程不依赖桌面控制 API。
@RunWith(AndroidJUnit4::class)
class UpdateFlowInstrumentedTest {
    @Test
    fun testChecksAndDownloadsFromIndependentUpdateOrigin() {
        val args: Bundle = InstrumentationRegistry.getArguments()
        val updateOrigin = args.getString("updateOrigin") ?: throw AssertionError("missing updateOrigin")
        val profile = Profile(
            controlOrigin = "https://nightdesk-unused.tail9f7f1f.ts.net",
            controlToken = "unused-control-token-000000000000000000000",
            updateOrigin = Endpoints.origin(updateOrigin)
        )
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val updates = Updates(context)
        // Manifest reachability must not depend on the installed build: after a release the
        // installed app already equals the published version, and "no update" is the correct result.
        val manifest = UpdateClient.manifest(profile)?.let(Release::parse)
            ?: throw AssertionError("update manifest unreachable")
        assertTrue("unexpected versionCode", manifest.versionCode >= 3L)
        val installed = context.packageManager.getPackageInfo(context.packageName, 0).longVersionCode
        if (manifest.versionCode <= installed) {
            assertNull("update offered for current build", updates.check(profile))
            assertNull("stale update kept for current build", updates.ready())
            return
        }
        val release = updates.check(profile) ?: throw AssertionError("no update available")
        assertTrue("unexpected versionCode", release.versionCode >= 3L)
        assertEquals("unexpected APK path", Release.LatestApkPath, release.apkPath)
        val apk = updates.file()
        assertTrue("apk not saved", apk.isFile)
        assertEquals("apk size mismatch", release.size, apk.length())
        assertEquals("apk sha256 mismatch", release.sha256, sha256Hex(apk))
        assertEquals("ready restore mismatch", release.versionCode, updates.ready()?.versionCode)
    }

    private fun sha256Hex(file: File): String =
        MessageDigest.getInstance("SHA-256").digest(file.readBytes()).joinToString("") { "%02x".format(it) }
}