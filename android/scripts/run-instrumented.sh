#!/usr/bin/env bash
set -uo pipefail
export JAVA_HOME=/home/tony/.local/opt/jdk-17
export ANDROID_HOME=/home/tony/Android/Sdk
export PATH="$JAVA_HOME/bin:$ANDROID_HOME/platform-tools:$PATH"
LOG=/home/tony/dev/nightdesk-logs/instrumented.log
mkdir -p "$(dirname "$LOG")"
rm -f /home/tony/dev/nightdesk-logs/instrumented.exit
android_dir="${NIGHTDESK_ANDROID_DIR:-/home/tony/dev/nightdesk-android}"
serial="${NIGHTDESK_ANDROID_SERIAL:-emulator-5580}"
update_origin="${NIGHTDESK_UPDATE_ORIGIN:-https://tony-ai-pc-1.tail9f7f1f.ts.net}"
status=0
{
set -x
adb -s "$serial" install -r "$android_dir/app/build/outputs/apk/release/app-release.apk" || status=1
adb -s "$serial" install -r "$android_dir/app/build/outputs/apk/androidTest/release/app-release-androidTest.apk" || status=1
# HostStoreInstrumentedTest covers multi-host persistence and needs no arguments.
adb -s "$serial" shell am instrument -w -e class dev.nightdesk.android.HostStoreInstrumentedTest dev.nightdesk.android.test/androidx.test.runner.AndroidJUnitRunner || status=1
# UpdateFlowInstrumentedTest needs the tailnet update origin explicitly; without
# it the test fails with "missing updateOrigin" instead of testing the feed.
adb -s "$serial" shell am instrument -w -e updateOrigin "$update_origin" -e class dev.nightdesk.android.UpdateFlowInstrumentedTest dev.nightdesk.android.test/androidx.test.runner.AndroidJUnitRunner || status=1
set +x
} > "$LOG" 2>&1
echo "$status" > /home/tony/dev/nightdesk-logs/instrumented.exit
exit "$status"