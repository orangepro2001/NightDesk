#!/usr/bin/env bash
set -euo pipefail
export JAVA_HOME=/home/tony/.local/opt/jdk-17
export ANDROID_HOME=/home/tony/Android/Sdk
export ANDROID_SDK_ROOT="$ANDROID_HOME"
export PATH="$JAVA_HOME/bin:$ANDROID_HOME/cmdline-tools/latest/bin:$ANDROID_HOME/platform-tools:$ANDROID_HOME/emulator:$PATH"
printf 'NightDesk emulator setup starting %s\n' "$(date -Is)"
sdkmanager 'emulator' 'system-images;android-35;google_apis;x86_64'
printf 'no\n' | avdmanager create avd --name nightdesk-api35 --package 'system-images;android-35;google_apis;x86_64' --device pixel_5 
printf 'SDK_SETUP_OK\n'