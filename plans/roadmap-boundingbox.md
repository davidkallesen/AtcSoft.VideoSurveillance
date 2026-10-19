# 🟥 Bounding Boxes and Activity Timeline Roadmap

Plan for making motion detection find **one box per moving object**, **no box for a tree in the wind**, and for showing **where in a recording something happened**. A sample app and a scored test harness come first, so every later change is measured against a stream whose content we know.

Affects **`AtcSoft.VideoSurveillance.Core`** (the detector), **`AtcSoft.VideoEngine`** (frames for analysis), both motion services, the overlays in **both WPF apps** and the **web client**, recordings (a new activity file), the **server** (a timeline of every camera across its recordings), the **API spec**, and a new **`sample/BoundingBoxWpfApp`**.

← Back to the [roadmap home](roadmap.md)

**Status: ⬜ Not started** — planned on 2026-10-18.

---

## 🗂️ Status Legend

| Emoji | Meaning |
|-------|---------|
| ⬜ | Not started |
| 🟨 | In progress |
| ✅ | Complete |
| ⏸️ | Deferred (intentionally not in current scope) |

Sizes: **S** an hour or two, **M** a day, **L** several days.

---

## 🎯 Goal

| What | Done when |
|---|---|
| One box per moving object | The storybook scene *car + human + cat* gives exactly three tracks, one box each, for the whole time they move |
| No boxes for things that only sway | The scene *tree in the wind* gives no box and no motion event |
| Small and slow objects are found | A small cat far away and a slow walker are boxed within 0.5 s |
| Boxes are drawn where the object is | The FlaUI test reads the drawn boxes and they match the truth, also at 90 / 180 / 270° |
| Every recording shows its activity | A 60 min recording with a person for 5 s at minute 48 shows a visible mark there, in both WPF apps and the web client |
| The server has a timeline of every camera | One view shows all cameras over a day (or a week), with where recordings exist, where the gaps are and where something happened; a click plays from that moment, across segment boundaries; it grows live |
| Real recordings become tests | A recording plus the user's verdicts (correct / false box / missed) runs as a regression test |
| Later: what the box is | A local ONNX model labels a box *person*, *car*, *cat* or *dog* (⏸️) |

---

## 🔍 Today's Problems (with evidence)

Checked against the code on 2026-10-18.

### How it works today

| Step | Where |
|---|---|
| The player encodes the whole frame (full resolution) as **PNG**, on a pool thread | `VideoPlayer.CaptureFrameAsync`, `src/AtcSoft.VideoEngine/VideoPlayer.cs:539-588` |
| The PNG is decoded again with GDI+, scaled to 800×600 **without keeping the aspect ratio**, and made grey | CameraWall: its own copy, `MotionDetectionService.cs:371-421`; server: `GdiMotionFrameReader.cs:25-28` |
| The new grey picture is compared with **the previous analysed picture only** | `MotionDetectionService.cs:297,347`; `ServerMotionDetectionService.cs:248-255` |
| Pixel threshold 15–35 from *Sensitivity*; 20×20 cells; a cell is active with ≥ 33 changed pixels; 8-connected cells form a box | `MotionFrameAnalyzer.cs:41,45,80,88-116` |
| Box padding, sort by area | `MotionFrameAnalyzer.cs:145-156` |
| Motion is "active" when the **whole picture's** changed share ≥ `MinimumChangePercent` (0.5 %) | `MotionDetectionService.cs:307-308`; `ServerMotionDetectionService.cs:257` |
| WPF draws at most 10 boxes, smoothed per **list position** | `MotionBoundingBoxOverlay.xaml.cs:16,139-180` |
| Web draws every box, no smoothing | `LiveView.razor:169-172` |

### Why there are too many boxes

| Cause | Evidence | Effect |
|---|---|---|
| **Two-frame difference, no background model** | `MotionFrameAnalyzer.Analyze(previous, current…)`; previous = last analysed frame (`MotionDetectionService.cs:347`) | A moving object changes only at its **leading and trailing edges**; a plain-coloured inside does not change. One person becomes two or three clusters. A fast object leaves a "ghost" box where it was |
| **No morphology, no blob merge** | Only cell counting and flood fill (`MotionFrameAnalyzer.cs:53-116`); padding without merging (`:145-152`) | Fragments 1 cell apart stay separate boxes; padded boxes overlap and are still drawn twice |
| **Foliage lights scattered cells** | One fixed threshold for every pixel (`:41`), no per-pixel variance | Leaves cross the threshold every frame; each cluster of leaves is a box — the "10+ boxes for a tree" |
| **No persistence, no tracking** | Boxes go straight from one comparison to the event (`MotionDetectionService.cs:314-324`) | Something there for one frame gets a box; nothing tells a swaying branch (moves back and forth on the spot) from a person (travels) |
| **`MinArea` does nothing** | Default 10 px² (`BoundingBoxSettings.cs:16`), but the smallest cluster is one cell = 400 px² (`MotionFrameAnalyzer.cs:45,137-143`) | The setting meant to filter noise never filters |
| **The 10-box limit is in the renderer** | `MaxBoundingBoxes = 10` (`MotionBoundingBoxOverlay.xaml.cs:16`); none in the web client | The detector reports any number; the cap hides it in WPF only |
| **No handling of a lighting change** | Nothing checks for a whole-picture change | Auto exposure, a cloud or the IR switch at dusk becomes one huge box or many |

### Why detection sometimes misses

| Cause | Evidence | Effect |
|---|---|---|
| **Global gate on the share of changed pixels** | `changePercent >= MinimumChangePercent` (0.5 %) over the whole picture (`MotionDetectionService.cs:308`, `ServerMotionDetectionService.cs:257`) | 0.5 % of 800×600 is 2 400 pixels. A small cat or a far person changes fewer (edges only), so **no motion and no box even when a cluster was found** |
| **Slow movers** | Two-frame difference at the analysis interval | A person walking slowly changes little between two pictures; at CameraWall's up to 15/s the difference is smallest |
| **Noise counts toward the gate** | Scattered pixels count in `totalChangedPixels` (`MotionFrameAnalyzer.cs:66,76`); pinned by the test `Scattered_Noise_Counts_As_Change_But_Gets_No_Box` | Motion can be "active" with **no box**, and a real object competes with noise for the same percentage |
| **The server only watches some cameras** | `ShouldWatchForMotion` = record-on-motion (`CameraConnectionService.cs:377-378`) | A camera that records continuously has no motion data at all — and so no activity timeline |

### Why the boxes jump or flicker

| Cause | Evidence | Effect |
|---|---|---|
| **Smoothing by list index while the list is re-sorted by area** | `smoothedBoxes[i]` (`MotionBoundingBoxOverlay.xaml.cs:150`) vs `boundingBoxes.Sort(...)` (`MotionFrameAnalyzer.cs:156`) | When the car's box becomes smaller than the person's, box 0 slides from the car to the person |
| **CameraWall ends motion on the first quiet frame** | `context.IsMotionDetected = false` (`MotionDetectionService.cs:341`); the tile hides the boxes at once (`CameraTile.xaml.cs:2640`) | Boxes blink on and off, while the server holds motion for `PostMotionDurationSeconds` (`ServerMotionDetectionService.cs:270-271`) |
| **The two services disagree** | CameraWall 2–15/s round robin with skips (`MotionDetectionService.cs:13-14,233-237`); server ≤ 4/s (`ServerMotionDetectionService.cs:21`); different fallback sizes (320×240 vs the settings' 800×600) | The same scene scores differently on the desktop and the server, because a frame difference depends on the interval |
| **Aspect ratio lost** | 16:9 or a portrait (rotated) picture squeezed into 800×600 (`MotionDetectionService.cs:377`) | Cells are not square in the real picture; box sizes differ by direction |

### Smaller findings

- `docs/motion-detection.md:98` says `AnalysisFrameRate` defaults to 30; the code says 15 (`MotionDetectionSettings.cs:12`).
- CameraWall has its own grey conversion; the server uses `GdiMotionFrameReader`. The same code twice.
- `CameraTile.UpdateMotionBoundingBoxes` defaults to 320×240 (`CameraTile.xaml.cs:2796-2799`). Harmless today (only used with no boxes), but a trap.
- The known bug *boxes misplaced at 180°* ([roadmap](roadmap.md#-known-bugs)) has no automated test. A storybook can carry a rotation and become that test.

---

## 🧭 Design

### The pipeline after this plan

| Step | What | Why |
|---|---|---|
| 1 | **Grey frame with its time** from the engine (Y plane, scaled by swscale, plus the frame's pts) | No PNG round trip; frames can be matched to the truth by time |
| 2 | Scale keeping the aspect ratio (width given, height follows); a light blur (3×3 or 5×5) | Square cells; sensor noise and compression blocks removed |
| 3 | **Global change check**: if more than ~60 % of pixels change, or the mean brightness jumps, re-learn the background and suppress boxes for a moment | Lighting, IR switch, a camera re-exposing |
| 4 | **Background model** per pixel: running mean and variance (a one-Gaussian model; MOG2-like with 2–3 modes only if the scores need it) | A slow mover differs from the background, not from the last frame; swaying leaves get a high variance and stop triggering |
| 5 | Foreground = \|pixel − mean\| > k·σ (with a minimum); foreground pixels are learnt slowly | One whole silhouette instead of two edges |
| 6 | **Morphology**: open (remove specks), then close (fill holes) | No speckle boxes; one blob per object |
| 7 | **Connected components** on the pixel mask (two-pass union–find) | Exact blobs; no 20 px quantisation |
| 8 | **Filter**: minimum area as **% of the picture**, masks and zones, aspect limits | Settings that mean the same at any resolution |
| 9 | **Merge**: blobs closer than a gap, or overlapping after padding, become one box | One box per object |
| 10 | **Tracker**: match boxes to tracks by IoU or centre distance with a constant-velocity prediction (SORT-like; Kalman optional). A track is **confirmed** after N hits *and* a minimum net travel; it ends after M misses | Persistence; the tree never travels, a person does; stable ids |
| 11 | **Output**: confirmed tracks only, each with a track id | Motion is "active" while a confirmed track exists — not on a global percentage |

All of this is **one stateful class in Core** (`MotionDetector`, one per camera). `MotionDetectionService` (CameraWall), `ServerMotionDetectionService` and the sample app only feed it frames and pass on what it returns. Same code, same results everywhere.

### Activity index (for the timeline)

| Part | Design |
|---|---|
| What is stored | Per second: activity level (largest foreground share, 0–255), number of confirmed tracks, flags (lighting change, masked). Per track: id, first and last time, largest box; later label and score |
| Where | A sidecar next to each recording segment (`….mp4` → `….activity.jsonl`). Its path comes from **`RecordingPathHelper`** (`BuildActivityPath` / `FindActivityPath`); `RecordingLayoutReorganizer.Synchronize` moves it with the video |
| Format | JSON Lines: a header line `{"v":1,…}`, then one line per second, appended while recording; readable while `IsBeingWritten` |
| Size | About 3 600 short lines per hour (~100 KB) |
| Written by | Both recording services, from the detector's output. Detection must therefore run for **every camera that records**, not only for record-on-motion (`CameraConnectionService.cs:377`) |
| Read by | `GET /recordings/activity?filePath=…&buckets=…&durationSeconds=…` (recordings are named by their path, as for delete; all three roles; no cache, the file grows) for the clients; CameraWall reads the file directly |
| Old recordings | An *Analyse recording* action (the sample app's offline path, reused) writes the index afterwards |
| Deleting | A deleted recording takes its sidecar with it. The clean-up code belongs to the disk-space work in progress: a dependency to agree, not a change made by this plan |

### Activity timeline (UI)

| Element | Design |
|---|---|
| Activity bars | A strip under the player; bar height = activity level. **Downsample with max, never mean**: at 60 min on 1 000 px each pixel is 3.6 s, and averaging erases a 5 s event |
| Event bands | Merged active periods (with pre/post padding) as coloured bands, at least 3 px wide, so a 5 s event in an hour is always visible |
| Colours | One colour for motion now; per class later (person, vehicle, animal), always **with an icon** too (never colour alone). Grey for ignored (lighting change, masked) |
| Hover | Time, track count, label, and a thumbnail (the event's peak frame, stored when the event ends) |
| Navigation | *Previous / next event* buttons and keys; a click on a band jumps there with a few seconds' lead-in |
| Skip the quiet | *Play events only*: plays each event and jumps over the rest |
| Zoom | Mouse wheel / pinch, from the whole recording down to a minute |
| Recordings lists | A small sparkline and an event count per row; a filter *with activity only* |
| Feedback | Right-click a band: *correct* / *false alarm* / *missed something here* — writes the review file of phase 9 |
| Where | WPF: `FullScreenRecordingWindow` (above its seek slider) and the `RecordingsBrowserDialog` rows, one control in Wpf.Core shared by CameraWall and the client. Web: the Recordings page player, its logic in `Logic/` with tests |

### Server timeline (every camera, across recordings)

The per-recording timeline answers *where in this file*. The server timeline answers *what happened at the site*: every camera in one view, over hours or days, without opening recordings one by one. It is built from the activity files of phase 7, so it adds no second detector.

| Part | Design |
|---|---|
| What it shows | One lane per camera, over a chosen span (default the last 24 h): **coverage** (where a recording exists), **gaps** (camera offline, not recording, disk full: from the connection and recording events), **activity bars** and **event bands** as in the per-recording timeline, and a **now line** |
| Day index on the server | Per camera and day, a summary built from that day's activity files: coverage spans with their recording ids, 10 s buckets (max level, max tracks), and the merged events (start, end, peak level, track count, peak thumbnail, later the labels). Kept in the data folder (`{data}/timeline/{camera}/{yyyy-MM-dd}.json`, paths from `ServerDataPaths`) and in memory for recent days. **Rebuilt from the activity files when missing or stale**, so it is a cache, never the only copy |
| Kept up to date | The recording service appends to today's index as each activity line is written; a finished segment closes its coverage span. The cleanup and the disk-space guard drop deleted recordings from the index (an agreed hook in the guard's result, not a scan) |
| Live | New events go out on `GET /events` (`activityStarted`, `activityEnded` with camera, time, level, later the label), so an open timeline grows without polling |
| API (spec-first, all three roles) | `GET /timeline?from=&to=&cameraIds=&width=` returns per camera the coverage spans, the gaps, the buckets **downsampled by max** to the requested width, and the events in the span. `GET /timeline/events?cameraIds=&from=&direction=next\|previous&limit=` finds the next or previous event across all recordings. Spans over a few days are cached (`x-cache-*`) and capped (a `maximum` on the span in the spec) |
| Playback across segments | A click resolves to *recording id + offset*. The player starts there and moves on to the next segment of that camera at its end, so a long timeline plays as one stream. A gap is skipped or shown, as the user chooses |
| Several cameras at once | *Play all from here*: the live grid's layout plays every lane from the same moment, kept roughly in step (the slowest sets the pace) |
| Filters | Cameras or a layout; *with activity only* hides quiet lanes; later per class (person, vehicle, animal) |
| Thumbnails | Each event's peak frame, stored when the event ends (the suggestion below), served behind the media token like recording thumbnails |
| Where | Web: a new *Timeline* page (lanes, day picker, zoom from a week to a minute, keyboard next / previous). WPF client: a *Timeline* view with the same control as the per-recording timeline, in lane mode. CameraWall: the same Core index builder over its own folders, later (it has no server) |

---

## 🛰️ Market Comparison: Activity Timelines

Only what the searches confirmed. "Not checked" means no source was found, not that the product lacks it.

| Product | Activity level | Event marks | Per-class colour / icon | Preview on hover | Skip / filter | Search | OpenCV in motion detection |
|---|---|---|---|---|---|---|---|
| **Frigate** | ✅ lines whose length is the amount of motion | ✅ shaded bands: alerts red, detections orange; motion yellow | Severity colours; person and car are alerts by default | Not checked | ✅ *motion only* mode skips periods already covered by alerts and detections; all cameras scrubbed together | Review items grouped per camera | ✅ **Yes**: its motion detector calls `cv2.accumulateWeighted` (running background), `absdiff`, `threshold`, `dilate`, `findContours`, `boundingRect`; the blur is SciPy |
| **Google Home / Nest** | Grey for motion outside zones | ✅ dots and bars | ✅ **colour per activity zone**; event filters | Not checked | Gaps between events (event plans) | Not checked | ❔ Closed source; no component list found |
| **UniFi Protect** | Not checked | ✅ timeline in the detection player; horizontal event scrubber | Colours not confirmed | ✅ frame preview scrubbing in detection clips | Not checked | ✅ *Find Anything* (replaced the Detections page) | ❔ Closed source; Ubiquiti names no libraries |
| **Synology Surveillance Station** | Not checked | ✅ | ✅ **colour per trigger** (motion, I/O, manual, …) | Not checked | Smart Time Lapse condenses long recordings | Smart Search (not in Timeline) | ❔ Licence list inside the package; not checked |
| **Milestone XProtect** | Not checked | ✅ bookmarks (blue), other markers in other colours | Marker colour by source | ✅ bookmark preview on hover | Not checked | ✅ motion search in a drawn area | ❔ Third-party terms ship in the install folder; not checked |
| **Blue Iris** (community sources only) | Not checked | ✅ clip bars, alert markers (orange/red) | Per camera; add-ons colour by AI class | Not checked | High-speed playback | Not checked | ❔ Not found; its help calls its own algorithms "fairly simple" (one is "Gaussian") |
| **Reolink** | Not checked | ✅ | ✅ **icons**: dot for motion, person, vehicle, paw | Not checked | Not checked | Not checked | ❔ Not found (detection is in the camera) |
| **Ring** | Not checked | ✅ event history timeline | Event types person / animal / vehicle | Not checked | ✅ filter by device and type | Not checked | ❔ Not found (cloud) |
| **Scrypted** | Not checked | Not checked | Not checked | Not checked | Not checked | Not checked | ✅ **Optional**: an *OpenCV Motion Detection* plugin (more CPU and memory, better results than its PAM Diff detector); its NVR uses a WebAssembly motion detector instead |
| Arlo, Eufy, HomeKit Secure Video | Not checked | Not checked | Not checked | Not checked | Not checked | Not checked | ❔ Not found |

**OpenCV:** confirmed only where the code is open: **Frigate** (core) and **Scrypted** (an optional plugin). The closed products publish no component lists that a search found, so ❔ means unknown, not "no". What Frigate does with OpenCV is the same short chain this plan writes in C# (decision 1): a running-average background, a difference, a threshold, dilation and contours. So the pure-C# detector follows a proven design without bringing OpenCV's native runtime into the published Core package.

**What to take:**

- From Frigate: **the amount of motion as a bar**, events as bands, and *motion only* playback. It is the most complete and the best documented.
- From Google Home: colours that **mean something the user chose** (zones). We use them for classes, and later for zones too.
- From Reolink and Ring: **an icon per class** and a filter by type.
- From UniFi and Milestone: **preview on hover**.
- Our own additions: max-downsampling with a minimum width (a 5 s event in an hour never disappears), *next event* keys, and **feedback from the timeline** straight into the test corpus. None of the products above was found doing that.

---

## 🧪 Sample App and Test Harness

### `sample/BoundingBoxWpfApp`

| Feature | Notes |
|---|---|
| Open a recording | Any MP4/MKV, played by the repo's `VideoEngine` and `VideoHost` |
| Open a storybook | Generates the video from it (cached by the storybook's hash), then plays it with its truth |
| The real pipeline | The Core `MotionDetector` and the Wpf.Core `MotionBoundingBoxOverlay` — referenced, never copied |
| Two clocks | *Live*: real time, as in the apps. *Deterministic*: every frame analysed by its pts, then paused, so a run always gives the same result |
| Debug layers | Foreground mask; rejected blobs in grey with the reason (too small, masked, not travelled, not confirmed); track ids; truth boxes in green |
| Tuning panel | Every detector setting, applied at once; *Save as storybook settings* |
| Score panel | Precision, recall, false boxes per frame, id switches, latency — per object and per time span |
| Review mode | For real recordings (phase 9): mark spans and boxes as correct / false / missed |
| Automation | Each drawn box is exposed to UI Automation (`AutomationId` `box-{trackId}`, its rectangle in video coordinates in `HelpText`); the score as JSON in a text element |
| Not shipped | `IsPackable` false, not in the release; in the solution under a `sample` folder |

### The play-storybook file (`*.storybook.json`)

A scene, its objects and what is expected. The generator draws the frames from it, so the truth is exact.

```json
{
  "version": 1,
  "name": "car-human-cat",
  "video": { "width": 1280, "height": 720, "fps": 15, "durationSeconds": 20,
             "noise": 2.0, "bitrateKbps": 2000, "rotation": "none" },
  "background": {
    "kind": "procedural", "seed": 42,
    "distractors": [
      { "id": "tree", "kind": "swayingTree", "at": [0.12, 0.55], "size": 0.25, "amplitude": 0.015, "periodSeconds": 1.7 },
      { "id": "clock", "kind": "clockOverlay", "corner": "topRight" },
      { "id": "dusk", "kind": "brightnessStep", "atSeconds": 15, "delta": 40 }
    ]
  },
  "objects": [
    { "id": "car", "kind": "car", "size": [0.22, 0.12],
      "path": [ { "t": 2, "at": [-0.2, 0.75] }, { "t": 8, "at": [1.2, 0.75] } ] },
    { "id": "human", "kind": "human", "size": [0.05, 0.18],
      "path": [ { "t": 3, "at": [0.3, 0.6] }, { "t": 7, "at": [0.6, 0.6] },
                { "t": 9, "at": [0.6, 0.6] }, { "t": 12, "at": [0.9, 0.62] } ] },
    { "id": "cat", "kind": "cat", "size": [0.04, 0.03],
      "path": [ { "t": 4, "at": [0.8, 0.85] }, { "t": 9, "at": [0.5, 0.85] } ] }
  ],
  "expect": {
    "tracks": 3,
    "minRecall": 0.95, "maxFalseBoxesPerFrame": 0.02, "maxIdSwitchesPerObject": 1,
    "maxLatencySeconds": 0.5, "matchIou": 0.3,
    "spans": [
      { "object": "human", "from": 7.2, "to": 9.0, "state": "dontCare", "why": "stands still" },
      { "distractor": "tree", "state": "mustNotBox" },
      { "distractor": "clock", "state": "mustNotBox" },
      { "from": 15, "to": 16, "state": "dontCare", "why": "lighting step" }
    ]
  }
}
```

| Part | Rules |
|---|---|
| Coordinates | Fractions of the picture (0–1), as shown (after rotation) |
| Paths | Key points with times, linear between them; before the first and after the last time the object is not in the picture |
| Truth states | **mustBox** (a moving object; the default), **mustNotBox** (distractors), **dontCare** (standing still, entering, a lighting step) |
| Objects | Simple silhouettes drawn in C# (car, human, dog, cat, with some texture and a shadow). Good enough for motion; **not** for an AI model — phase 10 needs real recordings or licensed photo sprites |
| Truth file | The generator writes `*.truth.jsonl` next to the video: per frame, each object's box from the pixels it actually drew |

### Generator

| Choice | Why |
|---|---|
| Frames drawn in managed C# (a BGRA buffer), deterministic from the seed | The truth comes from the same drawing; no image library dependency |
| Encoded by piping raw frames to the `ffmpeg.exe` the installer provides (H.264 at the storybook's bitrate) | Real compression artefacts, as from a camera |
| A fast path without encoding: frames go straight to the detector | Unit tests run in milliseconds, without FFmpeg |
| A live path: the generated file streamed over UDP by `ffmpeg -re` | Drives the real player and both apps, like the existing test cameras |

### Scoring

| Measure | How |
|---|---|
| Match | Per frame, Hungarian (or greedy) matching of drawn boxes to the truth by **IoU ≥ 0.3** or centre inside (motion boxes are padded, so 0.5 is too strict) |
| Recall / precision | Over *mustBox* frames, after a latency allowance at each object's start |
| False boxes per frame | Boxes matched to nothing, or inside a *mustNotBox* region |
| Track count, id switches, fragmentation | From the track ids |
| Latency | From an object's first *mustBox* frame to its first matched box |
| Report | JSON (`score.json`) and a short text table; the same code in the sample app and in the tests |

### Tests

| Test | Where | Needs |
|---|---|---|
| Storybook parsing, path maths, truth generation | `test/AtcSoft.VideoSurveillance.Storybook.Tests` | Nothing |
| Scorer against hand-made box lists | same | Nothing |
| **Detector against every scene, fast path** (the main regression test) | same | Nothing |
| Detector through encode and decode | same | FFmpeg; skips without |
| Overlay mapping (analysis → screen, letterbox, rotation) | `Core.Tests` (`MotionBoxMapper`), extended | Nothing |
| **FlaUI walk of the sample app**: open a storybook, run deterministic, compare the drawn boxes (automation) with the detector output and the truth at chosen frames, read the score | `test/AtcSoft.VideoSurveillance.E2E.Tests`, a new `BoundingBoxSampleSmokeTests` | `ATCSOFT_E2E=1`, a desktop, FFmpeg |
| Real recordings with reviews | `Storybook.Tests`, the corpus folder | `ATCSOFT_BBOX_CORPUS`; skips without |

### Scene catalogue

| # | Scene | Checks |
|---|---|---|
| S1 | Empty scene, sensor noise | No box, no motion |
| S2 | Tree in the wind (and moving grass) | No box, no motion |
| S3 | One person walking slowly | Found within 0.5 s, one box, one id |
| S4 | A car passing fast | One box, no ghost behind it |
| S5 | **Car + human + cat at the same time**, crossing once | Exactly three tracks, at most one id switch each |
| S6 | Lighting step and IR switch (colour → grey) | No box during the step; detection resumes after |
| S7 | S3 at 90 / 180 / 270° | Boxes on the object in the drawn picture (the 180° bug). Through the player only (phase 3): the fast path already sees the shown picture |
| S8 | A small cat far away | Found |
| S9 | A person stops for 5 s, then walks on | Same id after the stop |
| S10 | Rain or snow specks | No box |
| S11 | S5 at a low bitrate (blocky) | As S5 |
| S12 | A camera clock in the corner | No box for the clock |

---

## 🔁 Feedback Loop for Real Recordings

| Step | What |
|---|---|
| 1 | Open a real recording in the sample app (review mode); the detector runs deterministic |
| 2 | Mark spans: *correct*, *false box*, *missed*, *wrong count* (with the right count); optionally drag a box on a paused frame and name it (person, car, cat, dog, …) |
| 3 | Saved as `*.review.json` next to the recording: the recording's SHA-256, the detector version and settings, and the verdicts |
| 4 | The review becomes truth: marked spans become *mustBox* / *mustNotBox* with their counts; everything not marked is *dontCare* |
| 5 | The tests run every recording in `ATCSOFT_BBOX_CORPUS` with its review and compare with the last accepted score; a drop fails |
| 6 | Later, the timeline's right-click verdicts in the apps write the same format, so a user can hand over a recording with verdicts |

```json
{
  "version": 1,
  "recording": { "file": "garden-2026-10-20.mp4", "sha256": "…" },
  "detector": { "version": "2.0", "settings": { "…": "…" } },
  "spans": [
    { "from": "00:47:58", "to": "00:48:03", "verdict": "correct", "count": 1, "label": "person" },
    { "from": "00:12:00", "to": "00:14:30", "verdict": "falseBox", "note": "tree" },
    { "from": "00:31:10", "to": "00:31:14", "verdict": "missed", "count": 1, "label": "cat",
      "box": { "atFrame": 28012, "x": 0.71, "y": 0.80, "w": 0.04, "h": 0.03 } }
  ]
}
```

The recordings stay out of git (size, privacy). The small review files may go in `test/…/corpus/` and name their recording by its hash.

---

## 🛣️ Phases

| # | Phase | Size | Status |
|---|-------|:--:|:--:|
| 0 | Quick fixes in today's pipeline | S | ✅ |
| 1 | Storybook, generator, truth and scorer; today's baseline | L | 🟨 fast path, scorer and ten scenes done; S10, S11, the encoder pipe and render time open |
| 2 | Grey frames with their time from the engine | M | ⬜ |
| 3 | `sample/BoundingBoxWpfApp` and its FlaUI test | M | ⬜ |
| 4 | Detector v2 in Core (background model, morphology, merge) | L | 🟨 detector built and used by both apps; settings, SIMD and the rest of the scenes open |
| 5 | Tracker: persistence, travel, ids; smoothing by id | M | 🟨 tracker, ids on the wire, web smoothing by id; WPF overlays still by nearness |
| 6 | Masks and zones | L | ⬜ |
| 7 | Activity index (sidecar, API) | M | 🟨 Core, server and CameraWall done; the setting and *Analyse recording* open |
| 8 | Activity timeline in both WPF apps and the web client | L | 🟨 timeline, jumps and click to seek in all three clients; hover, play events only, zoom, sparklines open |
| 8b | Server timeline: activity summaries, `GET /timeline`, next / previous event | M | ✅ |
| 8c | Timeline view of every camera: web page and WPF client view, playback across segments, live events (`activityStarted` / `activityEnded`), gap reasons | L | ⬜ |
| 9 | Feedback loop: review mode and the regression corpus | M | ⬜ |
| 10 | Local AI detection (ONNX) on motion regions | L | ⏸️ |

### Phase 0 — Quick fixes ✅ (S)

Done 2026-10-19: `BoxSmoother` (Core) smooths each box against its own previous box; `MotionHold` (Core) holds motion on the desktop as on the server; CameraWall reads its pictures through `IMotionFrameReader`; the doc default is fixed.

- Smoothing matched to the **nearest previous box** (IoU or centre), not to the list index (`MotionBoundingBoxOverlay.xaml.cs:150`).
- CameraWall holds motion for `PostMotionDurationSeconds` as the server does, so boxes stop blinking (`MotionDetectionService.cs:326-343`).
- CameraWall uses `IMotionFrameReader` instead of its own grey conversion.
- `docs/motion-detection.md`: `AnalysisFrameRate` default 15.

**Test:** unit tests for the overlay matching (moved into a plain class, out of the code-behind); the existing `MotionFrameAnalyzerTests` stay green.

### Phase 1 — Storybook, generator, truth and scorer 🟨 (L)

**Done 2026-10-19:**

- The detector seam `IMotionDetector` (Core) with today's detection as `FrameDifferenceMotionDetector`; CameraWall and the server use only the seam, so the baseline measures what ships.
- `src/AtcSoft.VideoSurveillance.Storybook` (not packable): the model and its reader (errors name the place, such as `objects[0].path[1].t`), a deterministic renderer (our own random generator keyed by seed and frame; truth from the pixels actually drawn; standing still and less than half visible are *dontCare*), the scorer and the fast-path runner. The runner squeezes to 800×600 as the apps do and scores the boxes the apps show (kept until the detector replaces them or motion ends).
- Scenes S1, S2, S3, S5 and S12; a ratchet: `baseline.json` holds every score, a test fails when one gets worse, `ATCSOFT_BBOX_UPDATE_BASELINE=1` writes new scores on purpose.

**Baseline of today's detector** (default settings; recall over must-box frames after 0.5 s, false boxes per analysed frame):

| Scene | 4/s recall | 4/s false/frame | 15/s recall | 15/s false/frame |
|---|---|---|---|---|
| S1 empty, noise | 1.000 | 0.000 | 1.000 | 0.000 |
| S2 tree in the wind | 1.000 | **2.286** | 1.000 | **3.591** |
| S3 slow walker | **0.405** | 0.531 | **0.021** | 0.845 |
| S5 car + human + cat | 0.955 | **1.612** | 0.805 | **3.199** |
| S12 camera clock | 1.000 | 0.000 | 1.000 | 0.000 |

It shows both complaints: a tree gives two to four boxes in every frame, and a slow walker is mostly missed, at 15/s almost always (two pictures 1/15 s apart barely differ). The clock gives no box because its change stays under the 0.5 % gate, the gate that also hides small objects.

**Still to do in phase 1:** scenes S4, S6, S8–S11; the encoder pipe and `*.truth.jsonl`; the render time (10 runs take about 40 s).

- A non-packable `src/AtcSoft.VideoSurveillance.Storybook` (net10.0): the model, JSON reading with clear errors, the frame renderer, the truth writer, the encoder pipe, the scorer.
- Scenes S1–S12 as files under `test/AtcSoft.VideoSurveillance.Storybook.Tests/Scenes`.
- **Baseline**: today's `MotionFrameAnalyzer`, with the services' gating, scored on every scene. The numbers go into this plan, so phase 4 shows its gain. The tests first assert the baseline as it is (marked known-bad), then tighten.

**Test:** the parsing, truth and scorer tests; the detector-against-scenes test.

### Phase 2 — Grey frames with their time ⬜ (M)

- Engine: `TryCaptureAnalysisFrame(width, height)` returns the latest decoded frame's **Y plane**, scaled by swscale and rotated, with its pts. No PNG.
- Engine: an offline reader that decodes a file frame by frame (as fast as it can) to the same grey frames, for the sample app, the scorer and *Analyse recording*.
- Both services switch to it; `GdiMotionFrameReader` stays only as a fallback if needed.

**Test:** engine tests against the live test stream (pts increase, size and rotation right); the CPU cost against the PNG path in the test output.

### Phase 3 — Sample app and FlaUI test ⬜ (M)

- `sample/BoundingBoxWpfApp` as described above, added to `AtcSoft.VideoSurveillance.slnx` under `sample`.
- The overlay exposes each box to UI Automation (useful for the apps' E2E walks too).
- `BoundingBoxSampleSmokeTests`: open S5, run deterministic, pause at chosen times, compare the automation boxes with the truth, read the score panel. Also open a plain recording.

**Test:** the FlaUI walk (`ATCSOFT_E2E=1`). To check early: whether FlaUI sees elements inside the `VideoHost` overlay window.

### Phase 4 — Detector v2 🟨 (L)

**Done 2026-10-19:** `BuiltInMotionDetector` (Core, behind `IMotionDetector`): a 3×3 blur, a background per pixel (running mean and variance, time-based learning rates), a lighting-change check, open and close on the mask, blobs with a smallest share of the picture, merging of near blobs, and the tracker of phase 5. CameraWall and the server use it; `FrameDifferenceMotionDetector` stays for comparison. `BoundingBox` carries an optional `TrackId`. Options come from the existing *Sensitivity*; no new settings yet.

| Scene | Today 4/s: recall, false/frame | New 4/s | Today 15/s | New 15/s |
|---|---|---|---|---|
| S1 empty, noise | 1.000, 0.000 | 1.000, 0.000 | 1.000, 0.000 | 1.000, 0.000 |
| S2 tree in the wind | 1.000, **2.286** | 1.000, **0.000** | 1.000, **3.591** | 1.000, **0.011** |
| S3 slow walker | **0.405**, 0.531 | **1.000**, 0.000 | **0.021**, 0.845 | **0.979**, 0.000 |
| S5 car + human + cat | 0.955, **1.612** | 1.000, **0.000**, 3 tracks | 0.805, **3.199** | 0.996, **0.000**, 3 tracks |
| S12 camera clock | 1.000, 0.000 | 1.000, 0.000 | 1.000, 0.000 | 1.000, 0.000 |

Scenes written after the tuning, run without changing it (unseen), with a size per key point added to the storybook for S13:

| Scene | Today 4/s | New 4/s | Today 15/s | New 15/s |
|---|---|---|---|---|
| S4 fast car | 1.000, **0.673** | 1.000, 0.000 | 1.000, **0.801** | 1.000, 0.000 |
| S6 lighting step | 1.000, 0.100 | 1.000, 0.000 | **0.405**, 0.547 | 1.000, 0.000 |
| S8 small cat far away | **0.000**, 0.000 | **1.000**, 0.000 | **0.000**, 0.000 | **0.979**, 0.000 |
| S9 stops for 4 s, walks on | 1.000, 0.082 | 1.000, 0.000 | **0.000**, 0.873 | 1.000, 0.000 |
| S13 walks up to the camera | 0.966, **0.918** | 0.966, 0.000 | **0.509**, 0.530 | 0.973, 0.000 |

S4, S8 and S9 passed at once. S13 needed growth to count as travel (a sustained change of 30 % in the smoothed size), and the lighting change now shifts the background by the overall brightness change instead of re-learning everything (which left a ghost of whoever was in the picture). Today's detector never finds the small cat: the 0.5 % gate.

Cost per picture at 800×600: about 11 ms (today's 2 ms), so about 4–5 % of one core per camera at the server's 4/s; about 2 KB allocated per picture (the working arrays are kept per detector). Latency: 0.2–0.5 s for most, 0.7 s for a slow walker or a small cat (3 % of the width first), 0.7–1 s for someone coming straight at the camera.

**Still open:** the new settings in plain words (smallest object, learning speed, ignore lighting changes) end to end; analysis size from the width only (keep the aspect ratio); SIMD; scenes S10 (rain) and S11 (low bitrate, needs the encoder); a person filling more than half of the picture (a door or USB camera) is not covered by a scene yet; checking with real cameras.

- `IMotionDetector` (the seam) and `BuiltInMotionDetector` (stateful, in Core, pure C# with `Vector128` / `Vector<T>`): steps 2–9 of the pipeline above. An OpenCV detector can later plug into the same seam from a package of its own.
- Both services and the sample app use it; `MotionFrameAnalyzer` remains only until the services have moved.
- New settings in plain words: *smallest object* (% of the picture), *learning speed*, *ignore lighting changes*. End to end: Core, `MotionDetectionPolicy.Resolve`, the overrides, both settings dialogs, the API contract, `SettingsRoundTripTests`, four languages, the web copy in `Logic/` with its equality test.
- `MinArea` (px²) is replaced by the relative minimum; an old value is read and converted.
- Analysis size from the width only; the height follows the aspect ratio.

**Test:** scenes S1, S2, S3, S4, S6, S8, S10, S11 and S12 pass their thresholds; S5 has at most one box per object per frame (the track count comes in phase 5).

### Phase 5 — Tracker 🟨 (M)

**Done 2026-10-19:** `MotionTracker` (constant-velocity guess, matching by overlap or nearness, confirmation after 0.4 s and 3 % travel, end after 1 s unseen); motion is on while a confirmed track exists, so webhooks and motion recordings already start on a confirmed track. `MotionBox.trackId` in the spec and on `GET /events`; Core's `BoxSmoother` follows a box by its id first; the web client smooths by id too (`Logic/MotionBoxSmoother`, kept equal to Core by a test). **Open:** the WPF overlays take plain rectangles, so they still match by nearness (ids would have to reach `MotionBoundingBoxOverlay`); a host test that sees track ids in the event stream.

- A SORT-style tracker written from the paper (the reference code is GPL): prediction, IoU or centre matching, confirmation after N hits **and** a minimum net travel, end after M misses.
- Motion is active while a confirmed track exists. `MinimumChangePercent` stays readable in old files but no longer decides (deprecated, like the server's `language`).
- `MotionBox` gets an optional `trackId` in the spec; both clients smooth **by id** (WPF overlay; web: a `Logic/` class with tests). The render cap goes; the detector's output is already small.
- Webhooks and motion recordings start on a confirmed track, not on a changed percentage.

**Test:** S5 gives exactly three tracks; S9 keeps the id; S2 gives none; a host test sees track ids in the event stream.

### Phase 6 — Masks and zones ⬜ (L)

Covers the backlog row *Motion zones / exclusion masks*.

- Per camera: polygons that are **ignored**, and zones with **their own sensitivity and smallest object**, in fractions of the shown picture.
- Drawn on a snapshot in both camera dialogs and in the web camera form.
- A zone's colour is reused on the timeline (the Google Home idea).

**Test:** a storybook scene with a mask over the tree; dialog steps in the E2E walks.

### Phase 7 — Activity index 🟨 (M)

**Done 2026-10-19:**

- Core: `{video}.activity.jsonl` beside the video in every layout (`RecordingPathHelper.BuildActivityPath`). A header `{"v":1,"start":…}`, then one line per analysed second: `t` offset, `a` level (the largest changed share, 0–100, the tree included), `n` confirmed tracks, `ids`. A second without a line was not analysed, which is not the same as quiet. `ActivityAccumulator` (fed by every analysed picture, not by motion events), `ActivityLogWriter` (a line as soon as the second is over), `ActivityLogReader` (reads while written, skips a cut-off line), `ActivityDownsampler.ByMax`.
- The index is a companion of its recording (`GetCompanionPaths`): deleted by the remover, the clean-up and the disk-space guard, protected while recorded, moved by the reorganiser.
- Both apps: `ActivityIndexRecorder` (Core) writes the index of every recording, a new file per segment; each app runs it from a hosted `ActivityIndexService`. It needs only `IActiveRecordings`, which both recording interfaces now extend. A recorded camera is analysed even when it is not watched for motion, in an index-only mode of `IMotionDetectionService` (`StartIndexing`) that reports no motion: no badge, webhook or motion recording comes of it. In CameraWall a tile starting, restarting or stopping its motion watch leaves the index running.
- `GET /recordings/activity` with `hasIndex` false for a recording without one. Unit tests, handler tests, and a host test that records the live test stream and reads the activity back.

**Open:**

- A setting to turn the index off (always on today). It has to pass `SettingsRoundTripTests`, with texts in four languages and both clients.
- *Analyse recording* for older recordings.
- The time base is the session's start, not the first keyframe the recorder waited for, so the index may run up to a GOP ahead of the video. Phase 8b, which joins segments, should measure that against a real camera.
- Analysis costs a PNG capture per picture for every recorded camera; a camera analysed only for the index is held to 4 a second in both apps. Phase 2's grey frames would make it cheap.
- The `MaxRecordingStorageGb` cap counts the videos only, not their thumbnails and indexes (an index is about 2 MB per camera per day). The clean-up's orphan pass removes an index whose video is gone.

### Phase 8 — Activity timeline 🟨 (L)

**Done 2026-10-19 (both WPF apps and the web client):** `ActivityTimelineLayout` in Core (bar height by square root, event bands of at least 3 px, next and previous event); the drawn `ActivityTimeline` control in Wpf.Core above the player's seek bar, with previous / next event buttons (texts in four languages). CameraWall reads the index beside the file, the server client asks `GET /recordings/activity`; the client's Recordings view got a Play button (it played only on a double click, which a keyboard cannot do). The web player draws the same timeline as SVG under the video (its layout a `Logic/` copy kept equal to Core's), with the same jumps; it shows also when the browser cannot play the file. SVG numbers are written with a point in every language (`SvgNumber`), which fixes the live view's motion boxes in Danish and German too. Unit tests, a drawn-to-bitmap control test, and a step in all three walks. **Open:** hover with a thumbnail, *play events only*, zoom, sparklines in the recordings lists, a playhead line on the web timeline.

- A shared `ActivityTimeline` control in Wpf.Core (both WPF apps) and a web component with its logic in `Logic/`.
- Bars, bands, hover with thumbnail, next and previous event, *play events only*, zoom, sparklines in the recordings lists.
- All texts through `Translations`, in four languages.

**Test:** unit (layout maths: a 5 s event in 60 min is at least 3 px; max downsampling); E2E steps for the recording window and the web Recordings page.

### Phase 8b — Server timeline ✅ (M)

**Done 2026-10-19:** each activity index is summarised once (`ActivitySummarizer`: 10 s stretches by max, events to the second, moving seconds 5 s apart joined) and kept in `ActivitySummaryCache` (capped, a changed file is read again); `TimelineBuilder` builds a camera's lane from its recordings, with **coverage from the video files** (the name's time to the last write, now for one in progress), so a recording without an index is *recorded but not analysed*, not a gap, and events are joined across segments. `RecordingCatalog` is the one walk of the recordings folder, shared with the listing. `GET /timeline` (span at most 7 days, checked in the handler; no `x-cache`, a span reaching now grows) and `GET /timeline/events` (`after` or `before`, looking at most 7 days away). Reading an index is three times faster than in phase 7 (one reader per line): a cold day of ten cameras takes under a second, summaries are read in parallel. Unit, handler and host tests (two segments of the live test stream, one lane without a gap). **Moved to 8c:** the live `activityStarted` / `activityEnded` events and the reasons of gaps (offline, not recording, disk full), which the view needs. **Not built:** the day files of decision 11, see there.


- `TimelineIndex` in Core: builds a camera's day from its activity files (coverage, gaps, 10 s buckets by max, merged events). Pure and shared, so CameraWall can use it later.
- The server keeps the day indexes in `{data}/timeline/` and in memory, appends while recording, drops deleted recordings, rebuilds from the activity files when missing.
- `GET /timeline` and `GET /timeline/events` in the spec (roles, span maximum, caching); `activityStarted` / `activityEnded` on `GET /events`.

**Test:** unit (index from activity files; a gap where no recording exists; max downsampling; next / previous event across days); a host test records two segments of the live test stream with a moving object and reads one lane with coverage and an event back; the event stream test sees `activityStarted`.

### Phase 8c — Timeline view of every camera ⬜ (L)

- Web: a *Timeline* page with its logic in `Logic/` (lane layout, zoom, now line); WPF client: a *Timeline* view using the Wpf.Core control in lane mode.
- A click plays from that moment and moves on across segments; *play all from here* for a layout.
- From 8b: `activityStarted` / `activityEnded` on `GET /events`, so an open timeline grows without polling; the reasons of gaps (offline, not recording, disk full) from the connection and recording events.
- Texts through `Translations`, in four languages.

**Test:** unit (lane layout, the click-to-recording mapping across segment boundaries, live growth from events); E2E steps for the web page and the WPF view against the test server.

### Phase 9 — Feedback loop ⬜ (M)

- Review mode in the sample app; `*.review.json`; review → truth; the corpus test with `ATCSOFT_BBOX_CORPUS`.
- The timeline's right-click verdicts in the apps write the same format.
- A *sweep* command in the sample app: runs a grid of settings over all scenes and the corpus and lists the best defaults.

**Test:** round trip of a review file; the corpus test on one small recording.

### Phase 10 — Local AI detection ⏸️ (L)

Covers the backlog row *AI detection (ONNX)*.

- ONNX Runtime (`Microsoft.ML.OnnxRuntime`, DirectML on Windows) runs only on **crops around confirmed tracks**, a few times per track, not on every frame.
- The label goes on the track (`label` and `score` in `MotionBox` and the activity index); the timeline colours and marks by class; recording and webhooks can require a class.
- Model choice: Decision 8.

**Test:** real recordings from the corpus (silhouettes will not do); label accuracy per class in the score.

---

## ⚖️ Pure C# or OpenCvSharp?

The detector of phase 4 can be written in plain C# or built on [OpenCvSharp](https://github.com/shimat/opencvsharp), the .NET wrapper of OpenCV. This is the biggest technical choice in the plan, so here are both sides.

### What the detector needs

| Step | Size of the job in plain C# |
|---|---|
| Scale the grey picture, keep the aspect ratio | Done by FFmpeg's swscale in phase 2, so neither option is needed for it |
| Light blur (3×3 or 5×5 box or Gaussian) | A separable pass over rows and columns |
| Running mean and variance per pixel (the background) | One pass with `Vector<T>` |
| Difference against the background, threshold | Same pass |
| Morphology: open, close (3×3 or 5×5) | Separable min and max passes |
| Connected components with their boxes | Two-pass union–find; one pass over the mask |
| Merge, filters, tracker, scoring | Plain logic, the same in both options |

At an analysis size of 640×360 a picture is 230 400 grey pixels, and each step is one or two passes over them. That is small work in either option. Later work that OpenCV would make easier: optical flow, CLAHE (contrast in the dark), several-mode background models (MOG2, KNN).

### Pure C#

| ✅ For | ❌ Against |
|---|---|
| **No native runtime in the published Core package.** Core ships to NuGet with five other packages in lockstep; every consumer would have to deploy OpenCV next to the FFmpeg they already deploy | **We write and keep the algorithms.** Bugs in edge cases (borders, odd sizes, rotation) are ours to find; the storybook scores are the safety net |
| **One language, one debugger.** Step through a frame, inspect a mask as a `byte[]`, test any step alone | **Slower on heavy steps.** OpenCV's hand-tuned SIMD (and Intel IPP) beats a first C# version on big kernels; `Vector128`/`Vector256` closes much of the gap for the small kernels this plan needs |
| **Deterministic everywhere.** The same bytes in give the same boxes on every CPU, which the scorer and the regression corpus depend on | **Nothing ready-made for later needs.** Optical flow, CLAHE or MOG2 would each be written by us, or bring OpenCV in after all |
| **No interop:** no `Mat` lifetimes, no leaks of native memory, no native crash that takes down a 24/7 server or the desktop app | **No direct reuse of others' OpenCV code.** Frigate's parameters carry over, its code does not |
| **Runs wherever .NET runs**, the web client's WebAssembly included, should the web ever analyse locally | |
| **No clash with our FFmpeg 9.** OpenCV's video I/O can bring its own FFmpeg DLLs | |
| **Small:** a few hundred lines in Core; nothing to add to the MSI installers or the server | |

### OpenCvSharp

| ✅ For | ❌ Against |
|---|---|
| **Mature and fast:** OpenCV's optimised code, OpenCL through `UMat` | **A native runtime per platform:** `OpenCvSharp4.runtime.win` alone is a 38 MB package; Linux and macOS need their own; Windows x86 is no longer built |
| **Everything is there:** MOG2 and KNN background subtractors, morphology, `findContours`, `connectedComponentsWithStats`, optical flow, CLAHE | **It cannot go into Core** without forcing the runtime on every NuGet consumer. It would need a separate package, so the desktop and the server would no longer share one plain detector by default |
| **Frigate's design maps one to one:** `accumulateWeighted`, `absdiff`, `threshold`, `dilate`, `findContours` | **Native failure modes:** `Mat` objects must be disposed; a leak or an access violation in native code ends the process, where a C# bug only throws |
| **Fast to prototype**, especially in the sample app; a large community and many examples | **Version churn:** OpenCvSharp4 is in maintenance and OpenCvSharp5 (OpenCV 5) is the new line, so a move is ahead either way |
| **Apache-2.0**, actively released (4.13.0.20260627, June 2026), x64 and ARM64 on Windows, Linux and macOS, also WebAssembly | **Possibly less deterministic:** optimised paths and threads can differ by CPU, which makes scores harder to compare across machines |
| | **Deployment grows:** the MSIs, the server and the E2E machines all carry the runtime; the full Linux package needs GTK, the slim one drops contrib and video I/O |
| | **No CUDA** in the packages; GPU help would come from OpenCL only |

### The middle way

- **Production: pure C#** in Core, behind an `IMotionDetector` interface, used by both services and the sample app.
- **OpenCvSharp as a reference, never shipped.** The sample app and the storybook tests may run an OpenCvSharp detector (Frigate's chain, or MOG2) on the same scenes as an oracle. If it scores clearly better, we see by how much, and on which scenes, before deciding to bring OpenCV in.
- **A way back in, if needed.** Because of the interface, an OpenCvSharp detector could later ship as a package of its own (for example `AtcSoft.VideoSurveillance.Detection.OpenCv`) that a host chooses. Core stays free of native code.

### When to revisit

| Tripwire | Measured where |
|---|---|
| The C# detector misses the phase 4 thresholds on the scenes, and the OpenCvSharp oracle meets them | Storybook scores (phase 1 baseline, phase 4) |
| Detection costs more than a few milliseconds per picture per camera at 2–4 pictures a second, at the analysis size | The sample app's CPU panel; a benchmark test |
| A later phase needs optical flow, CLAHE or a several-mode background model | Phases 6 and 10 |

### Decided (2026-10-19)

**Our own implementation in pure C#, for as long as it does as well.** OpenCV stays an option we control, not a dependency:

| Part | How |
|---|---|
| The seam | `IMotionDetector` in Core: a picture (grey, with its time) in, boxes and track ids out. Both services, the sample app and the scorer use only the interface |
| The default | `BuiltInMotionDetector` in Core, pure C# |
| OpenCV, if ever needed | A separate, optional package (for example `AtcSoft.VideoSurveillance.Detection.OpenCv`) with an `OpenCvMotionDetector`, chosen per camera or per app by a setting (`MotionDetectorEngine`: `builtIn` or `openCv`). Core and the default installers stay free of native code; only a host that picks it carries the runtime. Since OpenCV 4 has no C API, "natively" in practice means through OpenCvSharp's thin native layer, or a small native shim of our own if we ever need to own that too |
| Deciding by numbers | The sample app and the storybook tests can run both detectors on the same scenes. The tripwires above say when OpenCV earns its place |

Nothing of the OpenCV option is built now: only the seam is, in phase 4.

---

## ❓ Decisions Needed

| # | Question | Recommendation |
|---|----------|----------------|
| 1 | **Pure C# or OpenCvSharp for the detector?** | ✅ **Decided 2026-10-19: our own pure C# detector** behind `IMotionDetector`, as long as it does as well; an optional OpenCV detector in a separate package stays possible, chosen by a setting. See [Pure C# or OpenCvSharp?](#️-pure-c-or-opencvsharp) |
| 2 | **Storybook in JSON or YAML?** | **JSON**, read with System.Text.Json: no new dependency, and a JSON schema gives editor help |
| 3 | **Draw frames in C# or with FFmpeg filters?** | **C#**: the truth comes from the same drawing. FFmpeg only encodes |
| 4 | **Where do storybook, generator and scorer live?** | A new non-packable `AtcSoft.VideoSurveillance.Storybook` project, used by the sample and the tests. **Not in Core** (published) |
| 5 | **Activity index: a sidecar per recording or a database on the server?** | **Sidecar.** It moves and is deleted with the file, works for CameraWall without a server, and follows the folder layouts. The server can cache it in memory |
| 6 | **Run detection for every recording camera?** | **Yes, at 2–4/s, while the index is on** (default on). Without it a continuous recording has no timeline. The grey-frame path of phase 2 makes it cheap |
| 7 | **What decides "motion is active"?** | **A confirmed track.** The global `MinimumChangePercent` is deprecated and kept readable |
| 8 | **Which AI model, later?** | A permissively licensed one: **YOLOX (Apache-2.0)** or **RF-DETR (Apache-2.0)**. **Not Ultralytics YOLO (AGPL-3.0)** unless an enterprise licence is bought. RT-DETR, D-FINE and NanoDet are candidates; check each repository's LICENSE **and the weights' licence** before choosing |
| 9 | **Where do the real recordings for the corpus live?** | Outside git, in a folder named by `ATCSOFT_BBOX_CORPUS`. The review files may be in git and name their recording by SHA-256 |
| 10 | **Release the wire change (`trackId`) with the desktop client?** | Yes. It is optional, so older clients ignore it, and the server and the desktop client are released together anyway |
| 11 | **Server timeline: day files or a database?** | ✅ **Decided 2026-10-19: neither, for now: summaries in memory** (`ActivitySummaryCache`), read from the activity files on first use. Nothing to keep in step, a deleted recording falls out by itself. Day files (in the data folder, rebuilt from the activity files) only if measurement says a cold span is too slow; SQLite only after that |
| 12 | **How far back does the timeline reach?** | **As far as the recordings do.** The index follows the recordings; the cleanup and the disk-space guard remove days whose recordings are gone |
| 13 | **Gaps: what explains them?** | Coverage comes from the recordings; the reason (offline, not recording, disk full, the guard stopped it) from the connection and recording events, kept in the day index. A gap without a known reason is shown as *no recording* |

---

## 💡 Suggestions

- **A debug view in the apps too** (behind a setting): the foreground mask and the rejected blobs with their reason. Tuning without it is guessing.
- **Settings in plain words, with presets**: *Garden (trees)*, *Street*, *Indoor*. Each preset is a tested set from the sweep (phase 9).
- **Defaults from data**: run the sweep over the scenes and the corpus, and set the defaults to what scores best.
- **One thumbnail per event** (the peak frame, cropped around the track): for the timeline hover, notifications and webhooks.
- **Notifications and webhooks only on confirmed tracks**: the change that removes the tree boxes also removes the tree alerts.
- **Camera clock overlays** (date and time burnt into the picture) change every second. The travel rule ignores them; offer a mask when one is found.
- **Day and night**: record the IR switch as its own flag in the activity index; it explains a grey band in the timeline.
- **CPU per camera** in the sample app's score panel, so the cost of each step is known before it reaches a server with many cameras.

---

## ✋ To Verify by Hand

| What | Needs |
|---|---|
| A camera facing a tree on a windy day: no boxes for the tree, one box for a person walking past | A camera, wind |
| Dusk: the IR switch gives no box and no event | A camera with IR, an evening |
| Rain or snow at night | Weather |
| Boxes on the object at 0 / 90 / 180 / 270° in the grid and full screen, in both WPF apps and the web client | A camera |
| A real 60 min recording with a short event: the event is visible and *next event* finds it, in all three clients | A recording |
| The first real recording with the user's review file runs as a test | The user's recording and verdicts |
| CPU with eight cameras recording with the activity index on | A machine and cameras |
| The server timeline over a day with several cameras: coverage, gaps after unplugging a camera, events, a click that plays across a segment boundary, live growth | The server, cameras, a day |

---

## 📚 Sources

- OpenCvSharp repository (licence, packages, platforms, limitations): https://github.com/shimat/opencvsharp
- OpenCvSharp4.runtime.win on NuGet (version, size): https://www.nuget.org/packages/OpenCvSharp4.runtime.win
- Frigate's motion detector, `frigate/motion/improved_motion.py`: https://github.com/blakeblackshear/frigate/blob/dev/frigate/motion/improved_motion.py
- Scrypted motion detection: https://docs.scrypted.app/detection/motion-detection.html and the HomeKit Secure Video setup: https://github.com/koush/scrypted/wiki/HomeKit-Secure-Video-Setup
- Blue Iris help on its motion algorithms (quoted on IPCamTalk): https://ipcamtalk.com/threads/blue-iris-motion-detection.60238/post-616819
- Milestone's third-party terms notice: https://www.milestonesys.com/globalassets/techcomm/ltsb/provms/english-united-states/2769.htm
- Ubiquiti's open-source licence statement: https://www.ui.com/download/unifi-protect

Detection and tracking:

- Frigate, [Tuning motion detection](https://docs.frigate.video/configuration/motion_detection) (threshold, contour area, lightning threshold, improve contrast) and [Masks](https://docs.frigate.video/configuration/masks) (motion regions passed to the object detector).
- Frigate source, [`frigate/util/object.py`](https://git.laurivan.com/Mirrors/blakeblackshear.frigate/src/branch/dev/frigate/util/object.py) (region grid).
- OpenCV, [`BackgroundSubtractorMOG2`](https://docs.opencv.org/3.4/javadoc/org/opencv/video/BackgroundSubtractorMOG2.html) (history, varThreshold, shadows, background ratio).
- Bewley et al., [Simple Online and Realtime Tracking](https://arxiv.org/pdf/1602.00763) (SORT: Kalman prediction, Hungarian matching); the reference implementation is GPL.

AI models and runtime:

- ONNX Runtime, [DirectML execution provider](https://onnxruntime.ai/docs/execution-providers/DirectML-ExecutionProvider); [Microsoft.ML.OnnxRuntime.DirectML](https://www.nuget.org/packages/Microsoft.ML.OnnxRuntime.DirectML) on NuGet.
- [YOLOX](https://playground.roboflow.com/models/megvii/yolox) (Apache-2.0); [RF-DETR is Apache-2.0](https://blog.roboflow.com/rf-detr-is-free-to-use-commercially/); [Ultralytics](https://docs.ultralytics.com/) (AGPL-3.0 or an enterprise licence); [Why AGPL-3.0 is a risk](https://blog.roboflow.com/why-agpl-3-0-is-a-risk-for-computer-vision-teams/) (a vendor's view).

Timelines:

- Frigate, [Review](https://docs.frigate.video/usage/review), [History](https://docs.frigate.video/usage/history), [Review configuration](https://docs.frigate.video/configuration/review), [0.14 release notes](https://newreleases.io/project/github/blakeblackshear/frigate/release/v0.14.0-beta1).
- Google, [Nest video history on a computer](https://support.google.com/googlenest/answer/9225631) and [Video history in the Google Home app](https://support.google.com/googlenest/answer/10904254).
- UniFi Protect, [5.2.42 overview](https://lazyadmin.nl/home-network/unifi-protect-5-2-42-overview/) and the [App Store release notes](https://apps.apple.com/us/app/unifi-protect/id1392492235).
- Synology, [Work with Timeline](https://www.synology.com/en-us/knowledgebase/Surveillance/help/SurveillanceStation/multievent_player) and the [Surveillance Station spec](https://www.synology.com/en-us/dsm/7.0/software_spec/surveillance_station).
- Milestone, [The main timeline](https://doc.milestonesys.com/2025R1/en-US/standard_features/sf_sc/sf_funda/sc_uitimelinemain.htm) and [Searching in XProtect Smart Client](https://doc.milestonesys.com/2025R1/en-US/standard_features/sf_sc/sf_investigate/current/sc_searchinginsc.htm).
- Blue Iris, community posts on [ipcamtalk](https://ipcamtalk.com/goto/post?id=181173) and the [UI3 timeline](https://www.ipcamtalk.net/goto/post?id=628778).
- Reolink, [Icons on the Reolink app](https://support.reolink.com/articles/22170873096729-Introduction-to-the-Icons-on-Reolink-App/); Ring, [Event History Timeline](https://ring.com/support/articles/16fks/Using-Event-History-Timeline).
- The code as read on 2026-10-18 (the file and line references above).
