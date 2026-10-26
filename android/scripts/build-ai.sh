#!/usr/bin/env bash
set -euo pipefail
umask 077
export JAVA_HOME=/home/tony/.local/opt/jdk-17
export ANDROID_HOME=/home/tony/Android/Sdk
export ANDROID_SDK_ROOT="$ANDROID_HOME"
export PATH="$JAVA_HOME/bin:/home/tony/.local/opt/gradle-8.11.1/bin:$ANDROID_HOME/platform-tools:$ANDROID_HOME/cmdline-tools/latest/bin:$ANDROID_HOME/build-tools/35.0.0:$PATH"
keydir="$HOME/.local/share/nightdesk-signing"
mkdir -p "$keydir"
chmod 700 "$keydir"
if [[ ! -f "$keydir/release.p12" ]]; then
    # Recover a partial setup only if the password still exists; never overwrite an existing signer.
    [[ -f "$keydir/password" ]] || openssl rand -base64 48 > "$keydir/password"
    keytool -genkeypair -noprompt -keystore "$keydir/release.p12" -storetype PKCS12 -storepass:file "$keydir/password" -alias nightdesk -keyalg RSA -keysize 3072 -validity 10000 -dname 'CN=NightDesk Release' > "$keydir/keygen.log" 2>&1
fi
chmod 600 "$keydir"/*
export NIGHTDESK_KEYSTORE="$keydir/release.p12"
export NIGHTDESK_STORE_PASSWORD="$(cat "$keydir/password")"
keytool -exportcert -keystore "$NIGHTDESK_KEYSTORE" -storepass:file "$keydir/password" -alias nightdesk -file "$keydir/certificate.der" >/dev/null 2>&1
sha256sum "$keydir/certificate.der" | cut -d' ' -f1 > "$keydir/certificate.sha256"
cd "${NIGHTDESK_ANDROID_DIR:-/home/tony/dev/nightdesk-android}"
printf 'BUILD_START %s\n' "$(date -Is)"
gradle --no-daemon testDebugUnitTest lintRelease assembleDebug assembleRelease "$@"
printf 'BUILD_OK\n'
