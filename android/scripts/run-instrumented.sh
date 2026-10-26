#!/usr/bin/env bash
export JAVA_HOME=/home/tony/.local/opt/jdk-17
export ANDROID_HOME=/home/tony/Android/Sdk
export PATH="$JAVA_HOME/bin:$ANDROID_HOME/platform-tools:$PATH"
LOG=/home/tony/dev/nightdesk-logs/instrumented.log
rm -f /home/tony/dev/nightdesk-logs/instrumented.exit
{
set -x
adb -s emulator-5580 install -r /home/tony/dev/nightdesk-android/app/build/outputs/apk/release/app-release.apk
adb -s emulator-5580 install -r /home/tony/dev/nightdesk-android/app/build/outputs/apk/androidTest/release/app-release-androidTest.apk
adb -s emulator-5580 shell am instrument -w -e class dev.nightdesk.android.HostStoreInstrumentedTest dev.nightdesk.android.test/androidx.test.runner.AndroidJUnitRunner
set +x
} > "$LOG" 2>&1
echo $? > /home/tony/dev/nightdesk-logs/instrumented.exit