#!/usr/bin/env bash
set -eu
export ANDROID_HOME=/home/tony/Android/Sdk
export PATH="$ANDROID_HOME/platform-tools:$ANDROID_HOME/emulator:$PATH"
# Isolated AVD, no KVM. main tmux and unrelated sessions/devices are untouched.
exec emulator -avd nightdesk-api35 -port 5580 -no-window -no-audio -no-boot-anim -no-snapshot -gpu swiftshader_indirect -accel off -cores 6 -memory 4096 -dns-server 100.100.100.100
