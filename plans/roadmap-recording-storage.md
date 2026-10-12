# 📁 Recording Storage Layout Roadmap

Plan for making the recordings folder manageable after months of continuous recording. Today every camera has **one flat folder** holding all its videos and thumbnails: 100 days × 15-minute segments is ~9,600 videos + ~9,600 thumbnails ≈ **19,000 files in one folder**, which Windows Explorer handles badly.

Two new, independent options:

1. **Thumbnails in their own folder**, separate from the videos.
2. **A sub-folder per day.**

Affects **`AtcSoft.VideoSurveillance.Core`** (new path helper, settings, cleanup), **`AtcSoft.CameraWall.Wpf`** and **`AtcSoft.VideoSurveillance.Api`** (the two recording services), **`AtcSoft.VideoSurveillance.Wpf.Core`** (recordings browser, settings UI), and the **API contract + both API clients** (settings only).

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

## 📐 Layouts

Camera `Front Door`, recorded on 12 October 2026.

**Today (and the default after this change):**

```
recordings\
└── Front Door\
    ├── Front Door_20261012_101500_000.mkv
    ├── Front Door_20261012_101500_000.png      ← thumbnail
    └── … every day, every segment, both file types
```

**Day folders on:**

```
recordings\
└── Front Door\
    ├── 2026-10-11\
    └── 2026-10-12\
        ├── Front Door_20261012_101500_000.mkv
        └── Front Door_20261012_101500_000.png
```

**Separate thumbnails on:**

```
recordings\
└── Front Door\
    ├── Front Door_20261012_101500_000.mkv
    └── thumbnails\
        └── Front Door_20261012_101500_000.png
```

**Both on (recommended for long retention):**

```
recordings\
└── Front Door\
    ├── 2026-10-12\
    │   └── Front Door_20261012_101500_000.mkv     ← 96 files per day at 15 min
    └── thumbnails\
        └── 2026-10-12\
            └── Front Door_20261012_101500_000.png
```

File names do not change, so a file is still self-describing when copied elsewhere.

---

## ⚙️ Settings

New members on `RecordingSettings` (app-level, both off by default so nothing changes on upgrade):

| Property | Type | Default | Effect |
|---|---|---|---|
| `UseDailySubfolders` | bool | `false` | Videos (and thumbnails) go into a `yyyy-MM-dd` folder |
| `SeparateThumbnailFolder` | bool | `false` | Thumbnails go into a `thumbnails` folder under the camera folder |

Not per-camera overridable (Decision 4): the layout is a property of the archive, and the browser and cleanup read the whole tree.

---

## 🔍 What Has to Change (Findings)

Traced through the code. The layout is assumed in far more places than the two recording services.

### No shared path logic exists

| Logic | Copies today |
|---|---|
| Camera-folder name sanitising | **5** — `RecordingService`, `TimelapseService`, `ServerRecordingService`, `ListRecordingsHandler`, `CaptureSnapshotHandler` (two different algorithms; only the server one has a fallback for an empty name) |
| Recording file path | **2** — `RecordingService.GenerateRecordingFilename`, `ServerRecordingService.GenerateRecordingFilename` |
| Video → thumbnail path | **8** — every one is `Path.ChangeExtension(video, ".png")`: the generator, both `RecordingEntry` models, two reclaim skip-sets, the cleanup skip-set, and two places in `MediaCleanupRunner` |

### Both recording listers break on a deeper tree

- **Standalone browser** (`RecordingsBrowserDialogViewModel`): enumerates exactly two levels (`camera folder → files`). With day folders it would find **nothing**.
- **API lister** (`ListRecordingsHandler`): recursive, but takes the camera from the file's **immediate parent folder**. With day folders every recording would resolve to no camera.

### Thumbnails assume "same folder, same name"

The generator's contract takes only the video path. The browser's delete, cleanup's companion delete and the active-file skip-sets all derive the thumbnail from the video path. A separate folder must be resolved in one place.

### Cleanup

- `MediaCleanupRunner` is already recursive and already removes empty folders — but **only after age-based cleanup**. The four disk-space reclaim call sites never remove empty folders, so empty day folders would pile up when retention is driven by the disk-space guard. Deleting from the browser leaves them too.
- Thumbnails outside the recording root would never be cleaned, and under the snapshot path they would be deleted by snapshot retention. This is why the thumbnail folder stays **inside the camera folder** (Decision 2).

### Existing bugs in the same code (fix here)

- 🐛 **The recording timestamp is never parsed from the file name.** File names end in `_HHmmss_fff` (milliseconds were added later), but the browser's regex and `FullScreenRecordingWindowViewModel.ParseRecordingStartTime` both expect `_HHmmss` directly before the extension. The browser silently falls back to the file's creation time; the playback overlay falls back to the playback position. Collision-suffixed names (`…_2.mkv`) fail the same way.
- 🐛 **Different clocks.** The standalone app names files in **local** time, the server in **UTC**. A day folder needs one answer (Decision 3).
- 🐛 `ListRecordingsHandler` builds its camera lookup with `ToDictionary`, which **throws** if two cameras sanitise to the same folder name.

### Out of scope (noted, not fixed here)

- The server never generates thumbnails (`ListRecordingsHandler` hard-codes `hasThumbnail: false`, and there is no thumbnail endpoint). The thumbnail option therefore only has a visible effect in the standalone CameraWall app for now.
- Browsing and cleanup only cover the global recording path; per-camera path overrides are never browsed or cleaned.
- The disk-space-guard settings and `TranscodeVideoCodec` never reached the API contract. Tracked in [suggestions](roadmap-suggestions.md).

---

## 🧩 Design

### One path helper in Core

`RecordingPathHelper` (name open) is the single owner of the layout:

```csharp
public static class RecordingPathHelper
{
    // "Front Door" → "Front Door"; invalid chars → '_'; empty → short camera id
    public static string GetCameraFolderName(string? displayName, Guid cameraId);

    // {root}\{camera}\[{yyyy-MM-dd}\]{camera}_{yyyyMMdd_HHmmss_fff}.{ext}
    public static string BuildRecordingPath(string root, string cameraFolder, DateTime timestamp, string format, RecordingLayoutOptions layout);

    // Where the thumbnail for this video is WRITTEN under the current layout
    public static string BuildThumbnailPath(string videoPath, string root, RecordingLayoutOptions layout);

    // Where the thumbnail for this video can be FOUND: tries each known location, old and new
    public static string? FindThumbnailPath(string videoPath, string root);

    // The camera folder a file belongs to = first path segment below the root
    public static string? GetCameraFolderName(string filePath, string root);

    // Timestamp from the file name, tolerating _fff and collision suffixes
    public static bool TryParseTimestamp(string fileName, out DateTime timestamp);
}
```

All five sanitiser copies, both file-name builders and all eight `ChangeExtension` sites are replaced by calls to it.

### Reading is layout-agnostic

Readers never look at the settings. They walk the camera folder recursively, skip the `thumbnails` folder, take the camera from the **first folder below the root**, and find a thumbnail by trying each known location. So:

- **No migration.** Old flat files, day-folder files and any mix of the two are listed, played, thumbnailed and cleaned side by side.
- Switching an option on or off only affects files recorded afterwards.

### Day folder rollover

The day folder is computed when a file is opened. With segmentation on, midnight is already a segment boundary (`RecordingSlotCalculator`), so each file lands in its own day. With segmentation off, a file stays in the folder of the day it **started**.

### Empty folders

`RemoveEmptyDirectoriesBelow` runs after disk-space reclaim too, and after a delete from the browser.

---

## 🛣️ Phases

Test-first: each phase starts with failing tests in `AtcSoft.VideoSurveillance.Core.Tests`.

| # | Phase | Status |
|---|-------|--------|
| 1 | Core path helper (no behaviour change) | ⬜ |
| 2 | Readers become layout-agnostic | ⬜ |
| 3 | Layout options in Core + both recording services | ⬜ |
| 4 | Cleanup and empty folders | ⬜ |
| 5 | Settings UI (standalone app) | ⬜ |
| 6 | API contract, API-client app and Blazor | ⬜ |
| 7 | Docs | ⬜ |

### Phase 1 — Core path helper (no behaviour change) ⬜

- `RecordingPathHelper` + `RecordingLayoutOptions`.
- Replace the duplicated sanitisers, file-name builders and thumbnail derivations. With both options off, every path is byte-for-byte what it is today.

**Tests:** sanitising (invalid chars, empty name fallback); path for each of the four layouts; thumbnail path for each layout; timestamp parsing for `_HHmmss`, `_HHmmss_fff` and `_fff_2` names; camera folder from a nested path.

### Phase 2 — Readers become layout-agnostic ⬜

- `RecordingsBrowserDialogViewModel`: recursive enumeration, camera from the first segment, timestamp via the helper, thumbnail via `FindThumbnailPath`, delete removes the thumbnail wherever it is.
- Both `RecordingEntry` models: thumbnail path resolved through the helper.
- `ListRecordingsHandler`: camera from the first segment; duplicate-name-safe lookup.
- `FullScreenRecordingWindowViewModel.ParseRecordingStartTime` → helper.

**Tests:** a temp tree mixing flat, day-folder and separate-thumbnail files lists every recording with the right camera, time and thumbnail.

### Phase 3 — Layout options in Core + both recording services ⬜

- `RecordingSettings.UseDailySubfolders` / `SeparateThumbnailFolder`.
- `RecordingService` and `ServerRecordingService` pass the layout to the helper.
- `ThumbnailGeneratorService` writes to `BuildThumbnailPath`.

### Phase 4 — Cleanup and empty folders ⬜

- `MediaCleanupRunner`: companion thumbnail via the helper; orphan thumbnails (video gone) are removed.
- Remove empty folders after reclaim and after a browser delete.
- Skip-sets use the helper.

**Tests:** age cleanup and reclaim on each layout delete the video **and** its thumbnail and leave no empty day folder.

### Phase 5 — Settings UI (standalone app) ⬜

- Two toggles in `Dialogs/Parts/Settings/CaptureRecordingSettings.xaml`, `SettingsDialogViewModel` (property, defaults, load, save), translations (en / da / de).

### Phase 6 — API contract, API-client app and Blazor ⬜

- `VideoSurveillance.yaml` settings schema, `SettingsMappingExtensions`, `ApiApplicationSettingsService`, the API-client `SettingsDialogViewModel`, Blazor `SettingsState` + `RecordingTab.razor`. Follows the same end-to-end path as the connection-type icon setting.
- Clients build playback URLs from the relative path the server returns, so nested folders work; **URL-encode** the path segments (today they are concatenated raw).

### Phase 7 — Docs ⬜

- `docs/settings.md` (settings table, paths table, JSON example), `docs/architecture.md`, `docs/disk-space-guard.md`, `README.md`, `CLAUDE.md` settings snippet.

---

## ❓ Decisions Needed

| # | Question | Recommendation |
|---|----------|----------------|
| 1 | **Day-folder shape:** `2026-10-12\` (flat list of days) or `2026\10\12\` (nested)? | **`2026-10-12`**. Sorts correctly, one click to a day. A year is 365 folders per camera, which Explorer handles easily. |
| 2 | **Where do thumbnails go?** Inside the camera folder (`Front Door\thumbnails\…`) or a separate root (`thumbnails\Front Door\…`, or a freely chosen path)? | **Inside the camera folder.** Everything for a camera stays together, the existing web file mount and cleanup keep working, and moving or deleting a camera's archive stays one folder. |
| 3 | **Which clock names the day folder on the server?** The standalone app uses local time, the server UTC. | **Local time for the folder** in both — people browse by their own calendar day. Leave the server's file-name timestamp as it is to avoid changing existing names. |
| 4 | **Per-camera override for the layout?** | **No.** App-level only. |
| 5 | **Move existing recordings into the new layout?** | **No.** Old and new layouts are read side by side; the old files age out through retention. A one-off "reorganise" action can be added later if wanted. |
| 6 | **Defaults:** off (nothing changes on upgrade) or on for new installs? | **Off.** Opt-in, as requested. |

---

## ✅ To Verify by Hand

- Each of the four layouts: record, see the files where expected, browse them, play them, see the thumbnail, delete one.
- Turn the options on with an existing flat archive: old and new recordings both appear.
- Midnight rollover with segmentation on.
- Disk-space reclaim and age cleanup leave no empty day folders or orphan thumbnails.
- API-client app and Blazor play a recording from a day folder (and a camera name with a space).
