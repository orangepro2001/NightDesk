#!/usr/bin/env bash
rm -f /home/tony/dev/nightdesk-logs/androidtest.exit
bash /home/tony/dev/nightdesk-android/scripts/build-androidtest.sh -q > /home/tony/dev/nightdesk-logs/androidtest.log 2>&1
echo $? > /home/tony/dev/nightdesk-logs/androidtest.exit