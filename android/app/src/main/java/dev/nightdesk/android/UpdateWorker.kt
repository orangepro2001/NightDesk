package dev.nightdesk.android

import android.Manifest
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.work.Constraints
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.ExistingWorkPolicy
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.Worker
import androidx.work.WorkerParameters
import java.util.concurrent.TimeUnit

class UpdateWorker(context: Context, params: WorkerParameters) : Worker(context, params) {
    override fun doWork(): Result = try {
        val p = ProfileStore(applicationContext).loadHosts()?.active?.profile
        if (p != null) {
            val release = Updates(applicationContext).check(p)
            if (release != null) notify(release)
        }
        Result.success()
    } catch (_: Exception) { if (runAttemptCount < 2) Result.retry() else Result.failure() }
    private fun notify(r: Release) {
        val app = applicationContext
        if (Build.VERSION.SDK_INT >= 33 && app.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) return
        val manager = app.getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(NotificationChannel("updates", "NightDesk 更新", NotificationManager.IMPORTANCE_DEFAULT))
        val pending = PendingIntent.getActivity(app, 1, Intent(app, MainActivity::class.java), PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
        manager.notify(1001, NotificationCompat.Builder(app, "updates").setSmallIcon(android.R.drawable.stat_sys_download_done)
            .setContentTitle("NightDesk ${r.versionName} 已下载").setContentText("打开应用确认安装").setContentIntent(pending).setAutoCancel(true).build())
    }
    companion object {
        private val constraints = Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build()
        fun schedule(context: Context) {
            WorkManager.getInstance(context).enqueueUniquePeriodicWork("nightdesk-updates", ExistingPeriodicWorkPolicy.KEEP,
                PeriodicWorkRequestBuilder<UpdateWorker>(6, TimeUnit.HOURS).setConstraints(constraints).build())
        }
        fun now(context: Context) {
            WorkManager.getInstance(context).enqueueUniqueWork("nightdesk-update-now", ExistingWorkPolicy.KEEP,
                OneTimeWorkRequestBuilder<UpdateWorker>().setConstraints(constraints).build())
        }
        fun cancel(context: Context) {
            WorkManager.getInstance(context).cancelUniqueWork("nightdesk-updates")
            WorkManager.getInstance(context).cancelUniqueWork("nightdesk-update-now")
        }
    }
}
