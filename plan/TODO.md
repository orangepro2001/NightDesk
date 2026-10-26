# NightDesk implementation TODO

更新时间：2026-10-27（Asia/Seoul）

## 1. Environment and driver research — done

- [x] Inspect host and confirm Parsec/Tailscale availability.
- [x] Install .NET 10 SDK 10.0.402 with Scoop.
- [x] Confirm VDD device interface, add/remove IOCTL, 100 ms keep-alive requirement, signer identity and existing manager behavior.
- [x] Confirm all Android builds/debugging can use SSH host `AI`.

## 2. Core app and tests — done

- [x] WPF project and executable assertion harness.
- [x] DPAPI settings, native APIs, VDD heartbeat, topology switch/restore and recovery snapshot verification.
- [x] Global hotkey and execution-state lifecycle.
- [x] Shutdown guard requiring an independent active hardware output before owned-display cleanup.

## 3. UI and remote access — done

- [x] Chinese WPF controls and system tray.
- [x] Loopback authenticated API and fixed command allow-list.
- [x] Ownership-checked Tailscale Serve controls and embedded remote page.
- [x] Remote-enable fixed to accept a structurally exact NightDesk route instead of requiring a historical fingerprint match.
- [x] EXE, window and tray icon unified.

## 4. Pairing / device tokens — done

- [x] 5-minute single-use QR ticket; no long-lived token in QR payload.
- [x] Permanent per-device token; DPAPI-protected registry stores only SHA-256 hashes.
- [x] Trusted-device list, last-seen metadata and individual revocation.
- [x] Android QR scanner and secure token persistence.

## 5. Android client — done

- [x] Status refresh, off/on/restore, sleep switch and VDD add/remove.
- [x] Update check on startup, every 6 hours and manually.
- [x] Validate package, version, minSdk, size, SHA-256 and signature before system installer handoff.
- [x] Multi-host storage: up to 20 hosts, active switch, delete, schemaVersion 3 migration.
- [x] Keystore AES-GCM encryption; plaintext-token regression test.
- [x] Adaptive/round launcher icon.

## 6. AI update feed — done

- [x] Move feed from desktop process to AI `nightdesk-update-feed.service`.
- [x] Loopback `127.0.0.1:17768`, enabled at boot.
- [x] Separate Tailscale Serve paths, existing routes untouched, no Funnel.
- [x] Atomic publish and server-side size/SHA-256 verification.
- [x] Publish Android 1.0.4 / versionCode 5 and verify the live Tailnet URLs.

## 7. Bundled VDD installer — done

- [x] Embed official Parsec VDD 0.45 installer in the single-file EXE.
- [x] Detect missing driver through the VDD device interface.
- [x] Validate exact size, SHA-256 and Parsec signer subject before execution.
- [x] Extract to `%LOCALAPPDATA%\NightDesk\driver\`, run with UAC + `/S`, and re-probe.
- [x] Auto-attempt only once per user; expose manual retry.
- [x] Tests for embedded payload, extraction, hash/signature match, tamper rejection and missing payload rejection.

## 8. Release verification — done

- [x] Windows 1.4.0 Release build with 0 warnings / 0 errors.
- [x] Windows Release suite: 176 passed; no display state changed.
- [x] Publish `NightDesk.exe` and `nx-host.exe` together through `tools\publish-windows.ps1`.
- [x] Verify the sidecar smoke check and the final sizes/SHA-256.
- [x] Android 1.0.4 AI build, instrumented multi-host tests and live-feed publication.
- [x] Android stage 5 candidate rebuilt and re-tested on AI.
- [x] README, HANDOFF, ACCEPTANCE, progress, findings and implementation-plan updates.

## Remaining manual acceptance (not code-completion blockers)

- [ ] Controlled replacement of PID 13816 (currently 1.3.1, no sidecar beside it) only after a physical display is available and `recovery.json` can be verified. Deploy both 1.4.0 EXEs to the same directory.
- [ ] Real display off/on/restore, hotkey and VDD add/remove with other VDD managers closed.
- [ ] End-to-end QR pairing/revocation from a physical Android device and second Tailnet node.
- [ ] True absent-driver VDD auto-install on a clean Windows machine/VM.
- [ ] AI host reboot recovery and long-term service supervision check.
