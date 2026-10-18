# 🎞️ FFmpeg 9 Upgrade Roadmap

Plan for moving the video engine from **FFmpeg 8.1** to **FFmpeg 9.0**, which means moving `Flyleaf.FFmpeg.Bindings` from **8.0.1** to **9.0.2**. The two cannot be done separately: the bindings refuse to load libraries of another major version.

Affects **`AtcSoft.VideoEngine`** and **`AtcSoft.VideoEngine.DirectX`** (code), the **release pipeline** and **README** (which FFmpeg is downloaded and documented), and every machine that runs from source.

← Back to the [roadmap home](roadmap.md)

**Status: 🟨 Built, awaiting hands-on checks** — picked up again and done in code on 2026-10-18: the engine runs on FFmpeg 9.0.2, and the apps and the server find, report and install it themselves. What is left is phase 5 by hand and the per-camera TLS setting.

---

## 🗂️ Status Legend

| Emoji | Meaning |
|-------|---------|
| ⬜ | Not started |
| 🟨 | In progress |
| ✅ | Complete |
| ⏸️ | Deferred (intentionally not in current scope) |

---

## 🔍 What Was Tried

On 2026-10-13 the bindings were bumped to 9.0.2 in a trial and reverted again.

| Step | Result |
|---|---|
| Compile against 9.0.2 | 4 errors, all the same kind: `sws_scale` now takes `byte**` / `int*` instead of managed arrays. Twelve array declarations in three files (`FrameCapture`, `GpuSnapshotCapture`, `CpuFrameUploader`) become `stackalloc`. With that, the whole solution builds with no warnings. |
| Start the engine | **Fails:** `Library avutil.61* not found`. The 9.x bindings need FFmpeg 9 libraries (`avutil-61`); the installed and shipped FFmpeg is 8.1.2 (`avutil-60`). |
| Engine tests | They **skip** when FFmpeg does not load, so the test run stays green. A green run is therefore no proof that the engine works after a bindings change. |

The bindings' own notes for 9.x: generated array wrappers are replaced with generic inline arrays, and `AVRational.Default` is renamed to `Zero` (not used in this codebase).

**A smaller step exists:** bindings **8.1.0** target FFmpeg 8.1, which is what is installed today. Not tried.

---

## 🎁 What FFmpeg 9 Would Give This Project

Read from the FFmpeg 9.0 `Changelog`, `doc/APIchanges` and source on the `release/9.0` branch. Nothing here has been tried in the engine.

### Useful

| What | Why it matters here |
|---|---|
| **RTSP `SET_PARAMETER` from the application** (`avformat_send_command`, new demuxer command API) | The first way to send an RTSP command to a camera on an open stream. Candidate uses: an explicit keep-alive for cameras that drop idle sessions, and vendor commands some cameras accept this way. Worth a spike before promising anything. |
| **Staying on a maintained line** | The engine parses data straight from network devices, and FFmpeg point releases are mostly decoder, demuxer and protocol hardening. The 8.1 branch is still getting those fixes today (see below), so this is a reason to upgrade eventually, not now. |
| **ONNX Runtime backend for FFmpeg's DNN filters, with GPU execution** | A possible route to the object detection listed in [suggestions](roadmap-suggestions.md): run a detection model in-process without a separate inference stack. The engine does not use `libavfilter` today, so this is a larger design question, not a free feature. |
| **swscale: selectable scaler and backend** (`SwsScaler`, `SwsBackend`) | The CPU path (MJPEG / USB cameras, snapshots) converts every frame with swscale. Newer backends may be faster; needs measuring. |

### Fixes that do **not** need FFmpeg 9

Several fixes that matter here are in the FFmpeg 9.0.x point releases **and** were backported to **8.1.3**, the next point release of the line in use (this project ships 8.1.2):

- **Crash in the MP4 muxer when flushing a fragment with no data.** Recordings are now written as fragmented MP4; a camera that stalls right at a segment boundary is the case this covers.
- **HEVC:** no longer errors out on the remaining slices of a skipped picture.
- **HTTP:** only reuses an old connection when it is still valid; cookie parsing fix.
- **MPEG-TS:** memory leak fix.

Moving from 8.1.2 to 8.1.3 needs **no code or bindings change** (same library versions) — only the download in the release pipeline, the README, and the DLLs on developer machines. On 2026-10-13 gyan.dev had no 8.1.3 build yet; BtbN publishes builds of the 8.1 branch. This is the cheap step to take first (Decision 4).

### Already available without upgrading

These arrived in **FFmpeg 8.1**, which is what runs today:

- `hxvs` demuxer for the HXVS / HXVT format used by some IP cameras.
- `BindFlags` / `MiscFlags` on the D3D11VA device context (finer control over decoder textures).
- D3D12 H.264 / AV1 encoders and D3D12 scaling and deinterlacing filters — relevant only once the engine transcodes.

### Not relevant

Most of the 9.0 headline list does nothing for a surveillance recorder: AMF colour and frame-rate filters, `transpose_cuda`, `v360_vulkan`, animated WebP, HE-AAC 960 (DAB+), Playdate video, ProRes RAW and APV hardware decoding, SMPTE 2094-50 HDR metadata, LCEVC muxing.

---

## ⚠️ What Changes Behaviour

| Change | Effect here | What to do |
|---|---|---|
| **TLS peer verification is on by default** (`tls_verify` default changed from off to on) | An **HTTPS or RTSPS camera with a self-signed certificate will stop connecting** after the upgrade. Most cameras that offer TLS use a self-signed certificate. | Add a per-camera (or app-level) setting and pass `tls_verify` to the demuxer explicitly, so the behaviour is a choice instead of an accident. Decide the default (Decision 1). |
| **Library major versions bump** (`avutil-61`, …) | FFmpeg 8 libraries no longer load. Every place that provides FFmpeg has to change together. | See the phases. |
| **CELT decoding removed** (Opus is not affected) | None expected: cameras here send H.264 / HEVC / MJPEG, and audio is not recorded yet. | Re-check when USB audio capture is finished. |
| **Deprecated NVENC options removed; NVENC needs SDK 11.1+** | None: the engine does not encode. | Nothing. |

---

## 🛣️ Phases

| # | Phase | Status |
|---|-------|--------|
| 1 | Make a failed FFmpeg load visible in tests | ✅ |
| 2 | TLS verification as an explicit setting | 🟨 off engine-wide; the per-camera setting is open |
| 3 | Bindings 9.0.2 and the code adaptation | ✅ |
| 4 | FFmpeg 9 everywhere it is provided | ✅ |
| 4b | Find, report and install FFmpeg (set-up dialog, server) | ✅ |
| 5 | Re-verify the engine against FFmpeg 9 | 🟨 automated part done |

### Done on 2026-10-18

- **Bindings 9.0.2**, `sws_scale` arrays as `stackalloc` (four files).
- **The engine knows which FFmpeg it needs.** `FFmpegRuntime.Expected` comes from the bindings' `LIB*_VERSION_MAJOR` constants. `FFmpegRuntime.Check` reads the library versions from the file names in each folder it looks in and says Ready, WrongVersion (with every folder and version found) or Missing. `VideoEngineBootstrap.Initialize` throws `FFmpegNotReadyException` with that description instead of the loader's "avutil.61 not found".
- **An installer** (`FFmpegInstaller`): the pinned gyan.dev 9.0.2 shared build from its GitHub mirror, checked against its SHA-256, only its `bin` folder, the versions checked, then put in `%ProgramData%\AtcSoft\ffmpeg\9.0.2\bin`. A failed install leaves the target as it was; an install in use (the server runs from it) fails before anything changes; one another app just finished is kept.
- **The set-up dialog** (`FFmpegSetup`, `FFmpegSetupDialog` in Wpf.Core), shown by CameraWall and the WPF client before the engine starts when the check is not Ready: what is needed, what was found, *Install FFmpeg* with progress and cancel, *Use this folder* (only accepted when it holds FFmpeg 9; remembered for the machine in `%ProgramData%\AtcSoft\ffmpeg\folder.txt`), the download page, and *Exit*.
- **The server** logs what is needed and exits with code 1, or installs first when started with `--install-ffmpeg` (`ServerFFmpegStartup`). Its live streams use `ffmpeg.exe` from the folder the engine loaded.
- **TLS**: `tls_verify=0` unless `StreamOptions.VerifyTlsCertificate`; the server's HLS command passes it for HTTPS cameras. Checked against an HTTPS stream with a self-signed certificate served by the FFmpeg 9 tool: it opens by default and is refused with verification on (`DemuxerTlsTests`).
- **Release pipeline** downloads the same pinned zip, checks the same SHA-256, and ships `ffmpeg.exe` too. README and package READMEs updated.
- **Tests**: all unit, host and engine suites pass on FFmpeg 9.0.2 with nothing skipped (the engine tests decode and record a live test stream); `FFmpegSetupSmokeTests` walks the dialog in both apps (`ATCSOFT_FFMPEG_SEARCH=explicit` hides the installed FFmpeg).

Three places name the pinned build and must change together: `FFmpegRuntime.ReleaseVersion`, `FFmpegInstaller.Package` (URL and SHA-256) and `release-please.yml`.
| 6 | Use what is new | ⏸️ |

### Phase 1 — Make a failed FFmpeg load visible in tests ✅

Done 2026-10-13. `VideoEngineBootstrap.FindFFmpegPath` tells "not installed" from "installed"; the engine tests skip in the first case and **fail** with the loader's message in the second, and print the FFmpeg version they ran against. The API host tests, which need the engine to start the server, skip the same way.

### Phase 2 — TLS verification as an explicit setting 🟨

Do this **before** the upgrade, on FFmpeg 8, so the upgrade does not change behaviour.

- A setting for verifying a camera's certificate, passed to the demuxer as `tls_verify` for HTTPS and RTSPS sources.
- End to end like any other setting: Core model, both settings dialogs, the API contract, docs.

### Phase 3 — Bindings 9.0.2 and the code adaptation ✅

- `Flyleaf.FFmpeg.Bindings` 8.0.1 → 9.0.2.
- The twelve `sws_scale` array declarations become `stackalloc` (the trial did this with a mechanical edit; it built cleanly).

### Phase 4 — FFmpeg 9 everywhere it is provided ✅

- **Release pipeline** (`release-please.yml`): the download URL is pinned to `ffmpeg-8.1.2-full_build-shared.7z`. FFmpeg 9.0.2 shared builds exist at gyan.dev and BtbN.
- **README** and the NuGet package READMEs: supported version, download links, DLL names (`avutil-61.dll`, …), the install snippet.
- **`VideoEngineBootstrap`**: check the error message a wrong version gives the user.
- **Developer machines**: `%LOCALAPPDATA%\ffmpeg\bin` has to be replaced, or the apps fail at startup.

### Phase 5 — Re-verify the engine against FFmpeg 9 🟨

The engine tests cover only part of this; the rest is by hand or with a marked test stream fed to the player over UDP by the FFmpeg command-line tool.

- Live view: hardware decode, software decode, no GPU.
- Recording in MKV and MP4: files play, survive being cut short, carry the rotation flag.
- Rotation at 90 / 180 / 270 on all three paths.
- Snapshots, thumbnails, motion detection.
- USB cameras (dshow).
- An HTTPS camera with a self-signed certificate, with the Phase 2 setting on and off.

### Phase 6 — Use what is new ⏸️

Only after the upgrade is in and stable; each is its own piece of work.

- Spike: RTSP `SET_PARAMETER` as keep-alive.
- Measure: swscale backends on the software-decode path.
- Design: ONNX Runtime detection through FFmpeg, as one option for the AI detection backlog item.

---

## ❓ Decisions Needed (when the work is picked up)

| # | Question | Recommendation |
|---|----------|----------------|
| 1 | **Default for certificate verification?** Off keeps every camera working as today; on is safer but breaks self-signed cameras until someone switches it off per camera. | **Off by default, per camera**, with the setting visible in the camera dialog. A camera on a private LAN with a self-signed certificate is the normal case. *2026-10-18: off for every camera (`StreamOptions.VerifyTlsCertificate`); the per-camera setting in the dialogs and the API is still to do.* |
| 2 | **Go to bindings 8.1.0 first?** It matches the FFmpeg already in use and carries less risk. | *Skipped: went to 9.0.2 directly on 2026-10-18.* |
| 3 | **Support FFmpeg 8 and 9 side by side for a while?** | **No.** The bindings accept exactly one major version; a clean switch in one release is simpler than two code paths. |
| 4 | **Take FFmpeg 8.1.3 now?** | *Superseded by 9.0.2, which has the same fixes.* |

---

## 📚 Sources

- FFmpeg `Changelog`, `doc/APIchanges`, `libavformat/tls.h` and `libavformat/avformat.h` on the `release/9.0` branch (`RELEASE` = 9.0.2), compared with the `Changelog` and `tls.h` on `release/8.1` (which lists 8.1.3).
- `Flyleaf.FFmpeg.Bindings` 8.1.0, 9.0.0 and 9.0.2 package release notes on nuget.org.
- The trial described under *What Was Tried*.
