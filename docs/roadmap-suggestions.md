# 🗺️ Roadmap Suggestions

This document is a candid product assessment of **AtcSoft.VideoSurveillance**, produced from a full internal code audit (60+ findings, spot-verified against the actual source) cross-referenced with 2024-2025 market research on the leading NVR/VMS products (Blue Iris, Frigate, Milestone XProtect, Genetec, Scrypted, Agent DVR). It is *not* an implementation plan — it is an honest "where we stand and where to invest" memo for the dev team. The existing [`roadmap.md`](roadmap.md) tracks the (now-complete) build-out of the WPF API client; this document covers what comes *after* feature-parity: making this best-in-class.

**Overall assessment.** The architecture is genuinely strong and ahead of most open-source competitors in several places. First-class USB camera support (Media Foundation + WMI hot-plug), native WPF with D3D11VA GPU rendering, **in-process** FFmpeg (no brittle external-process orchestration), an OpenAPI-first REST API with SignalR real-time events, and .NET Aspire orchestration together form a foundation that Frigate (Linux/YAML-only, no native USB, no desktop UI) and even Blue Iris (dated UI, single-process Windows app) cannot fully match. But the product has three serious, category-defining gaps that currently keep it out of the top tier: **(1) no security model whatsoever on the server** (no auth, no HTTPS, open video files), **(2) no AI object detection** — now table-stakes, not a differentiator — and **(3) no mobile app or push notifications**. There is also a cluster of recording/reliability bugs that will bite production users. Fix the reliability bugs and the security model first (they are credibility blockers), then invest in AI detection and mobile to compete on features.

---

## 🐛 Potential Bugs & Reliability Risks

These are concrete defects found in the code that will cause incorrect behaviour in production, ordered by severity.

### ✅ ServerRecordingService silently overwrites concurrent recording sessions *(Fixed — already used TryAdd)*
`ServerRecordingService.StartRecording` (line ~63) guards with `if (sessions.ContainsKey(camera.Id))` and then does `sessions[camera.Id] = session` (indexer assignment, line ~83). The check and the write are not atomic — two concurrent callers can both pass the check, and the second indexer write **silently replaces** the first session, leaking its FFmpeg pipeline and orphaning a half-written recording file with no error.
**Why it matters:** API recording start is reachable by any caller (see Security) and by motion triggers on background threads; a double-fire produces a leaked process and a corrupt file.
**Fix:** Drop the `ContainsKey` pre-check; rely solely on `TryAdd` and treat a `false` return as "already recording." The WPF `RecordingService` already does this correctly with `TryAdd`.

### ✅ SegmentRecording uses unconditional indexer replacement, not TryUpdate *(Fixed — already used TryUpdate on server; WPF fixed in batch 3)*
`ServerRecordingService.SegmentRecording` (line ~331) does `sessions[cameraId] = newSession`. If `StopRecording` runs concurrently (e.g. from the inactive-session reaper) and removes the entry between the `TryGetValue` and the assignment, the new session is re-inserted for a camera that is **no longer supposed to be recording** — an orphaned entry with no owning pipeline.
**Fix:** Use `ConcurrentDictionary.TryUpdate(cameraId, newSession, oldSession)` so the replacement is conditional on the expected old value still being present.

### ✅ Segment-swap window where IsRecording returns false for an active camera (WPF) *(Fixed — batch 3: replaced TryRemove+TryAdd with TryUpdate)*
Because `RecordingSession.CurrentFilePath` is get-only, `RecordingService.SegmentRecording` does `TryRemove` (line ~380) then `TryAdd` (line ~393) to swap in a new session object. Between those two calls, `IsRecording` returns `false` for an actively recording camera, and `GetActiveSessions` returns stale data. A second `StartRecording` slipping into that window starts a **second pipeline on the same camera**.
**Fix:** Make `RecordingSession` mutable for the file path (or swap under a per-camera lock), so the dictionary entry is never absent mid-segment.

### ✅ ServerRecordingService ignores per-camera RecordingPath/Format overrides *(Fixed — already routed through GetEffectiveRecordingPath/Format)*
`ServerRecordingService.GenerateRecordingFilename` / `StartRecording` always read `settingsService.Recording.RecordingPath` and `.RecordingFormat`, ignoring `camera.Overrides?.Recording.RecordingPath` / `.RecordingFormat`. The WPF side correctly uses `GetEffectiveRecordingPath` / `GetEffectiveRecordingFormat` helpers. **Per-camera path/format overrides configured through the API are a silent no-op on the server.**
**Fix:** Route the server through the same effective-value helpers as WPF.

### ✅ Disk-space guard only fires at segment boundaries, never mid-recording *(Fixed — batch 5: EnforceDiskSpaceGuard() on IRecordingService; segmentation timer starts when either EnableHourlySegmentation or EnableDiskSpaceGuard is true; WPF overlap guard added)*
`ReclaimDiskSpaceIfNeeded` runs in `StartRecording` and `SegmentRecording` only. On a long manual recording (segmentation disabled, or `MaxRecordingDurationMinutes = 0`) the disk can fill completely while FFmpeg keeps writing, producing a corrupt/truncated file with no warning event and no fallback stop. See [`disk-space-guard.md`](disk-space-guard.md) for the current design.
**Fix:** Poll free space on a timer during active recordings; raise a warning event and gracefully stop (closing the muxer cleanly) when below `MinFreeSpaceMb` and reclaim cannot recover.

### ✅ No hard pre-recording disk gate *(Fixed — batch 4: ReclaimDiskSpaceIfNeeded returns bool; StartRecording/TriggerMotionRecording abort on false)*
`ReclaimDiskSpaceIfNeeded` tries to free space but if `EnableDiskSpaceGuard` is false or reclaim returns `StillShort`, recording **starts anyway**, producing a zero-byte/corrupt file. There is no hard refusal-to-start.
**Fix:** When reclaim fails to reach `MinFreeSpaceMb`, abort the start, raise an error event, and surface it on the tile/API rather than starting a doomed recording.

### 🟠 MaxRecordingDurationMinutes is declared but never enforced
`RecordingSettings.MaxRecordingDurationMinutes` (default 60) is persisted but never read by either recording service. Segmentation is driven independently by `EnableHourlySegmentation`'s hard-coded 60-minute boundary. Changing this setting does nothing.
**Fix:** Either wire `MaxRecordingDurationMinutes` into the segmentation boundary or remove the setting to stop misleading operators.

### 🟡 IPv6 literal addresses produce invalid URIs in BuildUri
**Verified:** `CameraConfiguration.BuildUri()` builds `$"{scheme}://{userInfo}{Connection.IpAddress}:{Connection.Port}{normalizedPath}"` with no IPv6 bracketing. `fe80::1` becomes `rtsp://fe80::1:554/stream`, which `System.Uri` and FFmpeg both reject — IPv6 literals must be bracketed (`rtsp://[fe80::1]:554/stream`).
**Fix:** Detect `IPAddress.TryParse` → `AddressFamily.InterNetworkV6` and wrap the host in `[...]`.

### ✅ AnalysisFrameRate default (30) is silently halved to 15 *(Fixed — batch 2: default lowered to 15 to match MaxTargetFps cap)*
`MotionDetectionSettings.AnalysisFrameRate` defaults to 30, but `MotionDetectionService` clamps to `MaxTargetFps = 15` with no log line or UI warning. Operators tuning for "30 fps analysis" silently get 15. Documented in [`motion-detection.md`](motion-detection.md) but invisible at runtime.
**Fix:** Lower the default to a value inside the clamp range, or log/surface the clamp.

### ✅ Motion-frame analysis swallows all exceptions *(Fixed — batch 2: throttled warning logging with ConsecutiveFails counter)*
`MotionDetectionService.CaptureAndProcessAsync` / `ProcessCapturedFrame` catch blocks discard every exception with "Silently ignore." A corrupt JPEG, `System.Drawing` failure, or OOM produces "no motion ever detected" with **zero diagnostic trail**.
**Fix:** Log at warning level with throttling; expose a consecutive-failure counter on the service.

### 🟡 Server-side motion detection is a no-op stub
`ServerMotionDetectionService.RunDetectionLoopAsync` only `await Task.Delay(Timeout.Infinite)` and logs a warning; `MotionDetected` is never raised, no frames are analysed, and `GetAnalysisResolution` hard-returns `(320, 240)` ignoring settings. **Any server-hosted camera produces zero motion alerts and zero motion-triggered recordings.** This is a functional hole, not a tuning issue — it should be flagged prominently in docs until implemented.

### ✅ Low-severity cleanups *(Fixed — batch 1 & batch 2)*
- `RecordingService.StopAllRecordings` double-sweeps `postMotionTimers` — fixed.
- `Debug.WriteLine` in `CameraTile.xaml.cs` motion paths — removed; snapshot failure now routed through `LogSnapshotFailed`.

---

## 🔒 Security & Privacy

This is the most urgent category. The server edition is currently **wide open**. For a product that captures live video and stores credentials, this is a blocker for any non-loopback or commercial deployment.

| Gap | Status | Impact |
|-----|--------|--------|
| No authentication/authorization | **Verified** — `Program.cs` has no `AddAuthentication`/`UseAuthentication`/`RequireAuthorization`; hub has no `[Authorize]` | Any network caller can do camera CRUD, start/stop recording, read/write settings, capture snapshots |
| Video files served unauthenticated | **Verified** — two `UseStaticFiles` mounts (`/streams`, `/recordings-files`) with no auth check | Anyone reachable can download live HLS segments and complete recordings by enumerating UUID paths |
| No HTTPS enforcement | **Verified** — no `UseHttpsRedirection`/`UseHsts` | Camera passwords (in `CreateCameraRequest`) travel in plaintext |
| No rate limiting | **Verified** — no `AddRateLimiter` | Recording-start and `StartStream` each spawn FFmpeg; trivial CPU/disk/handle exhaustion |
| Credentials stored as plaintext | `AuthenticationSettings.UserName`/`Password` serialized plainly to `cameras.json`; `BuildUri` embeds plaintext password | At-rest credential exposure |
| `/health/recordings` unauthenticated | Returns session counts, `CameraId`, `CameraName`, `FilePath`, durations | Operational detail leak |
| No audit log | `JsonCameraStorageService` / `JsonApplicationSettingsService` record no actor/timestamp/prior-value | No post-incident trail; compliance blocker |

**What to do (in order):**
1. **Add authentication + authorization** (JWT bearer or ASP.NET Core Identity). Apply `RequireAuthorization()` to all endpoint groups and `[Authorize]` to `SurveillanceHub`. This single change unblocks every regulated deployment.
2. **Gate the static-file mounts** behind the same auth — serve `/streams` and `/recordings-files` through an authorized endpoint or middleware, not anonymous `UseStaticFiles`.
3. **Enforce HTTPS** (`UseHttpsRedirection` + `UseHsts`) so credentials aren't sent in clear.
4. **Encrypt credentials at rest** — DPAPI on Windows (`ProtectedData`), or a pluggable secret store; never persist plaintext passwords.
5. **Rate-limit** the FFmpeg-spawning endpoints (`POST /cameras/{id}/recording`, hub `StartStream`).
6. **Add an audit log** — append-only record of mutating operations (who, when, what, prior value) for the storage services.
7. **Privacy UX** — recording-consent overlays, retention controls surfaced in UI, transparency about what is recorded where. Becomes mandatory once face recognition is added.

Even adding RBAC with AD/LDAP (the enterprise benchmark from Milestone/Genetec) is downstream of this — there is no point in roles when there is no authentication.

---

## 🎯 Missing Features vs. Market Leaders

| Feature | This product | Blue Iris | Frigate | Why it matters |
|---------|-------------|-----------|---------|----------------|
| AI object detection (person/vehicle/animal) | ❌ none | ✅ built-in (v6) | ✅ built-in | **Now baseline.** Without it, every shadow/cloud triggers; no smart alert filtering |
| License plate recognition | ❌ | ✅ (via CodeProject.AI) | ✅ free, local (v0.16) | Prosumer expectation for gate/driveway — esp. relevant given USB-at-entrance use case |
| Face recognition | ❌ | ✅ (v6) | ✅ free, local (v0.16) | Privacy-conscious users reject cloud face AI; local is the draw |
| Mobile app (iOS/Android) | ❌ | ✅ (dated) | ⚠️ 3rd-party only | **#1 cited adoption barrier** for non-enterprise NVR |
| Push notifications w/ snapshot | ❌ | ✅ Firebase | ✅ WebPush (v0.15) | Live view + alert is the mobile MVP |
| Motion zones / exclusion masks | ❌ full-frame only | ✅ per-zone | ✅ polygon zones | Top wishlist item; eliminates false positives from trees/roads |
| Cloud/hybrid backup | ❌ local-only | ⚠️ none | ⚠️ none | 35% of surveillance data is cloud (2024); off-site DR expected |
| Two-way audio | ❌ | ✅ | ⚠️ | Driven by doorbell/intercom adoption |
| Multi-user / RBAC | ❌ single-admin (no auth at all) | ⚠️ basic | ❌ | Blocks household/SMB/regulated |
| PTZ control | ❌ | ✅ | ✅ + autotrack | Standard VMS selection criterion |
| Clip export / trim | ❌ Play/Delete/OpenFolder only | ✅ | ✅ | `RecordingsBrowserDialogViewModel` has no trim/export command |
| Native USB cameras | ✅ **first-class** | ⚠️ workarounds | ❌ not recommended | **Our differentiator** — most NVRs treat USB as second-class |
| Native desktop app + GPU render | ✅ WPF + D3D11VA | ✅ (dated) | ❌ web-only | **We lead** on desktop UX quality |
| In-process FFmpeg reliability | ✅ | ✅ | ⚠️ external FFmpeg failures | **Reliability advantage** |

**Narrative priority:** The two gaps that cost the most users are **AI detection** (everyone now ships it) and **mobile + push** (the single most-cited reason people pick Blue Iris over Frigate). Motion zones are the cheapest high-value win in this list. Clip export is a small, self-contained feature with outsized perceived value. PTZ and two-way audio matter but are camera-protocol-heavy and lower priority.

---

## 🤖 AI & Smart Detection

The current motion detection (`MotionDetectionService`) is pixel-differencing with bounding boxes — it answers "did pixels change?" but not "what changed?" Modern users expect object classification as a baseline, and it is the foundation for everything else (smart search, smart alerts, LPR/face).

**The pragmatic path (lowest risk → highest value):**

1. **Local object classification via ONNX Runtime.** The motion pipeline already produces downscaled frames and bounding boxes — feed the motion-cropped region (Frigate's proven "motion-crop-then-classify" pattern, far cheaper than full-frame) into a small YOLO/SSD ONNX model for person/vehicle/animal/package labels. Add `Label`/`Confidence`/`ObjectType` to the `BoundingBox` model (currently geometry-only).
2. **GPU acceleration.** The project already ships `AtcSoft.VideoEngine.DirectX` (D3D11VA). Run inference on the GPU/NPU via ONNX Runtime DirectML — this also fixes the "all motion analysis is GDI+ on CPU" scalability ceiling (see Performance). Intel Core Ultra NPU and DirectML on consumer GPUs make this viable on mini-PCs.
3. **Smart alert filtering.** Once labels exist, gate recordings/notifications on object type + confidence + zone — this is the false-alarm reduction users actually want.
4. **LPR and face recognition** as opt-in layers on top of classification (both shipped free + local in Frigate 0.16; this is the bar). Face recognition needs explicit privacy/consent UX before shipping.
5. **Motion zones** (exclusion polygons) — implement first, it benefits both pixel and AI detection. Neither `MotionDetectionSettings` nor `MotionDetectionOverrides` has any zone concept today.

A lower-cost alternative that opens the entire modern camera ecosystem: **ingest ONVIF Profile T metadata events** from AI-capable edge cameras instead of running inference server-side. This is the route Milestone XProtect deliberately took. Worth doing regardless, since it's also a camera-compatibility win.

**Server-side caveat:** none of this can land on the server until `ServerMotionDetectionService` is implemented (it's currently a no-op stub).

---

## 📱 Remote Access & Mobile

The product has no mobile story, and remote access is undocumented/insecure. This is the single biggest non-AI adoption gap.

**What users expect (and what we have):**

- **Native mobile app** — live view, push notifications *with snapshot thumbnail*, timeline/event scrubbing, PTZ. We have none. The HLS infrastructure already exists server-side (`/streams/{cameraId}/playlist.m3u8`), so a mobile app has a streaming backbone to build on — but it needs auth first.
- **Push notifications** — the SignalR hub already broadcasts connection/motion/recording events server-side. A mobile push pipeline (FCM/APNs, or WebPush like Frigate v0.15) is an *additive* layer on existing events, not a rewrite. This is the highest-leverage mobile feature.
- **Remote access without port-forwarding** — Scrypted (WebRTC P2P) and Agent DVR (relay) both solve this out of the box; we require manual networking. At minimum, document a VPN-based path; longer term, a relay or WebRTC option.
- **Browser live-view quality** — the Blazor app exists, but the audit notes hardcoded localhost CORS origins (`defaultAllowedOrigins`, verified) silently break non-loopback browser access. Fix CORS config + document it so the Blazor client works off-box at all.

**Recommendation:** Build push-on-existing-events first (small, high impact), then a thin mobile live-view client over the existing HLS endpoints — but only after authentication exists, since both expose video.

---

## 🔗 Integrations & Ecosystem

This is where a modest investment unlocks a disproportionate number of users — the self-hosted NVR community in 2024-2025 is organised around Home Assistant, MQTT, and webhooks.

| Integration | Effort | Unlocks |
|-------------|--------|---------|
| **MQTT publisher** | Low-Medium | Home Assistant, Node-RED, any automation. **The single most-requested integration surface.** The SignalR hub already routes connection/motion/recording events — publish the same events to MQTT topics |
| **Outbound webhooks** | Low | IFTTT, Zapier, Slack, PagerDuty, HA. HTTP POST on motion/connect/recording events. Agent DVR ships this as a first-class action |
| **Inbound webhook / external trigger** | Low | Door sensors, alarm panels, NFC start a recording via API call (the REST API can start recording but isn't designed for low-code callers) |
| **Home Assistant native integration** | Medium | HACS integration exposing camera/binary-sensor/switch entities — Frigate's deepest differentiator |
| **Smart-home bridges (HomeKit/Google/Alexa)** | High | Scrypted's entire identity; blocking gap for mixed smart-home households |
| **Cloud storage egress (S3/B2/Azure Blob)** | Medium | Off-site DR; market-preferred "local-first + async cloud" model |
| **Alarm management (intrusion panels)** | Medium-High | Enterprise SOC workflow gate (Milestone/Genetec benchmark) |

**Recommendation:** Ship **MQTT publish + outbound webhooks first.** Both are thin adapters over the event flow that already exists in `SurveillanceHub`, and together they cover the overwhelming majority of community automation requests. SignalR is .NET/browser-specific — MQTT is the lingua franca we're currently missing.

---

## 🖥️ UI/UX Improvements

Specific, actionable improvements referencing actual screens.

**Keyboard / navigation**
- The standalone **CameraWall.Wpf.App `MainWindow.xaml` has zero `InputBindings`** — entirely mouse-dependent. The VideoSurveillance.Wpf.App already defines Ctrl+1–6/F5/F11/F1; port the same set over.

**Live view (`LiveView.xaml` / `LiveViewViewModel`)**
- **No camera search/filter** — add a name/status filter box above the `ItemsControl`. Painful past a handful of cameras.
- **No layout quick-select** (1×1, 2×2, 3×3, 4×4) — add preset buttons; today switching grids requires editing a layout in a dialog and reloading.
- **Picture-in-picture** — full-screen opens a separate `FullScreenCameraWindow`; no floating mini-player exists.

**Tile interaction (`CameraTile.xaml`)**
- Zoom is rubber-band-only — add numeric zoom levels (50/100/200%) / a slider alongside the existing 1:1 reset.

**Camera & layout lists**
- `CameraListView.xaml` / `LayoutListView.xaml` DataGrids are `IsReadOnly` — **no search/filter, no inline rename, no drag-reorder**. Add a filter box; allow double-click inline `DisplayName` edit; apply the `Atc.DualListSelector` reorder pattern (already used in `AssignCameraDialog`) to layouts.

**Status bar**
- **No live disk-usage indicator** in either app's status bar, despite the Storage settings exposing `MinFreeSpace`. Surface current recording-folder usage / free space — directly relevant to the disk-space bugs above.

**Notifications (`NotificationHistoryView.xaml`)**
- No severity column/colour-coding, no filter, no unread badge on the ribbon button. Add severity + filter + an unread count.

**Theme**
- No in-app dark/light toggle — only reachable via the Settings dialog (Ctrl+Comma). Add a ribbon/status-bar toggle.

**Accessibility (whole `Wpf.Core` XAML surface)**
- **No `AutomationProperties.Name` anywhere** — icon-only action buttons (Edit/Delete/Record/Snapshot) and status `Ellipse`/`Path` indicators (connection/motion/recording dots) announce as generic "Button"/nothing to screen readers, and rely on colour as the sole channel. Add `AutomationProperties.Name` to every icon button and a hidden/automation text alternative to every status indicator. WCAG compliance is becoming a professional-software expectation.

**Validation (`CameraConfigurationDialogViewModel`)**
- `CanSave()` checks only `DisplayName` non-emptiness + source identity; it never calls `Validator.ValidateObject`, so the `[Range(1,65535)]` port and `[Required]` annotations are decorative — port 0 or 99999 saves fine. Run DataAnnotations validation before persist. (Connection-test-before-save can stay optional but consider a soft warning.)

---

## ⚡ Performance & Scalability

What limits scale today, and the architectural moves that help.

- **Motion detection is CPU/GDI+ bound.** `ConvertToGrayscale` uses `System.Drawing.Bitmap` + unsafe pointers and `CalculateFrameDifferenceWithBoundingBoxes` is a nested per-pixel C# loop at 800×600 default. Across many cameras this is *the* bottleneck. **Fix:** move differencing to the GPU via the existing `AtcSoft.VideoEngine.DirectX` (DirectCompute) path, or at minimum SIMD-vectorise the grayscale/diff loops. This is also the prerequisite for affordable AI inference.
- **FFmpeg open/read timeouts are compile-time constants.** **Verified:** `Demuxer.cs` hard-codes `OpenTimeoutSeconds = 15` / `ReadTimeoutSeconds = 10`; the user-facing `ConnectionAppSettings.ConnectionTimeoutSeconds` only sizes a `CameraTile` poll loop and never reaches `avformat_open_input`. High-latency cameras can't be tuned. **Fix:** thread the setting into the demuxer interrupt callback. Same for hard-coded `probesize=50000000` / `analyzeduration=10000000` — expose via `StreamOptions`.
- **WPF reconnect ignores the backoff helper.** **Verified:** `CameraTile.TryAutoReconnect` uses a flat `TimeSpan.FromSeconds(GetEffectiveReconnectDelaySeconds())` and never calls `ReconnectBackoff.ComputeDelay` — which *exists*, is documented (base 30s, cap 15min), and is used correctly server-side. A flapping camera hammers the network at a constant rate (~2,880 attempts/day per the helper's own remarks). **Fix:** wire `CameraTile` through `ReconnectBackoff.ComputeDelay(consecutiveFailures)`. This is a near-trivial change with real network-load impact.
- **SignalR fan-out at many cameras.** Per-frame motion/bounding-box broadcasts to all clients will not scale linearly. Plan for a backplane (Redis) if multi-client/multi-camera, and throttle bounding-box broadcast rate.
- **Observability is thin.** `MaxConsecutiveReadErrors = 30` is a hidden `VideoPlayer` constant with no exposure; there are no packet-loss/jitter/latency metrics on `VideoStreamInfo`. A degraded-but-not-dead stream is invisible until the threshold trips. **Fix:** expose a running read-error count and basic stream-health metrics so `CameraTile`/health endpoints can distinguish "degraded" from "lost."
- **No failover/redundancy.** Mid-market gap vs. Milestone/Genetec hot-standby. Even cold-standby recording-server promotion materially changes enterprise evaluations — lower priority than the above.

---

## 🚀 Quick Wins (High Impact, Low Effort)

Ordered by impact-to-effort.

1. **Wire `CameraTile.TryAutoReconnect` to `ReconnectBackoff.ComputeDelay`** — the helper already exists and is used server-side; this is a few lines and stops flapping cameras from hammering the network.
2. **Fix the `ServerRecordingService` `TryAdd`/`TryUpdate` race** — remove the `ContainsKey` pre-check, switch the segment swap to `TryUpdate`. Prevents leaked pipelines and orphaned files.
3. **Bracket IPv6 literals in `CameraConfiguration.BuildUri`** — small correctness fix; IPv6 cameras currently can't connect at all.
4. **Run DataAnnotations validation in `CameraConfigurationDialogViewModel.CanSave`** — call `Validator.ValidateObject`; stops invalid ports/empty fields persisting.
5. **Outbound webhook dispatcher on existing events** — POST on motion/connect/recording; opens IFTTT/Zapier/Slack/HA. Thin layer over `SurveillanceHub`.
6. **`AutomationProperties.Name` on icon buttons + status dots** in `CameraListView.xaml`/`LayoutListView.xaml`/`CameraTileBadge.xaml` — mechanical, closes the accessibility gap.
7. **Keyboard shortcuts in CameraWall.Wpf.App `MainWindow.xaml`** — copy the binding set already in VideoSurveillance.Wpf.App.
8. **Replace `Debug.WriteLine` with `ILogger<CameraTile>`** in `CameraTile.xaml.cs` motion paths — the logger is already injected; makes motion events visible in Serilog.
9. **Camera search/filter box** in `LiveView.xaml` and `CameraListView.xaml` — a `CollectionViewSource` filter; big usability gain.
10. **Layout quick-select preset buttons** (1×1/2×2/3×3/4×4) in `LiveView.xaml`.
11. **Disk-usage indicator** in both status bars — surfaces the data the disk-space guard already computes.

---

## 📊 Competitive Summary Table

Legend: 🟢 leads / strong · 🟡 matches / partial · 🔴 lags / missing

| Capability | AtcSoft.VideoSurveillance | Blue Iris | Frigate | Milestone XProtect |
|------------|:--:|:--:|:--:|:--:|
| Native USB cameras (first-class) | 🟢 | 🟡 | 🔴 | 🟡 |
| Native desktop UI + GPU render | 🟢 WPF + D3D11VA | 🟡 (dated) | 🔴 web-only | 🟢 |
| In-process video engine reliability | 🟢 in-proc FFmpeg | 🟢 | 🟡 ext. FFmpeg | 🟢 |
| OpenAPI-first API + real-time events | 🟢 REST + SignalR | 🟡 | 🟡 MQTT/REST | 🟢 SDK |
| Aspire / cloud-ready orchestration | 🟢 | 🔴 | 🟡 Docker | 🟡 |
| RTSP/ONVIF camera support | 🟡 RTSP/HTTP | 🟢 ONVIF | 🟡 | 🟢 Profiles S/T/G/M |
| Recording + segmentation + timelapse | 🟢 | 🟢 | 🟢 | 🟢 |
| Motion detection | 🟡 pixel-diff (WPF only; server stub) | 🟢 | 🟢 | 🟡 (edge/Profile T) |
| AI object detection | 🔴 none | 🟢 | 🟢 | 🟡 (Profile T / partner) |
| LPR / face recognition | 🔴 | 🟢 | 🟢 free/local | 🟢 (partner) |
| Motion zones / exclusion masks | 🔴 | 🟢 | 🟢 | 🟢 |
| Authentication / RBAC | 🔴 **none** | 🟡 | 🔴 single-admin | 🟢 AD/LDAP/OIDC/SCIM |
| HTTPS / transport security | 🔴 | 🟢 (v6) | 🟡 | 🟢 |
| Audit logging | 🔴 | 🟡 | 🔴 | 🟢 |
| Mobile app + push | 🔴 | 🟢 | 🟡 3rd-party | 🟢 |
| Home Assistant / MQTT | 🔴 | 🟡 | 🟢 deepest | 🟡 |
| Webhooks / IFTTT | 🔴 | 🟢 | 🟡 (via MQTT) | 🟢 SDK |
| Clip export / trim | 🔴 | 🟢 | 🟢 | 🟢 |
| PTZ control | 🔴 | 🟢 | 🟢 + autotrack | 🟢 |
| Two-way audio | 🔴 | 🟢 | 🟡 | 🟢 |
| Cloud / hybrid backup | 🔴 | 🔴 | 🔴 | 🟢 geo-redundant |
| Multi-site federation | 🔴 | 🔴 | 🔴 | 🟢 |
| Accessibility (WCAG) | 🔴 | 🟡 | 🔴 | 🟡 |

**Bottom line:** we **lead** on USB-first support, desktop UX, video-engine reliability, and developer/orchestration story; we **match** on recording fundamentals; we **lag hard** on security (a credibility blocker that must be fixed first), AI detection, mobile/push, and ecosystem integrations. Close security → then AI + mobile + MQTT/webhooks, and the product moves from "promising .NET NVR" to genuinely best-in-class.
