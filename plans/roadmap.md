# 🗺️ AtcSoft.VideoSurveillance Roadmap

The home page for everything planned. Each **area** has its own `roadmap-<area>.md` with the design, phases and decisions; this page only says where each area stands and what comes next.

System documentation (how things work today) lives in [`docs/`](../docs/). This folder holds plans only.

*Last updated: 2026-10-13*

---

## 🗂️ Status Legend

| Emoji | Meaning |
|-------|---------|
| ⬜ | Not started |
| 🟨 | In progress, or built and awaiting verification |
| ✅ | Complete |
| ⏸️ | Deferred (intentionally not in current scope) |

---

## 🎯 Focus

### Now

| Area | What | Blocked on |
|---|---|---|
| [Video rotation](roadmap-video-rotation.md) | 🐛 Motion boxes and recordings ignore the camera's rotation — built and tested | Hands-on verification with a real camera |
| [Recording storage layout](roadmap-recording-storage.md) | Day folders and a separate thumbnail folder; files move when the switches are saved; the server makes thumbnails — built and tested | Hands-on verification in the desktop apps |
| [Connection-type icon](roadmap-cameras-connection-icons.md) | Built and unit-tested; needs a visual check and a try against real ONVIF cameras | Hands-on verification |

### Next

| Area | What |
|---|---|
| [Connection-type icon](roadmap-cameras-connection-icons.md) | Phase 8: remove the legacy "USB" pills (the release after the icon ships) |
| [USB cameras](roadmap-usb-cameras.md) | Recording a USB camera that is being watched live first (needs one owner for every use of the camera); a look at the USB paths in the server's desktop client |
| [Suggestions](roadmap-suggestions.md) | The grid sizes, the camera filter, the disk indicators and the unplugged text in the two desktop apps are built and tested but not yet looked at on screen. A field for the per-camera "record as H.264" override in the camera dialog |

### Later

| Area | What |
|---|---|
| [WPF API client](roadmap-wpf-api-client.md) | Phase 10: multi-monitor, camera groups, event log viewer, timelapse browser |
| [Suggestions](roadmap-suggestions.md) | Server security (authentication, HTTPS, rate limiting), server-side motion detection, integrations (MQTT, webhooks) |
| [USB cameras](roadmap-usb-cameras.md) | ⏸️ Linux / macOS capture, UVC property pages |
| [FFmpeg 9](roadmap-ffmpeg-9.md) | ⏸️ Move the engine to FFmpeg 9 and bindings 9.0.2; before that, the cheap step to FFmpeg 8.1.3 |

---

## 📚 Areas

| Area | Roadmap | Status | Done | Open |
|---|---|:--:|---|---|
| 🔄 Video rotation | [roadmap-video-rotation.md](roadmap-video-rotation.md) | 🟨 | Phases 2, 5, 7; phases 1, 3, 4, 6 built; the web client's live view rotates | Hands-on checks; the 180° box report is not explained |
| 📁 Recording storage layout | [roadmap-recording-storage.md](roadmap-recording-storage.md) | 🟨 | Phases 1, 4, 7; phases 2, 3, 5, 6 built; move-on-save; server thumbnails and a corrected recordings listing | Hands-on checks in each layout |
| 🔌 Connection-type icon | [roadmap-cameras-connection-icons.md](roadmap-cameras-connection-icons.md) | 🟨 | Phases 1, 4, 5, 7; phases 2, 3, 6 built and unit-tested | Hands-on checks for 2, 3, 6; phase 8 (remove legacy pills) |
| 🎥 USB cameras | [roadmap-usb-cameras.md](roadmap-usb-cameras.md) | 🟨 | Phases 1–9, 12; recording, sound, motion and live view seen with real cameras on 2026-10-13 | Watched-first-then-recorded; ⏸️ phases 10–11 |
| 🎞️ FFmpeg 9 upgrade | [roadmap-ffmpeg-9.md](roadmap-ffmpeg-9.md) | ⏸️ | Trial bump and impact analysis; phase 1 (a failed FFmpeg load fails the tests) | Phases 2–6; deferred on 2026-10-13 |
| 🖥️ WPF API client | [roadmap-wpf-api-client.md](roadmap-wpf-api-client.md) | 🟨 | Phases 1–9 | Phase 10 (advanced capabilities); auto-reconnect on network recovery |
| 💡 Suggestions (backlog) | [roadmap-suggestions.md](roadmap-suggestions.md) | — | Reliability fixes, batches 1–5; outbound webhooks; keyboard shortcuts; camera filter | Security gaps, feature gaps, remaining quick wins |
| 🌐 Web client | — (see the note below) | 🟨 | Settings, live view, recordings and dashboard brought in line with the desktop apps on 2026-10-13 | Camera and layout pages untouched; no delete of recordings; motion boxes untested (no server-side motion) |

### What changed on 2026-10-13

A long session without an area file of its own. The details are in the commit messages and in [`docs/server.md`](../docs/server.md); the decisions that were made on the way:

| Decision | Why |
|---|---|
| The thumbnail generator lives in `AtcSoft.VideoEngine.Windows` | The server and the standalone app both need it, it draws with GDI+, and that project is not published as a package |
| The server takes its data folder from `Storage:DataPath` | It could only run against the shared application data folder, so it could not be tested, or run twice, safely |
| Video quality is `Auto` / `1080p` / `720p` / `480p` / `360p` everywhere, the API included | The API had its own names with no translation, and a chosen resolution was reset to Auto by any save through the server |
| Colours are stored by name | The desktop colour selector cannot show the hex values the web client wrote; the web client now loads those as names |
| Webhooks are configured in the server's configuration, not in the settings | Receivers and their secrets are an operator's matter; a settings page can follow |
| Only reads are retried by the web client, and slow calls get minutes, not seconds | Starting a recording and saving a layout change were cut off after ten seconds and sent again |
| The API tests run the real server in-process against a temp data folder | Routing, serialization, the file server and the real recording pipeline are covered, including a live test stream from the FFmpeg tool |

### What changed later on 2026-10-13 (USB cameras, motion, and the rest of the quick wins)

Done with two USB cameras attached, then with the IP cameras back. Details in the commit messages, [`docs/server.md`](../docs/server.md), [`docs/usb-cameras.md`](../docs/usb-cameras.md) and [`docs/settings.md`](../docs/settings.md).

| What | State |
|---|---|
| USB cameras record (encoded to H.264), with sound from a microphone | Seen on the server and in CameraWall |
| Server-side motion detection: watch, record while it moves, events and webhooks | Seen with two cameras and a webhook receiver |
| A USB camera the server has open is streamed to browsers from that pipeline | Seen: live in the web client while watched and recorded |
| "Record as H.264" for cameras that send H.265 or MJPEG | Seen with a real H.265 camera, on the server and in CameraWall |
| Webhook receivers added, changed, tested and removed while the server runs; a page for it | Seen in a browser |
| USB cameras in the web client's camera form and list | Seen in a browser |
| Grid sizes with pages (both desktop apps), camera filter (server client) | Tested with the real grid control; not looked at on screen |
| Disk indicator in every client; names on icon-only buttons | Web dashboard seen; the desktop status bars not |

| Decision | Why |
|---|---|
| A fixed grid hides the tiles of the other pages instead of removing them | In CameraWall a camera is connected and recorded by its tile; removing the tile would stop the recording |
| The server is the one owner of a camera's pipeline; a motion recording borrows it | Stopping a motion recording must not close the connection that watches for the next motion |
| A motion lasts until the scene has been quiet for the post-motion time | An end at every pause would start and stop recordings and webhooks many times a minute |
| Only a USB camera is streamed from the server's pipeline | It cannot be opened twice. A network camera can, and its own stream carries its sound |
| The server analyses at most four pictures a second for motion | Each analysed picture is captured as a PNG and decoded again |
| Webhook receivers from the configuration stay read-only in the API | An operator's configuration should not be changed from a web page; added receivers live in `webhooks.json` |
| `recordAsH264` is a plain yes/no in the API | The setting has two values; an enum there would only add a translation |

### Supporting plans

| Plan | Purpose |
|---|---|
| [soak-test-plan.md](soak-test-plan.md) | How to run and judge a 24/7 stability soak of the camera wall |
| [archive/](archive/) | Finished plans kept for their design rationale |

---

## ✋ Waiting on a Person

Things that are built but cannot be closed without hands, eyes or hardware.

| What | Needs | Area |
|---|---|---|
| Icon placement in all four info-box corners, grid and full screen | A visual check | [Connection-type icon](roadmap-cameras-connection-icons.md#still-to-verify-by-hand) |
| ONVIF detection against a wired, a Wi-Fi and a Tapo camera | Real cameras | [Connection-type icon](roadmap-cameras-connection-icons.md#still-to-verify-by-hand) |
| Boxes at 0 / 90 / 180 / 270, rotation after a setting change and a tile swap (rotated playback is confirmed in the app; VLC needs MP4) | A real camera | [Video rotation](roadmap-video-rotation.md#-to-verify-by-hand) |
| Record, browse, play and delete in each of the four folder layouts; midnight rollover | The app and some recording time | [Recording storage layout](roadmap-recording-storage.md#-to-verify-by-hand) |
| The CameraWall keyboard shortcuts (Ctrl+1 to 9, Ctrl+N, Ctrl+R, F5, F1) and Enter in the recordings browser | The app | [Suggestions](roadmap-suggestions.md) |
| The filter box and the thumbnail column in the server client; its settings dialog after the contract change (video quality, quick actions) | The server client app | [WPF API client](roadmap-wpf-api-client.md) |
| The web client against real cameras: live view of H.264 and H.265 cameras, rotated cameras, playback of real recordings | A browser, the server and cameras | — |
| Webhooks against a real receiver (Home Assistant, Slack) | A receiver | [`docs/server.md`](../docs/server.md#-webhooks) |

---

## 🐛 Known Bugs

Open defects, wherever they are tracked.

| Bug | Tracked in |
|---|---|
| Motion boxes reported misplaced at 180° — two possible causes fixed, not confirmed | [Video rotation](roadmap-video-rotation.md#the-180-report-is-not-explained) |

---

## ✍️ Conventions

- **One area, one file:** `roadmap-<area>.md`. Start it with a one-paragraph summary and a link back here.
- **Every area file has** a phase table with a status per phase, a "Decisions" section, and a "To verify by hand" section when something needs hardware or eyes.
- **Update this page in the same change** that moves a phase: the *Areas* row, and *Focus* if priorities shifted.
- **Ideas without a plan** go into [roadmap-suggestions.md](roadmap-suggestions.md). When one is picked up, it gets its own area file and a row here.
- **Finished areas** move to [`archive/`](archive/) once nothing is open, and their row here is removed.
- **How things work today** is documentation, not a plan: it belongs in [`docs/`](../docs/).
