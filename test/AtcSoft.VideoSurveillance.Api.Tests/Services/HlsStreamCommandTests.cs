namespace AtcSoft.VideoSurveillance.Api.Services;

public sealed class HlsStreamCommandTests
{
    private const string Playlist = @"C:\hls\cam\stream.m3u8";

    [Fact]
    public void An_Rtsp_Camera_Gets_Its_Rtsp_Transport()
    {
        var camera = CreateCamera(CoreCameraProtocol.Rtsp, 554);
        camera.Stream.RtspTransport = "udp";

        var arguments = HlsStreamCommand.BuildArguments(camera, Playlist);

        arguments.Should().Contain("-rtsp_transport udp");
        arguments.Should().Contain("-i \"rtsp://192.0.2.10:554/stream1\"");
        arguments.Should().EndWith($"\"{Playlist}\"");
    }

    [Theory]
    [InlineData(CoreCameraProtocol.Http, 80)]
    [InlineData(CoreCameraProtocol.Https, 443)]
    public void An_Http_Camera_Does_Not_Get_The_Rtsp_Only_Option(
        CoreCameraProtocol protocol,
        int port)
    {
        // FFmpeg refuses an option the input format does not know, so an
        // HTTP camera with -rtsp_transport never starts streaming.
        var camera = CreateCamera(protocol, port);

        var arguments = HlsStreamCommand.BuildArguments(camera, Playlist);

        arguments.Should().NotContain("rtsp_transport");
        arguments.Should().Contain("-c:v copy");
    }

    [Fact]
    public void FFmpeg_Is_Told_Not_To_Print_Its_Progress()
    {
        // The progress line comes twice a second per stream, and every line
        // FFmpeg prints goes into the server's log.
        var arguments = HlsStreamCommand.BuildArguments(CreateCamera(CoreCameraProtocol.Rtsp, 554), Playlist);

        arguments.Should().StartWith("-nostats ");
    }

    [Fact]
    public void The_Password_Is_Not_In_What_Gets_Logged()
    {
        var camera = CreateCamera(CoreCameraProtocol.Rtsp, 554);
        camera.Authentication.UserName = "admin";
        camera.Authentication.Password = "s3cr3t!pw";

        var arguments = HlsStreamCommand.BuildArguments(camera, Playlist);
        arguments.Should().Contain("s3cr3t", "FFmpeg itself needs the password");

        var logged = HlsStreamCommand.Redact(arguments, camera);

        logged.Should().NotContain("s3cr3t").And.NotContain("admin");
        logged.Should().Contain("192.0.2.10:554/stream1", "the rest of the address stays readable");
    }

    [Fact]
    public void The_Password_Is_Removed_From_FFmpeg_Output_Too()
    {
        // FFmpeg prints the input address, user and password included.
        var camera = CreateCamera(CoreCameraProtocol.Rtsp, 554);
        camera.Authentication.UserName = "admin";
        camera.Authentication.Password = "p@ss word";
        var line = $"Input #0, rtsp, from '{camera.BuildUri()}':";

        var logged = HlsStreamCommand.Redact(line, camera);

        logged.Should().NotContain("p%40ss").And.NotContain("p@ss").And.NotContain("admin");
        logged.Should().StartWith("Input #0, rtsp, from 'rtsp://");
    }

    [Fact]
    public void A_Short_Password_Is_Removed_Where_It_Is_A_Password_And_Nowhere_Else()
    {
        // Removing every "1" from a line would ruin it without hiding anything.
        var camera = CreateCamera(CoreCameraProtocol.Rtsp, 554);
        camera.Authentication.UserName = "admin";
        camera.Authentication.Password = "1";
        var line = $"Input #0, rtsp, from '{camera.BuildUri()}': frame=  120 fps= 15";

        var logged = HlsStreamCommand.Redact(line, camera);

        logged.Should().NotContain("admin:1@");
        logged.Should().Contain("192.0.2.10:554/stream1").And.EndWith("frame=  120 fps= 15");
    }

    [Fact]
    public void Text_Without_Credentials_Is_Left_As_It_Is()
    {
        var camera = CreateCamera(CoreCameraProtocol.Rtsp, 554);

        HlsStreamCommand.Redact("frame=  120 fps= 15", camera).Should().Be("frame=  120 fps= 15");
    }

    [Fact]
    public void The_Tool_Is_Taken_From_The_Folder_The_Engine_Uses()
    {
        var folder = Path.Combine(Path.GetTempPath(), "atcsoft-hls-tool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var tool = Path.Combine(folder, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
            File.WriteAllText(tool, string.Empty);

            HlsStreamCommand.FindTool(folder).Should().Be(tool);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(@"C:\no-such-folder-for-ffmpeg")]
    public void Without_The_Tool_In_That_Folder_The_Path_Is_Searched(
        string? folder)
    {
        HlsStreamCommand.FindTool(folder).Should().Be("ffmpeg");
    }

    [Fact]
    public void A_Usb_Camera_Is_Opened_As_A_Capture_Device_In_Its_Chosen_Format()
    {
        var camera = CreateUsbCamera("HP 5MP Camera");
        camera.Connection.Usb!.Format = new UsbStreamFormat { Width = 1280, Height = 720, FrameRate = 15 };

        var arguments = HlsStreamCommand.BuildArguments(camera, Playlist);

        // The capture options belong to the input, so they stand before it.
        arguments.Should().Contain("-f dshow -video_size 1280x720 -framerate 15 -i \"video=HP 5MP Camera\"");
        arguments.Should().EndWith($"\"{Playlist}\"");
    }

    [Fact]
    public void A_Usb_Camera_Without_A_Chosen_Format_Is_Opened_In_The_Device_Default()
    {
        var arguments = HlsStreamCommand.BuildArguments(CreateUsbCamera("HP Display Camera"), Playlist);

        arguments.Should().Contain("-f dshow -i \"video=HP Display Camera\"");
        arguments.Should().NotContain("-video_size").And.NotContain("-framerate");
    }

    [Fact]
    public void A_Usb_Camera_Is_Encoded_Because_It_Sends_Uncompressed_Pictures()
    {
        // A webcam sends raw pictures, which cannot be copied into HLS, and
        // browsers only play H.264 with 4:2:0 colour.
        var camera = CreateUsbCamera("HP 5MP Camera");
        camera.Connection.Usb!.Format = new UsbStreamFormat { Width = 640, Height = 480, FrameRate = 30 };

        var arguments = HlsStreamCommand.BuildArguments(camera, Playlist);

        arguments.Should().Contain("-c:v libx264").And.Contain("-pix_fmt yuv420p");
        arguments.Should().NotContain("-c:v copy");

        // A key frame every two seconds, so every HLS segment can start with one.
        arguments.Should().Contain("-g 60 ");
    }

    [Fact]
    public void A_Usb_Camera_Gets_No_Network_Options()
    {
        // FFmpeg refuses an option the input does not know.
        var arguments = HlsStreamCommand.BuildArguments(CreateUsbCamera("HP 5MP Camera"), Playlist);

        arguments.Should().NotContain("rtsp_transport").And.NotContain("-timeout");
    }

    [Fact]
    public void A_Usb_Camera_With_Sound_Opens_Its_Microphone_Too()
    {
        var camera = CreateUsbCamera("HP 5MP Camera");
        camera.Connection.Usb!.PreferAudio = true;
        camera.Connection.Usb.AudioDeviceName = "Microphone (HP 534pm Audio)";

        var arguments = HlsStreamCommand.BuildArguments(camera, Playlist);

        arguments.Should().Contain("-i \"video=HP 5MP Camera:audio=Microphone (HP 534pm Audio)\"");
    }

    [Fact]
    public void Text_About_A_Usb_Camera_Is_Left_As_It_Is()
    {
        // A USB camera has no address and no password; asking it for one throws.
        var line = "Input #0, dshow, from 'video=HP 5MP Camera':";

        HlsStreamCommand.Redact(line, CreateUsbCamera("HP 5MP Camera")).Should().Be(line);
    }

    [Fact]
    public void A_Usb_Camera_The_Server_Is_Recording_Cannot_Be_Streamed_As_Well()
    {
        // Windows lets one program at a time open a USB camera. The second
        // one gets no pictures and no clear error, so it is refused up front.
        var reason = HlsStreamCommand.GetUnavailableReason(CreateUsbCamera("HP 5MP Camera"), isRecording: true);

        reason.Should().Contain("recording").And.Contain("USB");
    }

    [Fact]
    public void A_Usb_Camera_That_Is_Not_Being_Recorded_Can_Be_Streamed()
        => HlsStreamCommand.GetUnavailableReason(CreateUsbCamera("HP 5MP Camera"), isRecording: false)
            .Should().BeNull();

    [Fact]
    public void A_Network_Camera_Can_Be_Streamed_While_It_Is_Recorded()
        => HlsStreamCommand.GetUnavailableReason(CreateCamera(CoreCameraProtocol.Rtsp, 554), isRecording: true)
            .Should().BeNull();

    private static CameraConfiguration CreateUsbCamera(string friendlyName)
    {
        var camera = new CameraConfiguration { Id = Guid.NewGuid() };
        camera.Connection.Source = CoreCameraSource.Usb;
        camera.Connection.Usb = new UsbConnectionSettings
        {
            DeviceId = @"\\?\usb#vid_0000&pid_0000#test",
            FriendlyName = friendlyName,
        };
        return camera;
    }

    private static CameraConfiguration CreateCamera(
        CoreCameraProtocol protocol,
        int port)
    {
        var camera = new CameraConfiguration { Id = Guid.NewGuid() };
        camera.Connection.Protocol = protocol;
        camera.Connection.IpAddress = "192.0.2.10";
        camera.Connection.Port = port;
        camera.Connection.Path = "stream1";
        return camera;
    }
}