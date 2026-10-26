#!/usr/bin/env bash
# Prepares the software AVD for NightDesk HTTPS tests. Test-only workaround:
# the emulator's slirp DNS proxy cannot resolve the desktop MagicDNS name here,
# so the guest gets an explicit default route plus a bind-mounted hosts entry.
#
# Usage:
#   ND_ORIGIN_HOST=minipc-xxxx.tailxxxxx.ts.net ND_HOST_IP=100.x.y.z \
#     bash scripts/prepare-emulator-net.sh [serial]
set -euo pipefail
export ANDROID_HOME=${ANDROID_HOME:-/home/tony/Android/Sdk}
export PATH="$ANDROID_HOME/platform-tools:$PATH"
SERIAL=${1:-emulator-5580}
: "${ND_ORIGIN_HOST:?set ND_ORIGIN_HOST=<host>.<tailnet>.ts.net}"
: "${ND_HOST_IP:?set ND_HOST_IP=<tailscale IPv4 of the desktop>}"

adb -s "$SERIAL" root >/dev/null 2>&1 || true
sleep 2
adb -s "$SERIAL" wait-for-device

# Bring wifi up if the guest booted with it down.
if ! adb -s "$SERIAL" shell "ip -4 addr show wlan0 | grep -q 'inet 10.0.2'"; then
  adb -s "$SERIAL" shell "svc wifi enable" >/dev/null 2>&1 || true
  adb -s "$SERIAL" shell "cmd wifi connect-network AndroidWifi open" >/dev/null 2>&1 || true
  for _ in $(seq 1 30); do
    if adb -s "$SERIAL" shell "ip -4 addr show wlan0 | grep -q 'inet 10.0.2'"; then break; fi
    sleep 2
  done
fi

# Android keeps per-network policy routes; the wlan0 table was empty in this AVD,
# which drops the DHCP default route even though ConnectivityService lists it.
# Some AVDs expose the wifi policy table as "wlan0", newer images only ship the
# numeric "local_network" table (97). Install the routes in whichever exists.
for TBL in wlan0 97; do
  if adb -s "$SERIAL" shell "ip route replace 10.0.2.0/24 dev wlan0 scope link table $TBL" 2>/dev/null &&
     adb -s "$SERIAL" shell "ip route replace default via 10.0.2.2 dev wlan0 table $TBL" 2>/dev/null; then
    break
  fi
done
# Belt and braces: the plain main table must also reach the slirp gateway.
adb -s "$SERIAL" shell "ip route replace default via 10.0.2.2 dev wlan0" 2>/dev/null || true

# Resolve the desktop name without relying on the emulator DNS proxy.
adb -s "$SERIAL" shell "printf '%s %s\n' '$ND_HOST_IP' '$ND_ORIGIN_HOST' > /data/local/tmp/hosts.nd"
adb -s "$SERIAL" shell "chmod 644 /data/local/tmp/hosts.nd"
adb -s "$SERIAL" shell "mount -o bind /data/local/tmp/hosts.nd /system/etc/hosts"

adb -s "$SERIAL" shell "ping -c 1 -W 4 $ND_ORIGIN_HOST"