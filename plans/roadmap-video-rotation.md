# 🔄 Video Rotation Roadmap

Plan for making a camera's **rotation** (`CameraDisplaySettings.Rotation`: 0 / 90 / 180 / 270, clockwise) apply to everything the operator sees, not only the live picture. Today the live picture rotates, but the **motion bounding boxes** do not follow it and **recordings play back unrotated**.

Affects **`AtcSoft.VideoEngine`** (recorder, frame capture, media probe), **`AtcSoft.VideoEngine.DirectX`** (software-frame upload), **`AtcSoft.VideoSurveillance.Core`** (a new mapping helper), **`AtcSoft.VideoSurveillance.Wpf.Core`** (`CameraTile`, motion overlay, full-screen windows, recording playback) and **`AtcSoft.CameraWall.Wpf`** (thumbnails).

← Back to the [roadmap home](roadmap.md)

---

## 🗂️ Status Legend

| Emoji | Meaning |
|-------|---------|
| ⬜ | Not started |
| 🟨 | In progress |
| ✅ | Complete |
| ⏸️ | Deferred (intentionally not in current scope) |

---

## 🐛 Reported Problem

> When I rotate a camera in the view, the rotation works fine, but:
> - the motion-detection bounding boxes are not rotated
> - the recording is in the original orientation, not rotated — hard to see after some days

---

## 🔍 Root-Cause Analysis

Traced through the code and checked by experiment where marked 🧪. The development machine has no cameras, so the experiments used a marked test stream played through the real engine, not a camera.

### How rotation works today

Rotation is a **render-time GPU transform only**. `CameraTile.ApplyRotation` → `IMediaPipeline.SetRotation` → `VideoPlayer.SetRotation` → `D3D11Accelerator` → `VideoProcessorRenderer`, which rotates during the NV12 → BGRA conversion (`VideoProcessorSetStreamRotation`). Decoded frames and packets are never rotated, and `VideoPlayer.StreamInfo.Width/Height` stay the **unrotated** decoder size.

### Cause 1 — Recordings carry no usable rotation

The recorder (`VideoEngine/Recording/Remuxer.cs`) is a packet-copy remuxer, so the pixels in the file are always the camera's original orientation. It tries to flag the rotation with a `rotate` **stream metadata tag** (`av_dict_set(ref outStream->metadata, "rotate", …)`), based on the assumption that FFmpeg's MP4 muxer turns that tag into a display matrix.

🧪 That assumption is wrong for FFmpeg 8.x. Tested with the FFmpeg 8.1.2 CLI (the build CI ships):

| Written as | Container | What ends up in the file |
|---|---|---|
| `rotate=90` stream tag | MP4 | **Nothing** — tag dropped, no side data |
| `rotate=90` stream tag | MKV | A plain text tag `ROTATE: 90`, no display matrix |
| Display-matrix side data | MP4 | `Display Matrix`, `rotation: 90` ✅ |
| Display-matrix side data | MKV | `Display Matrix`, `rotation: 90` ✅ |

Players only honour the **display matrix**. So no player rotates today's recordings.

On top of that, **the app's own playback never looks at rotation**. `FullScreenRecordingWindow` plays files in a WPF `MediaElement`, and nothing in `Demuxer` / `MediaProbe` reads a display matrix. Even a correctly flagged file is not guaranteed to play rotated in-app (Media Foundation's handling, especially for MKV, is unverified).

### Cause 2 — The box overlay letterboxes with the wrong aspect ratio

Motion analysis runs on snapshots from `VideoPlayer.CaptureFrameAsync`. On the GPU path that snapshot is the **already rotated** picture, so the box coordinates are in rotated space — which is what we want.

But `MotionBoundingBoxOverlay` positions the boxes inside a letterboxed area computed from `VideoWidth / VideoHeight`, and all three hosts feed it the **unrotated** `Player.StreamInfo` size:

- `Wpf.Core/UserControls/CameraTile.xaml.cs` (`UpdateMotionBoundingBoxes`)
- `Wpf.Core/Windows/FullScreenCameraWindow.xaml.cs`
- `Wpf/Windows/FullScreenCameraWindow.xaml.cs` (API-client app)

For a 16:9 camera rotated 90°, the picture is drawn 9:16 but the overlay assumes 16:9, so the boxes are stretched over the wrong area. At 180° the aspect ratio is unchanged, so boxes should already line up there.

### Cause 3 — Rotation is inconsistent across decode paths

- **Software-decoded frames are never rotated.** `D3D11Accelerator.ProcessSoftwareFrame` → `CpuFrameUploader` has no rotation. This covers MJPEG, USB cameras and any camera with hardware acceleration off.
- **The CPU snapshot fallback is never rotated.** `FrameCapture.CaptureFrame` returns the unrotated frame, so snapshots, thumbnails, timelapse frames and motion analysis all change orientation depending on which path produced the frame.
- **The server has no GPU**, so `SetRotation` there only stores a field: server snapshots are unrotated. (The comment in `Api/Services/VideoEngineMediaPipelineFactory.cs` says otherwise.)

### Cause 4 — Rotation is lost or mixed up in some flows

- `CameraTile.RecreatePlayer` builds a new player and pipeline and never calls `ApplyRotation`, so the picture snaps back to unrotated after any performance or override setting change. Same for the pipeline created in `InitializeServices`.
- **Tile swap (to verify):** `CameraGrid` swaps the camera entries, then `SwapPlayerWith` exchanges the players without re-applying rotation. Two swapped tiles may end up with each other's rotation.

### Related findings

- Recording **thumbnails** draw each frame into a fixed 320×240 cell with no aspect preservation, so a rotated (portrait) frame is squashed.
- `RecordingSettings.TranscodeVideoCodec` exists as a setting, but the engine has **no transcoding path** — the recorder is copy-only. Burning the rotation into the pixels is therefore not available today (see Decision 1).
- `CameraRotation`'s doc comment claims "display, snapshots, and (via MP4 metadata) recordings are all rotated". It will be true after this roadmap.
- There are **no tests** for rotation, the overlay mapping, the remuxer or motion detection.

---

## 🎯 Goal

> Rotating a camera rotates the live picture, the motion boxes, snapshots, thumbnails and recordings, on every decode path. A recording made on a rotated camera plays back rotated in the app and in common players (VLC, Windows Media Player, browsers).

Existing recordings stay as they are: they contain no rotation information to recover (Decision 3).

---

## 🧩 Design

### 1. One rule: "captured frames are always rotated"

`VideoPlayer.CaptureFrameAsync` returns the picture **as the operator sees it**, whichever path produced it. The CPU fallback (`FrameCapture`) rotates the frame before encoding the PNG. Motion analysis, snapshots, thumbnails and timelapse then agree by construction, and box coordinates are always in displayed space.

### 2. The player reports its displayed size

Add the post-rotation size to the player (`DisplayWidth` / `DisplayHeight`, swapped for 90 / 270) and feed **that** to the overlay instead of `StreamInfo.Width/Height`. One small Core helper does the swap so the three hosts cannot disagree.

### 3. The box mapping becomes a pure, tested helper

Move the letterbox + scale math out of `MotionBoundingBoxOverlay`'s private methods into a Core helper (`MotionBoxMapper` or similar): *(box, analysis size, displayed video size, container size) → screen rectangle*. The user control keeps only the drawing and smoothing.

### 4. Recordings carry a real display matrix

`Remuxer.Open` replaces the `rotate` tag with **display-matrix side data** on the output stream (`AV_PKT_DATA_DISPLAYMATRIX` on `codecpar->coded_side_data`, filled by `av_display_rotation_set`). FFmpeg's matrix angle is counter-clockwise while `CameraRotation` is clockwise, so the sign is pinned by a test, not by reasoning.

### 5. In-app playback applies the rotation itself

`MediaProbe` learns to read the rotation from a file. `FullScreenRecordingWindow` applies it as a layout transform on the `MediaElement` — **unless** Media Foundation already rotated the picture (detected by comparing `NaturalVideoWidth/Height` with the probed coded size), to avoid rotating twice.

### 6. Rotation survives player re-creation

`ApplyRotation` is called wherever a player or pipeline is created or exchanged: `RecreatePlayer`, `InitializeServices` and after `SwapPlayerWith`.

### 7. Software-decoded frames rotate too

`CpuFrameUploader` rotates while uploading (or the uploaded texture is routed through the same video-processor rotation), so MJPEG and USB cameras rotate live like hardware-decoded ones.

---

## 🛣️ Phases

Test-first: each phase starts with failing tests, then the fix.

| # | Phase | Status |
|---|-------|--------|
| 1 | Motion boxes follow rotation | 🟨 Built and unit-tested; awaiting a check on a real camera |
| 2 | Recordings carry a display matrix | ✅ |
| 3 | In-app playback honours the rotation | 🟨 Built; awaiting a check with a real recording |
| 4 | Rotation survives re-creation and swap | 🟨 Built; awaiting a check in the app |
| 5 | Software-decode path and CPU captures rotate | ✅ |
| 6 | Thumbnails keep their aspect ratio | 🟨 Built; awaiting a visual check |
| 7 | Docs | ✅ |

### Phase 1 — Motion boxes follow rotation 🟨

- Core helper for the displayed size of a rotated video, and the pure box-to-screen mapper.
- `MotionBoundingBoxOverlay` uses the mapper.
- `CameraTile`, both `FullScreenCameraWindow`s: pass the displayed size.
- Re-map when rotation changes while boxes are visible.

**Tests:** mapper for all four rotations × landscape/portrait containers × letterbox/pillarbox; a 16:9 source at 90° maps into a 9:16 area; zero sizes fall back to the full container.

### Phase 2 — Recordings carry a display matrix ✅

- `Remuxer.Open`: write display-matrix side data; remove the `rotate` tag.
- `MediaProbe`: read the rotation back (also needed by Phase 3).
- `InternalsVisibleTo` for `AtcSoft.VideoEngine.Tests`.

**Tests (in-process, need the FFmpeg DLLs):** write a short file through `Remuxer` for each rotation in **both MP4 and MKV**, read it back with `MediaProbe`, and assert the angle. This pins the clockwise / counter-clockwise sign. Rotation 0 writes no side data.

### Phase 3 — In-app playback honours the rotation 🟨

- `FullScreenRecordingWindow` / view model: probe the file, apply the transform, guard against double rotation.
- Check the Blazor and API-client playback paths: browsers honour the MP4 matrix natively; note what MKV does.

### Phase 4 — Rotation survives re-creation and swap 🟨

- `ApplyRotation` after `RecreatePlayer`, in `InitializeServices`, and for both tiles after `SwapPlayerWith`.
- Reproduce the swap case first; fix only if it is real.

### Phase 5 — Software-decode path and CPU captures rotate ✅

- `FrameCapture` rotates the CPU fallback frame.
- `CpuFrameUploader` (or the renderer) rotates software frames for live view.
- Server: snapshots become rotated through the CPU path; fix the misleading comment.

**Tests:** `FrameCapture` on a synthetic frame with a marked corner, for each rotation.

### Phase 6 — Thumbnails keep their aspect ratio 🟨

- `ThumbnailGeneratorService` fits each frame into its cell (letterboxed) instead of stretching.

### Phase 7 — Docs ✅

- `docs/settings.md` (rotation row), `docs/motion-detection.md`, `CameraRotation` doc comment, release notes ("recordings made before this version are not rotated").

---

## ✅ Decisions

Resolved with the product owner on 2026-10-12.

| # | Question | Decision |
|---|----------|----------|
| 1 | Flag the rotation (display matrix) or burn it into the pixels? | **Flag it.** No extra CPU, lossless. Burn-in would need a transcoding path the engine does not have. |
| 2 | Is the software-decode path (MJPEG / USB / hardware acceleration off) in this release? | **Yes**, both the CPU capture and the live picture. |
| 3 | Fix up existing recordings? | **No.** They hold no rotation information. |

---

## 📝 Implementation Notes

Where the build differs from, or adds to, the plan above.

- **Sign of the display matrix.** `av_display_rotation_get` reports counter-clockwise degrees and is the negation of what `av_display_rotation_set` is given, so a clockwise camera angle is passed to the setter as-is. 🧪 Confirmed two ways: in-process tests write MP4 and MKV through `Remuxer` at each angle and read it back, and FFmpeg's own decoder turned a corner marker clockwise for the same flag.
- **`MediaElement` ignores the flag.** 🧪 Rendering flagged MP4 and MKV files in a WPF `MediaElement` and sampling a corner marker showed no rotation at any angle, and `NaturalVideoWidth/Height` stay unrotated. So the recording player always applies the rotation itself (a `LayoutTransform` from `MediaProbe.GetRotation`), and no double-rotation guard is needed.
- **End-to-end snapshot check.** 🧪 A marked stream was played through `VideoPlayer` on all three paths — hardware-decoded with GPU, software-decoded with GPU, and no GPU — at 0 / 90 / 180 / 270. All twelve snapshots came back with the marker in the expected corner and the expected dimensions.
- **Recordings from the real pipeline.** 🧪 Recording the marked stream through `VideoPlayer` → `Remuxer` at 90 / 180 / 270, in MP4 and MKV, gave files that `ffprobe` reports as rotation -90 / -180 / 90, and FFmpeg's decoder showed the marker in the top-right / bottom-right / bottom-left corner — clockwise, matching the live picture.
- **Cost of CPU rotation.** Rotating one 1920×1080 BGRA frame takes about 1.6 ms (release build, this machine), so a rotated software-decoded 1080p camera at 30 fps spends roughly 5% of one core on it. Hardware-decoded cameras rotate on the GPU and pay nothing.
- **Checked on a real camera (2026-10-12).** A recording from TAPO-C120-04 (rotated 270°, MKV) plays rotated in the built-in player. **VLC 3.0.24 shows the same MKV unrotated**, and shows an MP4 copy of it (same flag, no re-encode) rotated: VLC 3 applies the flag from MP4 only. So MP4 is the format for rotated cameras whose recordings must look right in VLC.
- **MP4 recordings are crash-safe** *(added 2026-10-12)*. They are written with the header first and the video in fragments (`frag_keyframe+empty_moov+default_base_moof`), so choosing MP4 no longer means losing a recording that is cut short. Checked on a real stream: a file cut at 60% still opens, in FFmpeg and in the WPF player.
- **Shared rotation code.** `PixelRotator` (VideoEngine) rotates packed pixel buffers and is used by both `FrameCapture` (RGB24) and `CpuFrameUploader` (BGRA). `MotionBoxMapper` (Core) owns the box-to-screen mapping.
- **Also fixed:** the full-screen camera window did not rotate a player it created itself. Boxes found before a rotation change are cleared rather than drawn in the old orientation.
- **Rotation in the API** *(added 2026-10-12)*. The camera resource and the create / update requests carry `rotation` (`none`, `rotate90`, `rotate180`, `rotate270`); an update that leaves it out keeps the current value. The server applies the stored rotation to a running pipeline on every connection-service pass, so a change takes effect within one polling interval (30 s): at once for snapshots, from the next segment for recordings. The WPF API client reads and sends it (camera edit dialog) and turns its own player, because the server streams the camera's own pixels. The Blazor camera form has the field too.
- ✅ **The web client's live view rotates** *(2026-10-13)*. The HLS stream is the camera's own video, so the turn happens in the browser: `VideoRotationStyle` gives the `<video>` element a CSS rotation, and for a quarter turn sizes it from the tile (container query units) so the picture still fits. Checked in a browser with live test streams at 90° and 180°. The browser's own video controls are left out for a rotated camera, since they would turn too.
- **Recordings in a browser:** an MP4 carries the rotation and the browser applies it. An MKV usually does not play in a browser at all; the web client says so and offers the download.
- **Thumbnails.** Only portrait frames are letterboxed; landscape frames fill the cell as before, so existing thumbnails look the same.

### The 180° report is not explained

The reporter sees misplaced boxes at 180° too. The cause found (wrong aspect ratio) only bites at 90° / 270°, and the end-to-end check shows hardware-path snapshots are correctly rotated at 180°. Two fixed defects could have produced it, but neither is proven to be what the reporter saw:

- A **tile swap** left the two streams with each other's rotation, so the picture and the analysis could disagree with the setting.
- Whenever the GPU snapshot failed, the **CPU fallback returned an unrotated frame**. Frame differencing between a rotated and an unrotated frame produces boxes in the wrong place.
- A **full-screen window that created its own player** showed the picture unrotated, while its boxes came from the tile's rotated analysis. At 180° that looks exactly like "boxes mirrored through the centre". This only applies if the boxes were seen in full screen.

If boxes are still off at 180° after this change, capture: grid or full screen, the camera's codec and whether hardware acceleration is on, whether tiles were swapped, and a snapshot taken at the same moment.

---

## ✅ To Verify by Hand

- Boxes line up at 0 / 90 / 180 / 270 in the grid and in full screen, including after resizing the window.
- A new recording from a rotated camera plays rotated in the app, VLC, Windows Media Player and a browser — for both MKV and MP4. No double rotation.
- Rotation survives changing a performance setting, and swapping two tiles with different rotations.
- Snapshots and thumbnails are upright and not squashed.
