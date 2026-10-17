# 🗺️ AtcSoft.VideoSurveillance Roadmap

The one list of what is still open. Every item was checked against the code and the git history on
**2026-10-17**; the finished area plans moved to [`archive/`](archive/), where they keep their design and
decisions.

System documentation (how things work today) lives in [`docs/`](../docs/). This folder holds plans only.

*Last updated: 2026-10-17*

---

## 🗂️ Status Legend

| Emoji | Meaning |
|-------|---------|
| ⬜ | Not started |
| 🟨 | In progress, or built and awaiting verification |
| ✅ | Complete |
| ⏸️ | Deferred (intentionally not in current scope) |

Sizes: **S** an hour or two, **M** a day, **L** several days.

---

## 🎯 Focus

| What | Why now |
|---|---|
| Release the server and the desktop client together | The wire format of enums changed with generator v3 ([REST API generator v3](roadmap-rest-api-sg.md)); generator v3 has to be on nuget.org first, the build uses a local package until then |
| The hands-on checks below | A large part of what was built since 2026-10-12 has unit, host and E2E tests but has not been looked at with real cameras |
| Run the E2E smoke tests before a release | `ATCSOFT_E2E=1 dotnet test test/AtcSoft.VideoSurveillance.E2E.Tests` (see [`docs/e2e-smoke-tests.md`](../docs/e2e-smoke-tests.md)); on 2026-10-17 they found five defects the unit tests had not |

---

## 📚 Areas

| Area | Plan | Status | Open |
|---|---|:--:|---|
| 🧬 REST API source generator v3 | [roadmap-rest-api-sg.md](roadmap-rest-api-sg.md) | 🟨 | Phases 0–9 and 7b done, security S1–S6 done; waits for v3 on nuget.org. Minor items in that file |
| 🔄 Video rotation | [archive/roadmap-video-rotation.md](archive/roadmap-video-rotation.md) | 🟨 | Hands-on checks; the 180° report |
| 📁 Recording storage layout | [archive/roadmap-recording-storage.md](archive/roadmap-recording-storage.md) | 🟨 | Hands-on checks; cameras with their own recording path |
| 🔌 Connection-type icon | [archive/roadmap-cameras-connection-icons.md](archive/roadmap-cameras-connection-icons.md) | 🟨 | Hands-on checks; phase 8 (remove the legacy USB pills) |
| 🎥 USB cameras | [archive/roadmap-usb-cameras.md](archive/roadmap-usb-cameras.md) | ✅ | Hands-on checks; a few small gaps; ⏸️ Linux / macOS, UVC property pages |
| 🖥️ WPF API client | [archive/roadmap-wpf-api-client.md](archive/roadmap-wpf-api-client.md) | ✅ | Phases 1–9 done; phase 10 is in the backlog below |
| 🎞️ FFmpeg 9 | [roadmap-ffmpeg-9.md](roadmap-ffmpeg-9.md) | ⏸️ | Phase 1 done; FFmpeg 8.1.3 and the TLS setting can come first |
| 🧪 24/7 soak | [soak-test-plan.md](soak-test-plan.md) | ⬜ | The procedure was never run; the report scripts count day folders since 2026-10-17 |
| 💡 Ideas | [roadmap-suggestions.md](roadmap-suggestions.md) | — | The backlog below is the checked part of it |

---

## 🔧 Open work a developer can do now

Nothing here needs a camera or a person. Each item says how it can be tested.

### Small (S)

| Item | Where it stands | Test |
|---|---|---|
| Remove the legacy USB pills (connection-type icon, phase 8) | Still in `CameraOverlay.xaml`, `CameraTileBadge.xaml` and `CameraTileViewModel` (`ShowUsbBadge`). The plan gates it on "the release after the icon ships" | E2E walks catch a binding left behind |
| Rotation: the client's `Wpf/Windows/FullScreenCameraWindow` sizes boxes without the rotation | Nothing in the client opens that window today; fix it or remove the window | Unit, once the sizing leaves the code-behind |
| A message for a stored USB device that is not plugged in (`UsbDeviceMissing`) | No key or message exists; the picker may already cover it | Unit on the dialog view model |
| `Debug.WriteLine` left in `CameraTile`, `ApplicationSettingsService`, `CameraStorageService`, `SettingsDialogViewModel` | The suggestions list marks it done; it is not quite | None needed |
| A rate limit on starting a recording or a stream | Neither has `x-ratelimit-*`. A second start only adds a lease, so it may not be needed | Host test |
| Remote-access notes (VPN, reverse proxy) in the docs | Nothing in `docs/` | — |
| A severity filter and an unread badge for notifications in the client | Only time, title, message and type columns | Unit and E2E |
| A theme toggle in the ribbon | None | E2E |

### Medium (M)

| Item | Where it stands | Test |
|---|---|---|
| Cameras with their own recording path are never browsed, cleaned or reorganised | Listing, the recordings browser, both clean-up services and `RecordingLayoutReorganizer.Synchronize` use the global path only | Unit with temp trees, host |
| Record into each folder layout end to end without a camera | The host tests record with the FFmpeg test stream, but not into each layout | Host |
| TLS verification as an explicit per-camera setting (FFmpeg 9 plan, phase 2) | No `tls_verify` anywhere; must pass `SettingsRoundTripTests` | Unit and E2E dialog walk |
| FFmpeg 8.1.3 (FFmpeg 9 plan, decision 4) | Still 8.1.2 in the pipeline and README; check that a shared build exists | Engine tests, E2E with the live test camera |
| Camera passwords at rest in standalone CameraWall | Only the server encrypts (`SecretProtector`); CameraWall writes them in plain text | Unit |
| The audit log keeps the value before a change | `AuditRecord` has none | Unit |
| Clip export / trim | Only a download exists | Unit and E2E |
| MQTT publisher | None | Unit and host |
| Webhook management in the WPF client | Web client only; whether it is wanted is open | E2E |
| Inline rename and drag-reorder in the client's camera and layout lists | Both grids are read-only | E2E |
| FFmpeg timeouts and probe sizes as settings; stream-health counters | Constants in `Demuxer` and `VideoPlayer` | Unit |
| Privacy and permission messages for a USB camera Windows refuses | Only documented in `docs/usb-cameras.md` | Unit for the message mapping |
| USB counters in the stats panel | None | Unit |

### Large (L)

| Item | Where it stands |
|---|---|
| Motion zones / exclusion masks | No zone concept anywhere |
| AI detection (ONNX) | Needs a model choice |
| Push notifications (WebPush) | None |
| WPF client phase 10: multi-monitor, camera groups and tags, an event log viewer, a camera health view, timelapse | None of it exists. Tags, health and timelapse need the server first (timelapse is only a setting today). The event log may be covered by the notification history and the audit log: decide whether it still stands |
| FFmpeg 9 and bindings 9.0.2 (phases 3–6) | ⏸️ deferred on 2026-10-13 |

---

## ✋ Waiting on a Person

Built and tested, but not closed without hands, eyes or hardware.

| What | Needs | From |
|---|---|---|
| Icon placement in all four info-box corners, grid and full screen; tint on connect and disconnect | A visual check | [Connection-type icon](archive/roadmap-cameras-connection-icons.md#still-to-verify-by-hand) |
| ONVIF detection against a wired, a Wi-Fi and a Tapo camera | Real cameras | [Connection-type icon](archive/roadmap-cameras-connection-icons.md#still-to-verify-by-hand) |
| Boxes at 0 / 90 / 180 / 270 in the grid and full screen; rotation after a setting change and a tile swap; snapshots and thumbnails upright | A real camera | [Video rotation](archive/roadmap-video-rotation.md#-to-verify-by-hand) |
| Record, browse, play and delete in each of the four folder layouts; an old flat archive; midnight rollover | The app and some recording time | [Recording storage](archive/roadmap-recording-storage.md#-to-verify-by-hand) |
| USB: unplug and replug in CameraWall (recording resumes within 5 s); recording and sound seen in CameraWall (the USB plan says not yet, the old roadmap said seen: confirm) | Two USB cameras | [USB cameras](archive/roadmap-usb-cameras.md) |
| "Show in Full Screen" on a CameraWall tile: through UI Automation it stayed disabled on a connected HTTP camera | The app and a camera | [E2E notes](../docs/e2e-smoke-tests.md#left-out-on-purpose) |
| The client's live grid now draws motion boxes (bound since 2026-10-17) | The server client and a camera that moves | — |
| The client's filters and grid size as label controls; the camera dialog's drop-downs | A look at the server client | — |
| The grid sizes, filter, disk indicators and unplugged text in both desktop apps; CameraWall's per-camera *Recording video* row; the keyboard shortcuts | A look at the apps | [Ideas](roadmap-suggestions.md) |
| The Users dialog (WPF) and the Users page (web) | A look at the clients | [REST API generator v3](roadmap-rest-api-sg.md) |
| The web client against real cameras: H.264 and H.265 live view, rotated cameras, playback of real recordings | A browser, the server and cameras | — |
| Webhooks against a real receiver (Home Assistant, Slack) | A receiver | [`docs/server.md`](../docs/server.md) |
| The 24/7 soak | A machine, three cameras and a day | [soak-test-plan.md](soak-test-plan.md) |

---

## 🐛 Known Bugs

| Bug | Tracked in |
|---|---|
| Motion boxes reported misplaced at 180°: two possible causes fixed, not confirmed | [Video rotation](archive/roadmap-video-rotation.md#the-180-report-is-not-explained) |
| `RecordingsBrowserDialogTests` is flaky in a full test run | [REST API generator v3](roadmap-rest-api-sg.md) |
| An `http://` client behind the HTTPS redirect loses its `Authorization` header | [REST API generator v3](roadmap-rest-api-sg.md) |
| `ValidationError` template in Atc.Wpf.Theming reads `(Validation.Errors)[0]`, which fails each time an error clears | Filed in atc-wpf: `issues/theming-validationerror-template-indexes-empty-errors.md` |

---

## ✅ Done since the last roadmap (2026-10-13 to 2026-10-17)

The details are in the commit messages and in [`docs/`](../docs/).

| What | Where to read |
|---|---|
| Sign-in, roles, API keys, audit log; HTTPS redirect and HSTS; secrets encrypted at rest; rate limits | [`docs/server.md`](../docs/server.md) |
| `GET /events` (Server-Sent Events) replaced SignalR; stream leases for live views | [`docs/server.md`](../docs/server.md) |
| REST API source generator v3 (local package until it is on nuget.org) | [roadmap-rest-api-sg.md](roadmap-rest-api-sg.md) |
| Every client in US and British English, Danish and German | CLAUDE.md, *Texts and languages* |
| E2E smoke tests of all three apps; binding errors logged; logs in the Aspire dashboard; `ATCSOFT_DATA_PATH` | [`docs/e2e-smoke-tests.md`](../docs/e2e-smoke-tests.md) |
| Found by the smoke tests and fixed: the client's camera dialog drop-downs, its environment configuration, motion boxes on its live tiles, the web client signed out after ten reloads a minute, its user menu label, its server address | The commits of 2026-10-17 |
| A camera without an address or a USB device is refused by the server | `CameraSourceCheck` |
| Neither client forgets its stored session when the server is busy, failing or restarting (only a refused refresh token does) | [`docs/server.md`](../docs/server.md) |
| The soak report scripts count recordings in day folders | `scripts/soak-report*.sh` |

---

## ✍️ Conventions

- **This page is the list of what is open.** An area gets a plan file of its own (`roadmap-<area>.md`) when it needs design and decisions; its open items are listed here too.
- **Every area plan has** a phase table with a status per phase, a "Decisions" section, and a "To verify by hand" section when something needs hardware or eyes.
- **Update this page in the same change** that closes or opens an item.
- **Ideas without a plan** go into [roadmap-suggestions.md](roadmap-suggestions.md); when one is picked up it moves here.
- **Finished areas** move to [`archive/`](archive/) once their open items are listed here or done.
- **How things work today** is documentation, not a plan: it belongs in [`docs/`](../docs/).
