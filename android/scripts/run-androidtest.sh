#!/usr/bin/env bash
android_dir="${NIGHTDESK_ANDROID_DIR:-/home/tony/dev/NightDesk/android}"
rm -f /home/tony/dev/nightdesk-logs/androidtest.exit
bash "$android_dir/scripts/build-androidtest.sh" -q > /home/tony/dev/nightdesk-logs/androidtest.log 2>&1
echo $? > /home/tony/dev/nightdesk-logs/androidtest.exit
