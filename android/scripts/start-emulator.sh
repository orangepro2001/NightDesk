#!/usr/bin/env bash
set -eu
export ANDROID_HOME=/home/tony/Android/Sdk
export PATH="$ANDROID_HOME/platform-tools:$ANDROID_HOME/emulator:$PATH"
# Isolated AVD, no KVM. main tmux and unrelated sessions/devices are untouched.
#
# 这台机器没有 KVM，qemu 只能用 TCG 纯软件模拟，常驻 RSS 约为 guest 内存的 2~3 倍：
# -memory 4096 时实测约 10 GiB，会把 8 GiB swap 吃满。所以默认降到 3072 MB / 4 核，
# 并且只在需要时启动——三个常规仪器测试现在跑在 GitHub CI 的 KVM 模拟器上，
# 这台机器只用于 UpdateFlowInstrumentedTest（需要 tailnet）或交互调试。
# 用完执行 scripts/stop-emulator.sh 释放内存。
exec emulator -avd nightdesk-api35 \
    -port "${ND_EMU_PORT:-5580}" \
    -no-window -no-audio -no-boot-anim -no-snapshot \
    -gpu swiftshader_indirect -accel off \
    -cores "${ND_EMU_CORES:-4}" \
    -memory "${ND_EMU_MEMORY:-3072}" \
    -dns-server 100.100.100.100
