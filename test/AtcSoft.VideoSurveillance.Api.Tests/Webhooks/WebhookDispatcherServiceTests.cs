namespace AtcSoft.VideoSurveillance.Api.Webhooks;

public sealed class WebhookDispatcherServiceTests
{
    private static readonly WebhookEvent Motion = new(WebhookEventTypes.MotionStarted, Guid.NewGuid(), "Front Door", DateTimeOffset.UtcNow, new Dictionary<string, object?>(StringComparer.Ordinal));
    private static readonly WebhookEvent Recording = new(WebhookEventTypes.RecordingStopped, Guid.NewGuid(), "Garden", DateTimeOffset.UtcNow, new Dictionary<string, object?>(StringComparer.Ordinal));

    private readonly IWebhookSender sender = Substitute.For<IWebhookSender>();

    [Fact]
    public async Task Each_Queued_Event_Goes_To_Every_Endpoint_That_Wants_It()
    {
        var all = new WebhookEndpoint { Url = "https://all.test/hook" };
        var motionOnly = new WebhookEndpoint { Url = "https://motion.test/hook", Events = ["motion.*"] };
        using var sut = CreateSut(all, motionOnly);

        sut.Enqueue(Motion).Should().BeTrue();
        sut.Enqueue(Recording).Should().BeTrue();
        await sut.DoWorkAsync(TestContext.Current.CancellationToken);

        await sender.Received(1).SendAsync(all, Motion, Arg.Any<CancellationToken>());
        await sender.Received(1).SendAsync(all, Recording, Arg.Any<CancellationToken>());
        await sender.Received(1).SendAsync(motionOnly, Motion, Arg.Any<CancellationToken>());
        await sender.DidNotReceive().SendAsync(motionOnly, Recording, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_Event_Is_Sent_Once()
    {
        using var sut = CreateSut(new WebhookEndpoint { Url = "https://all.test/hook" });
        sut.Enqueue(Motion);

        await sut.DoWorkAsync(TestContext.Current.CancellationToken);
        await sut.DoWorkAsync(TestContext.Current.CancellationToken);

        await sender.ReceivedWithAnyArgs(1).SendAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Without_Endpoints_Nothing_Is_Queued_Or_Sent()
    {
        using var sut = CreateSut();

        sut.Enqueue(Motion).Should().BeFalse();
        await sut.DoWorkAsync(TestContext.Current.CancellationToken);

        await sender.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task An_Endpoint_With_An_Invalid_Address_Is_Left_Out()
    {
        var valid = new WebhookEndpoint { Url = "https://all.test/hook" };
        using var sut = CreateSut(new WebhookEndpoint { Url = "nonsense" }, valid);
        sut.Enqueue(Motion);

        await sut.DoWorkAsync(TestContext.Current.CancellationToken);

        await sender.Received(1).SendAsync(valid, Motion, Arg.Any<CancellationToken>());
        await sender.ReceivedWithAnyArgs(1).SendAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task One_Endpoint_Failing_Does_Not_Keep_The_Event_From_The_Others()
    {
        var broken = new WebhookEndpoint { Url = "https://broken.test/hook" };
        var working = new WebhookEndpoint { Url = "https://working.test/hook" };
        sender.SendAsync(broken, Arg.Any<WebhookEvent>(), Arg.Any<CancellationToken>())
            .Returns<Task<bool>>(_ => throw new InvalidOperationException("unexpected"));
        using var sut = CreateSut(broken, working);
        sut.Enqueue(Motion);

        await sut.DoWorkAsync(TestContext.Current.CancellationToken);

        await sender.Received(1).SendAsync(working, Motion, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_Full_Queue_Drops_The_Oldest_Event_Instead_Of_Growing()
    {
        using var sut = CreateSut(new WebhookEndpoint { Url = "https://all.test/hook" });

        for (var i = 0; i < WebhookDispatcherService.QueueCapacity + 25; i++)
        {
            sut.Enqueue(Motion);
        }

        sut.QueuedCount.Should().Be(WebhookDispatcherService.QueueCapacity);
    }

    private WebhookDispatcherService CreateSut(
        params WebhookEndpoint[] endpoints)
        => new(
            NullLogger<WebhookDispatcherService>.Instance,
            new WebhookDispatcherServiceOptions(),
            new WebhookOptions { Endpoints = [.. endpoints] },
            sender,
            Substitute.For<IRecordingService>(),
            Substitute.For<IMotionDetectionService>(),
            Substitute.For<ICameraStorageService>(),
            Substitute.For<IApplicationSettingsService>());
}