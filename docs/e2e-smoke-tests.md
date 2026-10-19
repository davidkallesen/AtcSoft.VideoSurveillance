# End-to-end smoke tests

`test/AtcSoft.VideoSurveillance.E2E.Tests` starts the real apps and opens every window, page and
dialog they have, without cameras of their own and without touching anyone's data. A smoke test fails
on a step that breaks, on a binding error or unhandled exception in an app's log, on a console error,
page error or 5xx answer in the browser, and on a fatal entry in the server's output.

## Running them

The tests need a desktop session (they drive real windows), so they skip unless asked for:

```powershell
dotnet build
$env:ATCSOFT_E2E = "1"
dotnet test test/AtcSoft.VideoSurveillance.E2E.Tests
```

A run takes about three minutes. CI runs every test project, so there they skip.

What a run needs, and what happens without it:

| Needs | Without it |
|---|---|
| A desktop session, the screen not locked | The WPF walks cannot find their windows |
| The FFmpeg command-line tool (`ffmpeg.exe` where the engine finds FFmpeg) | No live test camera; the offline camera is still used |
| Microsoft Edge (Playwright uses the installed browser, nothing is downloaded) | The web client test fails to start a browser |

Leave the mouse alone while the CameraWall walk runs: a few steps are real clicks.

## What runs

- **The server**, as its own process on a free port, with a scratch data folder (`Storage__DataPath`).
  The fixture creates the first admin with the setup code, an offline camera (a port nothing listens
  on), a live camera (with FFmpeg) and a layout, all through the API.
- **CameraWall**, with `ATCSOFT_DATA_PATH` set to a scratch folder seeded with the app's own storage
  services: en-US, a log file, no recording, the two cameras in the start-up layout.
- **The WPF client**, pointed at the test server the way Aspire does it (`services__api__http__0`),
  signed in from `Auth__UserName` / `Auth__Password`, in a scratch folder of its own.
- **The FFmpeg set-up dialog** (`FFmpegSetupSmokeTests`): CameraWall and the WPF client started with
  `ATCSOFT_FFMPEG_SEARCH=explicit`, so the installed FFmpeg is not found. CameraWall takes the real
  FFmpeg folder typed into the dialog and starts; the client exits from the dialog. The install
  itself (a 100 MB download) is covered by the unit tests only.
- **The web client**, served by its development server on the port the test server allows as a CORS
  origin. The browser is pointed at the test server by answering the app's `appsettings.json` request.

The live camera is an FFmpeg test pattern served over HTTP. FFmpeg serves one client and ends when it
goes, so `LiveTestCamera` starts it again on the same port: the camera survives a reconnect.

Each WPF walk selects every tab and opens every drop-down in each dialog before closing it; binding
errors show up when a template is first used.

Both WPF walks also play a recording: CameraWall the one the fixture writes (with an activity index
beside it that has one event), the client the one the server recorded from the live camera. The player
must show its activity timeline; the event buttons are pressed (in the client only when the detector
found a track in the test pattern). The web walk plays the server's recording too and waits for the
timeline (an SVG named Activity), which shows whether or not the browser can play the file.

## Where to look after a run

Everything of a run is kept in `%TEMP%\AtcSoft.E2E\{yyyyMMdd-HHmmss}`:

- `camera-wall\logs`, `wpf-client\logs`: the apps' logs (binding errors are logged at Warning by
  `AtcSoft.BindingErrors`).
- `server-output.log`, `web-client-output.log`, `*-test-camera.log`: the processes' output.
- `camera-wall-ui`, `wpf-client-ui`: the UI Automation tree of every window at each failure.
- `web-client-ui`: a screenshot and the page text at each failure, and `browser-problems.txt`.

## Adding a step

Controls have no automation ids; the walks find them by their en-US text (the apps run in en-US) and
control type. To see what a window offers, run the tree dump:

```powershell
$env:ATCSOFT_E2E = "1"; $env:ATCSOFT_E2E_DUMP = "1"
dotnet test test/AtcSoft.VideoSurveillance.E2E.Tests --filter-class "*UiTreeDumpTests"
```

- **Owned windows:** UI Automation shows an owned (modal) dialog as a child of its owner, not of the
  desktop. `DesktopApp.Windows()` follows them, and leaves context-menu pop-ups out.
- **Steps go on after a failure:** `SmokeSteps` records a failed step and goes on, so one run reports
  every broken dialog. Its recovery closes what was left open.
- **Patterns, not keys:** the walks use UI Automation patterns (Invoke, SelectionItem,
  ExpandCollapse, Window.Close), not keystrokes.

## Left out on purpose

- **Opens something outside the app:** save and folder dialogs, opening the recordings folder,
  update downloads in a browser, Change Server (it starts a new process), and Start with Windows.
- **Changes or ends things:** deleting, the webhook test send, Restore defaults, signing out,
  switching the web client's language (it reloads the page), and Exit.
- **A tile's context menu in CameraWall:** it needs a real right click on the video, and from a test
  it does not open reliably once the walk has opened other windows. Its Edit Camera is the camera
  dialog the walk already opens through Add Camera. Through UI Automation, "Show in Full Screen" also
  stayed disabled on a connected tile; check that by hand before relying on it.

## What the smoke tests found (2026-10-17)

- The client's camera dialog never showed or saved the protocol, connection type, rotation, overlay
  position or RTSP transport (the drop-downs were bound to the generated structs).
- The client ignored environment variables, so the documented unattended sign-in did not work.
- Reloading the web client ten times a minute from one address signed it out (session renewal was
  under the sign-in limit, and any failed renewal forgot the session).
- The web client's user menu was labelled "Signed in as {0}".
- The web client could only reach a server on `http://localhost:5000` (no `appsettings.json`).
