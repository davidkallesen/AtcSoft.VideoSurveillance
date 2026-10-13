# 🖧 Server

How the headless server (`AtcSoft.VideoSurveillance.Api`) is configured and what it offers besides the REST endpoints in `src/VideoSurveillance.yaml`. The settings that the clients edit (recording folder, formats, clean-up and so on) are described in [settings.md](settings.md).

---

## ⚙️ Configuration

These are read from the server's configuration: `appsettings.json` next to the server, environment variables, or the command line. They are not part of the settings the clients can change.

| Key | Environment variable | Default | Purpose |
|---|---|---|---|
| `Storage:DataPath` | `Storage__DataPath` | `%ProgramData%\AtcSoft\VideoSurveillance` | The folder the server keeps its own files in |
| `Cors:AllowedOrigins` | `Cors__AllowedOrigins__0`, ... | The local web client addresses | Which web addresses may call the API |
| `Webhooks:Endpoints` | `Webhooks__Endpoints__0__Url`, ... | None | Where camera events are posted — see [Webhooks](#-webhooks) |

### Data folder

With `Storage:DataPath` set, the server keeps `settings.json` and `cameras.json` in that folder. The recording, snapshot and log folders are moved into it too, as long as they still point at their default location; a folder you picked yourself in the settings is left alone.

Use it to run a second server on the same machine, or to keep the server's data on another drive:

```powershell
$env:Storage__DataPath = "D:\Surveillance"
.\AtcSoft.VideoSurveillance.Api.exe --urls http://localhost:5000
```

The log folder is decided before the rest of the configuration is read. For the debug log to follow the data folder, give `Storage:DataPath` as an environment variable or on the command line, not only in `appsettings.json`.

---

## 📼 Recordings and thumbnails

`GET /recordings` lists the recordings, newest first. Each entry has:

| Field | Meaning |
|---|---|
| `filePath` | Where the recording is, below the recording folder, with forward slashes |
| `thumbnailPath` | Where its thumbnail is, the same way; `null` when it has none |
| `startedAt` | When it started. Taken from the file name, which is in UTC on the server |
| `duration` | Its length, ISO 8601 (`PT1M5S`). Empty while it is being recorded, when the file cannot be read, or when the server has not read the file yet: lengths are read in the background after a start, so a large archive fills in over the following listings |
| `inProgress` | `true` while it is still being recorded |
| `id` | The same for a file every time it is listed |

The files themselves are fetched from `/recordings-files/{filePath}` and `/recordings-files/{thumbnailPath}`. Escape each folder and file name of the path on its own (a camera name may contain `#`, `?`, `&`, `+` or `%`). Only recordings (`.mp4`, `.mkv`, `.avi`) and thumbnails (`.png`) are served, from the recording folder that is in the settings at the time of the request.

The server makes a thumbnail for every recording file, the way the standalone app does: one picture or a 2 x 2 grid (the *Thumbnail tiles* setting), stored next to the video or in `thumbnails\` (the *Keep thumbnails in a separate folder* setting). Thumbnails are drawn with GDI+, so a server that is not running on Windows records without them.

---

## 📺 Live streaming

The web client watches a camera through HLS. A client asks for a stream over the SignalR hub (`/hubs/surveillance`, `StartStream`) and gets back the address of a playlist below `/streams/`.

- The stream is made by the FFmpeg command-line tool. It is taken from the folder the video engine loads its FFmpeg libraries from, so it needs no entry in the `PATH`; without it there, the `PATH` is searched.
- A network camera's video is passed on as it is (no re-encoding), so a rotated camera is **not** rotated in the stream. A client turns the picture itself, from the camera's `rotation`.
- A USB camera sends uncompressed pictures, so its stream is encoded to H.264. That costs processor time for every USB camera being watched.
- **A USB camera the server has open itself** (it is recording it, or watching it for motion) is streamed from that open pipeline, not by the FFmpeg tool: the server's video engine writes the playlist and segments next to the recording, from one encoder. Such a stream has no sound.
- Windows lets one program at a time open a USB camera. A USB camera the server does **not** have open is streamed by the FFmpeg tool, which then holds it: starting a recording of it answers `409 Conflict` with the reason, and record-on-connect or motion watching waits until the viewers are gone. Close the live view, start the recording, and open the live view again; it is then served from the server's pipeline.
- A stream nobody asks about is stopped after a minute. A client that is watching calls `StreamHeartbeat` regularly (the web client does so every 20 seconds).
- A camera's user name and password are removed from everything the server logs or sends back about a stream.

---

## 🏃 Motion detection

A camera with *record on motion* on (the application's setting, or the camera's own override) is kept open by the server and watched.

- **What counts as a motion:** from the first picture in which something moved until the scene has been quiet for the post-motion time. Something that pauses for a moment does not end it.
- **Recording:** a motion starts a recording and its end stops it. After a motion recording, the next one waits for the cooldown. A camera that is *recorded on connect* as well is recording anyway; watching it only makes the motion events.
- **Events:** clients get `MotionDetected` over SignalR for every analysed picture with movement (with the boxes, for overlays) and once when the motion is over. Webhooks get `motion.started` and `motion.stopped`, once each.
- **How it looks:** up to four pictures a second, compared with the same analysis as the desktop app, at the analysis size from the settings. A higher analysis frame rate in the settings is capped at four.
- **Cost:** each analysed picture is captured as a PNG and decoded again. Two USB cameras at 1280x720, both watched and recorded (which for a USB camera includes encoding), used about 1.4 processor cores.
- **Windows only:** the pictures are decoded with GDI+. On another platform the server logs that motion detection is not available and runs without it.
- **Settings are read when a camera starts being watched.** A change to the motion settings reaches a camera the next time it is opened; turning record-on-motion on or off takes effect within half a minute.
- **USB cameras:** a watched USB camera is held by the server; the web client's live view of it is served from the same open pipeline.
- **Starting a recording by hand** for a watched camera opens the camera a second time. That works for a network camera and fails for a USB camera.

---

## 🔔 Webhooks

The server can post what happens to other systems (Home Assistant, Slack, a script of your own). Every receiver under `Webhooks:Endpoints` gets an HTTP `POST` per event.

```json
{
  "Webhooks": {
    "Endpoints": [
      {
        "Url": "https://example.org/hooks/surveillance",
        "Secret": "a-long-random-text",
        "Events": [ "motion.*", "camera.disconnected" ]
      }
    ]
  }
}
```

| Setting | Meaning |
|---|---|
| `Url` | Where to post. `http` or `https` |
| `Secret` | Optional. When set, the body is signed |
| `Events` | Optional. The events this receiver wants, by name or by group (`motion.*`). Left out: all of them |

### Receivers added while the server runs

Receivers can also be added, changed, tested and removed without touching the configuration: on the **Webhooks** page of the web client, or over the API.

| Call | Does |
|---|---|
| `GET /webhooks` | Lists all receivers, those from the configuration first |
| `POST /webhooks` | Adds one (`url`, optional `secret`, optional `events`) |
| `PUT /webhooks/{id}` | Changes one. Without `secret` the secret stays as it is; an empty `secret` removes it |
| `DELETE /webhooks/{id}` | Removes one |
| `POST /webhooks/{id}/test` | Sends a `webhook.test` event to that one receiver and answers whether it took it |

- They are kept in `webhooks.json` next to `settings.json`, secret included, like the camera passwords in `cameras.json`.
- A secret is never sent back: a receiver only says whether it has one.
- A receiver from the configuration is listed, and can be tested, but is changed in the configuration (`409 Conflict` otherwise).

### Events

| Event | When | `data` |
|---|---|---|
| `camera.connected` | A recorded camera connected | `state` |
| `camera.disconnected` | A recorded camera was lost, or stopped being recorded | `state` |
| `recording.started` | A recording file was started | `filePath` |
| `recording.stopped` | A recording file was finished | `filePath` |
| `motion.started` | Motion began | `changePercentage` |
| `motion.stopped` | Motion ended | `changePercentage` |

A recording that is split into segments reports a stop and a start for every file. `filePath` is the path `GET /recordings` lists the file under, so a receiver can fetch it from `/recordings-files/`.

The server follows a camera's connection only while it records it, so the camera events follow the recording. Motion events come when the server detects motion. Server-side motion detection is not built yet (see the roadmap), so today no motion events are sent.

### The request

```http
POST /hooks/surveillance
Content-Type: application/json
X-AtcSoft-Event: recording.stopped
X-AtcSoft-Delivery: 5f1c0e6d6f4b4a0b9d3c0c7a4c1f2e11
X-AtcSoft-Signature: sha256=6d0f...

{
  "event": "recording.stopped",
  "cameraId": "47c2d0ad-b1c1-424f-91de-70d7ef99b279",
  "cameraName": "Front Door",
  "timestamp": "2026-10-13T10:15:00+00:00",
  "data": { "filePath": "Front Door/2026-10-13/Front Door_20261013_101500_000.mp4" }
}
```

- **Signature:** `sha256=` followed by the HMAC-SHA256 of the body, keyed with the secret, in lower-case hex. Compute the same over the raw body you received and compare.
- **Delivery id:** the same for every attempt of one delivery, so a receiver can ignore a repeat.

### Delivery

- A receiver that answers with a server error (5xx), `408` or `429`, or cannot be reached, is tried three times.
- A receiver that refuses the request (any other 4xx) is not tried again.
- A receiver has 10 seconds to answer.
- Events wait in a queue of 500. When receivers are slower than events arrive, the oldest are dropped and the server says so in its log.
- The log names a receiver by scheme and host only, and never contains the secret.
- Receivers in the configuration are read when the server starts. Receivers added through the API or the web client take effect at once.
