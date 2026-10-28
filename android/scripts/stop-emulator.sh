#!/usr/bin/env bash
# 停掉按需启动的软件模拟器，把它占的内存还给主机。
#
# 无 KVM 时 qemu 的 RSS 约为 guest 内存的 2~3 倍（-memory 4096 实测约 10 GiB），
# 空闲挂着没有任何收益：常规仪器测试已经移到 GitHub CI。
set -euo pipefail
export ANDROID_HOME=${ANDROID_HOME:-/home/tony/Android/Sdk}
export PATH="$ANDROID_HOME/platform-tools:$PATH"
serial="${1:-emulator-${ND_EMU_PORT:-5580}}"
avd="${ND_AVD:-nightdesk-api35}"

if adb -s "$serial" get-state >/dev/null 2>&1; then
    adb -s "$serial" emu kill >/dev/null 2>&1 || true
fi

for _ in $(seq 1 30); do
    if ! pgrep -f "qemu-system-x86_64-headless.*-avd $avd" >/dev/null 2>&1; then
        printf 'emulator stopped: %s\n' "$serial"
        free -h | sed -n '1,3p'
        exit 0
    fi
    sleep 1
done

printf 'emulator did not exit within 30s; check: pgrep -af qemu-system\n' >&2
exit 1
