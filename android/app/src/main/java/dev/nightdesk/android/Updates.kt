package dev.nightdesk.android

import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import org.json.JSONObject
import java.io.File
import java.security.MessageDigest

data class Release(
    val versionCode: Long,
    val versionName: String,
    val minSdk: Int,
    val size: Long,
    val sha256: String,
    val certificateSha256: String,
    val apkPath: String,
    val notes: String
) {
    companion object {
        const val PackageName = "dev.nightdesk.android"
        const val LatestApkPath = "/nightdesk/latest.apk"

        fun parse(json: JSONObject): Release {
            if (json.getInt("schemaVersion") != 1 || json.getString("packageName") != PackageName) throw IllegalArgumentException("更新清单不匹配")
            val release = Release(
                json.getLong("versionCode"), json.getString("versionName"), json.getInt("minSdk"), json.getLong("size"),
                json.getString("sha256").lowercase(), json.getString("certificateSha256").lowercase(),
                json.getString("apkPath"), json.optString("notes")
            )
            if (release.versionCode <= 0 || release.versionName.isBlank() || release.minSdk !in 29..35 ||
                release.size !in 1..104857600 || !release.sha256.matches(Regex("[0-9a-f]{64}")) ||
                !release.certificateSha256.matches(Regex("[0-9a-f]{64}")) ||
                !validApkPath(release.apkPath, release.versionCode) || release.notes.length > 4096
            ) throw IllegalArgumentException("更新清单无效")
            return release
        }

        private fun validApkPath(path: String, versionCode: Long): Boolean =
            path == LatestApkPath || path == "/api/v1/android-apk?versionCode=$versionCode"
    }
}

class Updates(context: Context) {
    private val app = context.applicationContext
    private val dir = File(app.filesDir, "updates").apply { mkdirs() }
    private val manifest = File(dir, "pending.json")
    private val apk = File(dir, "update.apk")

    @Synchronized
    fun check(profile: Profile): Release? {
        val release = UpdateClient.manifest(profile)?.let(Release::parse) ?: run { clear(); return null }
        val installed = app.packageManager.getPackageInfo(app.packageName, PackageManager.GET_SIGNING_CERTIFICATES)
        val installedCert = installed.signingInfo?.apkContentsSigners?.firstOrNull()?.toByteArray()?.let(::sha256Bytes)
            ?: throw IllegalStateException("无法读取本应用签名")
        if (release.versionCode <= installed.longVersionCode) { clear(); return null }
        if (release.minSdk > Build.VERSION.SDK_INT) throw IllegalStateException("系统版本不满足更新要求")
        if (release.certificateSha256 != installedCert) throw SecurityException("发布签名与本应用不一致")
        val current = ready()
        if (current != null && current.versionCode == release.versionCode && current.sha256 == release.sha256) return current

        clear()
        val temp = File(dir, "download.tmp")
        try {
            UpdateClient.download(profile, release.apkPath, temp, release.size)
            require(temp.length() == release.size && sha256(temp) == release.sha256) { "APK 与清单不匹配" }
            verifyApk(temp, release)
            if (!temp.renameTo(apk)) {
                temp.copyTo(apk, true)
                temp.delete()
            }
            manifest.writeText(JSONObject().put("schemaVersion", 1)
                .put("packageName", Release.PackageName)
                .put("versionCode", release.versionCode)
                .put("versionName", release.versionName)
                .put("minSdk", release.minSdk)
                .put("size", release.size)
                .put("sha256", release.sha256)
                .put("certificateSha256", release.certificateSha256)
                .put("apkPath", release.apkPath)
                .put("notes", release.notes)
                .toString())
            return release
        } finally {
            temp.delete()
        }
    }

    @Synchronized
    fun ready(): Release? {
        if (!manifest.isFile || !apk.isFile) return null
        return try {
            val release = Release.parse(JSONObject(manifest.readText()))
            if (apk.length() == release.size && sha256(apk) == release.sha256 && verifyApk(apk, release)) release
            else { clear(); null }
        } catch (_: Exception) {
            clear()
            null
        }
    }

    fun clear() {
        manifest.delete()
        apk.delete()
    }

    fun file(): File = apk

    private fun verifyApk(file: File, release: Release): Boolean {
        val info = app.packageManager.getPackageArchiveInfo(file.absolutePath, PackageManager.GET_SIGNING_CERTIFICATES)
            ?: throw IllegalStateException("APK 无法解析")
        if (info.packageName != Release.PackageName || info.longVersionCode != release.versionCode || info.versionName != release.versionName)
            throw IllegalStateException("APK 包名或版本不匹配")
        val cert = info.signingInfo?.apkContentsSigners?.firstOrNull()?.toByteArray()?.let(::sha256Bytes)
            ?: throw IllegalStateException("APK 未签名")
        if (cert != release.certificateSha256) throw SecurityException("APK 签名不匹配")
        val installedCert = app.packageManager.getPackageInfo(Release.PackageName, PackageManager.GET_SIGNING_CERTIFICATES)
            .signingInfo?.apkContentsSigners?.firstOrNull()?.toByteArray()?.let(::sha256Bytes)
            ?: throw IllegalStateException("无法读取已安装应用签名")
        if (cert != installedCert) throw SecurityException("更新签名与已安装应用不同")
        return true
    }

    private fun sha256(file: File): String = MessageDigest.getInstance("SHA-256").let { digest ->
        file.inputStream().use { input ->
            val buffer = ByteArray(32768)
            while (true) {
                val count = input.read(buffer)
                if (count < 0) break
                digest.update(buffer, 0, count)
            }
        }
        digest.digest().joinToString("") { "%02x".format(it) }
    }

    private fun sha256Bytes(bytes: ByteArray): String =
        MessageDigest.getInstance("SHA-256").digest(bytes).joinToString("") { "%02x".format(it) }
}