#!/usr/bin/env bash
set -euo pipefail
umask 077
export JAVA_HOME=/home/tony/.local/opt/jdk-17
export ANDROID_HOME=/home/tony/Android/Sdk
export ANDROID_SDK_ROOT="$ANDROID_HOME"
export PATH="$JAVA_HOME/bin:/home/tony/.local/opt/gradle-8.11.1/bin:$ANDROID_HOME/platform-tools:$ANDROID_HOME/cmdline-tools/latest/bin:$ANDROID_HOME/build-tools/35.0.0:$PATH"
keydir="$HOME/.local/share/nightdesk-signing"
[[ -f "$keydir/release.p12" && -f "$keydir/password" ]] || { echo "signing key missing" >&2; exit 1; }
export NIGHTDESK_KEYSTORE="$keydir/release.p12"
export NIGHTDESK_STORE_PASSWORD="$(cat "$keydir/password")"
cd /home/tony/dev/nightdesk-android
printf 'ANDROIDTEST_START %s\n' "$(date -Is)"
gradle --no-daemon assembleReleaseAndroidTest "$@"
printf 'ANDROIDTEST_OK\n'
