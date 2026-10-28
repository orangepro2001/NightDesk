#!/usr/bin/env bash
rm -f /home/tony/dev/nightdesk-logs/build-1.0.4.exit
bash /home/tony/dev/NightDesk/android/scripts/build-ai.sh -q -PnightdeskVersionCode=5 -PnightdeskVersionName=1.0.4 > /home/tony/dev/nightdesk-logs/build-1.0.4.log 2>&1
echo $? > /home/tony/dev/nightdesk-logs/build-1.0.4.exit