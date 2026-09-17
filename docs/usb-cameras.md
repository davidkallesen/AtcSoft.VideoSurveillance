# 🎥 USB Camera Support

Architectural overview, configuration model, and operator-facing behaviour for the USB / DirectShow / UVC webcam path. Companion to [`docs/roadmap-usb-cameras.md`](roadmap-usb-cameras.md), which tracks status of the rollout.

---

## 🎯 TL;DR

- 💻 **Where it works.** Windows. The Linux V4L2 + macOS AVFoundation paths are scaffolded but deferred (Phase 10 in the roadmap).
- ✅ **What you can do today.** Open a USB camera, record it (MP4/MKV via the in-process FFmpeg pipeline), capture snapshots, rotate, and recover gracefully from unplug/replug. HLS re-streaming to a browser is not yet implemented for USB sources.
- ⏸️ **What's deferred.** UVC PTZ + property pages (brightness / focus / exposure) — Phase 11.
- 🏠 **Hosts.** `AtcSoft.CameraWall.Wpf.App` (standalone) wires the Windows enumerator into DI directly. `AtcSoft.VideoSurveillance.Api` (server) does the same when running on Windows. `AtcSoft.VideoSurveillance.Wpf.App` (API client) talks to the server over `GET /devices/usb` instead of enumerating locally.

---

## ⚙️ Configuration model

`ConnectionSettings.Source` is the discriminator:

```csharp
public enum CameraSource
{
    Network = 0,  // existing — uses IpAddress / Port / Protocol / Path
    Usb = 1,      // new      — uses Connection.Usb (UsbConnectionSettings)
}
```

The two configuration shapes are mutually exclusive — fields belonging to the unused branch are ignored. JSON storage migration is additive: cameras saved before USB landed have no `Source` field and deserialize with `Source = Network` (the default), so no migration step is needed.

### 🔌 `UsbConnectionSettings`

| Field | Notes |
|-------|-------|
| `DeviceId` | The Media Foundation symbolic link, e.g. `\\?\usb#vid_046d&pid_085e&mi_00#7&...`. Stable across reboots and USB-hub reshuffles — **the only safe identifier**. |
| `FriendlyName` | Display-only. The OS may surface duplicate friendly names if you have two cameras of the same model; `DeviceId` is the tie-breaker. |
| `Format` | Optional `UsbStreamFormat` — `Width × Height @ FrameRate (PixelFormat)`. `null` = let the driver pick its default. |
| `PreferAudio` | Off by default. When set, the Phase 9 audio path will pull a companion microphone track from the same device. |

### 🎞️ `UsbStreamFormat`

A capture-format triple — resolution, frame rate, pixel format. Pass-through to FFmpeg's dshow / v4l2 / avfoundation options (`video_size`, `framerate`, `pixel_format`). `FrameRate` is `double` so non-integer rates (29.97, 59.94) round-trip cleanly.

---

## 🏛️ Architecture flow

```
Add USB camera in dialog
  ↓
CameraConfiguration { Source = Usb, Usb = { DeviceId, FriendlyName, Format, ... } }
  ↓
CameraUriHelper.BuildSourceLocator(camera)
  ↓
SourceLocator { Uri = dshow:<encoded>, InputFormat = "dshow", RawDeviceSpec = "video=Friendly Name", VideoSize, FrameRate, PixelFormat }
  ↓
IMediaPipeline.Open(locator, settings) / IVideoPlayer.Open(uri, options)
  ↓
Demuxer.Open
  - av_find_input_format("dshow")  ← chosen because options.InputFormat ≠ Auto
  - avformat_open_input(ctx, RawDeviceSpec, dshowFormat, dict)
  - dict carries: video_size, framerate, pixel_format, rtbufsize=100000000
```

Network cameras still take the legacy path: `BuildSourceLocator` returns a locator with the old `rtsp://...` `Uri` and `null` `InputFormat`, the demuxer's auto-detect path runs unchanged, and the option dictionary keeps the rtsp keys (`rtsp_transport`, `probesize`, `analyzeduration`, `timeout`/`stimeout`).

The synthetic `dshow:<friendly-name>` URI in the locator is purely cosmetic — keeps log lines and exception messages readable. FFmpeg never parses it for USB sources; the demuxer reads `RawDeviceSpec` directly.

---

## 🪪 Stable identity

USB hub reshuffles, replug events and (rare) Windows updates can change the friendly name *or* even the symbolic link, but in practice:

1. The symbolic link encodes `vid_xxxx&pid_xxxx&mi_NN` — we extract VID and PID via `UsbSymbolicLinkParser` so multiple instances of the same product can be told apart by interface number.
2. The friendly name is for human display only. **Never persist a camera by friendly name.** Reconcile by `DeviceId` first; fall back to `FriendlyName` only for legacy cameras saved before the symbolic link was captured.
3. `IUsbCameraEnumerator.FindByDeviceId` is case-insensitive — Windows is inconsistent about the case of `USB` vs `usb` in the link, especially across reboots.

---

## ⚡ Hot-plug

`IUsbCameraWatcher` raises `DeviceArrived` / `DeviceRemoved`. The Windows implementation (`WindowsUsbWatcher`) uses WMI's `__InstanceCreationEvent` / `__InstanceDeletionEvent` over `Win32_PnPEntity`, filtered by the camera/sensor class GUID `{E5323777-F976-4F5B-9B55-B94699C46E44}`.

A 2-second polling window is the WMI default — fast enough for human-perceived hot-plug, gentle on CPU. The alternative (a hidden message-pump window with `RegisterDeviceNotification`) would be lower-overhead but requires a UI thread; WMI works for headless servers too.

When a camera is unplugged, the connection state transitions to **`ConnectionState.DeviceUnplugged`** (distinct from `Error`). The reconnect-backoff loop is suspended for `DeviceUnplugged` cameras until the watcher reports `DeviceArrived` — without this, a single unplug would generate ≈1 M failed reconnect attempts per year.

---

## 💉 DI wiring

```csharp
// Default fallbacks — no-op, returns empty device list
services.AddSingleton<IUsbCameraEnumerator>(NullUsbCameraEnumerator.Instance);
services.AddSingleton<IUsbCameraWatcher, NullUsbCameraWatcher>();

// Replace with the Windows implementation
AtcSoft.VideoEngine.Windows.DependencyInjection.ServiceCollectionExtensions
    .AddWindowsUsbCameraSupport(services);
```

Keep the Null fallbacks registered first so:
- The composition root works on hosts that don't reference `AtcSoft.VideoEngine.Windows`.
- `GET /devices/usb` can detect the fallback case (`enumerator is NullUsbCameraEnumerator`) and return **HTTP 503** instead of an empty list — clients then know "platform doesn't support USB" vs. "no devices attached".

---

## 🌐 API surface

`GET /devices/usb` lists the cameras visible to the **server** host, not the client. Useful for the API-client WPF app where the operator wants to add a USB camera physically attached to the surveillance server. Schema:

```json
[
  {
    "deviceId": "\\\\?\\usb#vid_046d&pid_085e&mi_00#7&15ee2c2&0&0000#{e5323777-...}",
    "friendlyName": "Logitech BRIO",
    "vendorId": "046d",
    "productId": "085e",
    "isPresent": true
  }
]
```

`POST /cameras` (and `PUT /cameras/{id}`) accept the new `source: "usb"` discriminator and the `usbDeviceId`, `usbFriendlyName`, `usbWidth`, `usbHeight`, `usbFrameRate`, `usbPixelFormat`, `usbCaptureAudio` fields. Network and USB requests share the same endpoint — the server picks the right path based on `source`.

---

## 🔒 Privacy permissions

Windows 10 1903+ enforces a Camera privacy gate (`Settings → Privacy → Camera → Allow desktop apps to access your camera`). When this is off, FFmpeg's dshow demuxer fails with a generic `-1` from `avformat_open_input` — confusing in logs. Operators see the failure in the dialog's *Test Connection* result; document the privacy setting in any onboarding guide.

Group policy estates can enforce `LetAppsAccessCamera = Deny` system-wide. The enumerator still lists devices in that case (Media Foundation reads from a privileged subsystem), but the demuxer fails to open them. Surface the gap by checking the Test Connection result.

---

## ⏸️ What's not in scope (yet)

| Feature | Where it lives | Why deferred |
|---------|----------------|--------------|
| HLS re-streaming USB to browsers | `StreamingService` | Needs a different ffmpeg.exe argv (`-f dshow -i video=...`); the in-process pipeline still records & snapshots fine. The handler currently throws `NotSupportedException` for USB cameras with a clear message. |
| Capability discovery (resolution × FPS × pixfmt) | `MediaFoundationEnumerator` | Phase 3 first cut returns devices with empty `Capabilities`. The dialog will populate the capability picker by opening each device on demand once Phase 4.3 lands. |
| Audio companion track | `Demuxer` + `Remuxer` | Phase 9. The toggle exists in `UsbConnectionSettings.PreferAudio` but the audio packet path isn't wired through the remuxer yet. |
| UVC property pages (brightness, focus, …) | New `IUsbCameraPropertyService` | Phase 11. Blue-Iris-grade — pulls in `IAMCameraControl` / `IAMVideoProcAmp` interop. |
| V4L2 + AVFoundation enumerators | `AtcSoft.VideoEngine.Linux` (future), `AtcSoft.VideoEngine.macOS` | Phase 10. The demuxer already accepts `InputFormatKind.V4l2` / `AVFoundation` and translates the option dict correctly — only the enumerator + DI binding are missing. |

---

## 🩺 Troubleshooting

| Symptom | Likely cause |
|---------|--------------|
| Test Connection returns "stream open failed" but the device is listed | Privacy permission denied (per-app or per-machine); another process opened the camera first; format triple is unsupported by this device. |
| `IUsbCameraEnumerator.EnumerateDevices()` returns an empty list on a Windows host with a known webcam | Privacy permission revoked at the system level (Settings or GPO); or the Windows Camera Frame Server service is stopped. |
| Camera appears with `isPresent = true` but immediately disconnects on stream open | The driver only exposes media types we don't ask for. Try `Format = null` (driver default) before locking a triple. |
| Hot-plug events not firing | `WMI` service stopped; or your filter is too tight — `WindowsUsbWatcher` filters on `ClassGuid = {E5323777-...}` (KSCATEGORY_VIDEO_CAMERA), some vendors ship UVC cameras under different class GUIDs and need a vendor-specific addition. |
| Recording rotates fine for IP cameras but not for the USB camera | The `Player.SetRotation()` call needs to land **after** the player is in `Playing` state; if you set rotation before opening the dshow source, the GPU accelerator hasn't seen the resolution yet. The CameraTile path handles this automatically; custom code shouldn't pre-rotate. |
