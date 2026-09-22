# AtcSoft.VideoEngine

Cross-platform video engine built on in-process FFmpeg: demuxing, decoding, recording and frame capture for RTSP, HTTP and DirectShow (USB) sources.

Target framework: `net10.0`.

## Usage

Initialise the engine once at startup, then create players through the factory:

```csharp
VideoEngineBootstrap.Initialize(new VideoEngineConfig
{
    FFmpegPath = ffmpegFolder,          // folder holding the native FFmpeg DLLs
    PreferHardwareAcceleration = true,
});

var factory = new VideoPlayerFactory(loggerFactory);
IVideoPlayer player = factory.Create();
```

`VideoEngineConfig` also exposes `FFmpegLogLevel` and `DecoderThreads`. The parameterless `VideoEngineBootstrap.Initialize()` overload uses the defaults and probes for FFmpeg on the standard paths.

## FFmpeg is not included

This package contains no native binaries. Deploy FFmpeg **8.x** shared builds (`avutil-60.dll` and friends) with your application and point `FFmpegPath` at them. A different major version fails at runtime with `Library avutil.60* not found`.

## Related packages

- [`AtcSoft.VideoEngine.DirectX`](https://www.nuget.org/packages/AtcSoft.VideoEngine.DirectX) — D3D11VA hardware acceleration on Windows
- [`AtcSoft.VideoPlayer.Wpf`](https://www.nuget.org/packages/AtcSoft.VideoPlayer.Wpf) — WPF host control
- [`AtcSoft.VideoSurveillance.Wpf.Core`](https://www.nuget.org/packages/AtcSoft.VideoSurveillance.Wpf.Core) — ready-made camera tile and grid controls

## Links

- [Source and documentation](https://github.com/davidkallesen/AtcSoft.VideoSurveillance)
