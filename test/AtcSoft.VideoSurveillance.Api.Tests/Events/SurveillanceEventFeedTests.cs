namespace AtcSoft.VideoSurveillance.Api.Events;

public sealed class SurveillanceEventFeedTests
{
    private static readonly Guid Front = Guid.NewGuid();

    [Fact]
    public async Task A_Subscriber_Gets_What_Is_Published_After_It_Subscribed()
    {
        var feed = new SurveillanceEventFeed();
        feed.Publish(Event(1));
        using var subscription = feed.Subscribe(lastSequence: null);

        var sequence = feed.Publish(Event(2));

        subscription.Replayed.Should().BeEmpty();
        var received = await subscription.Live.ReadAsync(TestContext.Current.CancellationToken);
        received.Sequence.Should().Be(sequence);
        received.Event.Should().Be(Event(2));
    }

    [Fact]
    public void A_Subscriber_That_Resumes_Gets_What_Came_After_Its_Last_Event()
    {
        var feed = new SurveillanceEventFeed();
        var first = feed.Publish(Event(1));
        feed.Publish(Event(2));
        feed.Publish(Event(3));

        using var subscription = feed.Subscribe(lastSequence: first);

        subscription.Replayed.Select(e => e.Event).Should().Equal(Event(2), Event(3));
        subscription.Missed.Should().Be(0);
    }

    [Fact]
    public void A_Subscriber_That_Was_Away_Too_Long_Is_Told_How_Many_It_Missed()
    {
        var feed = new SurveillanceEventFeed();
        var first = feed.Publish(Event(0));
        for (var i = 1; i <= SurveillanceEventFeed.ReplayCapacity + 5; i++)
        {
            feed.Publish(Event(i));
        }

        using var subscription = feed.Subscribe(lastSequence: first);

        subscription.Replayed.Should().HaveCount(SurveillanceEventFeed.ReplayCapacity);
        subscription.Missed.Should().Be(5);
    }

    [Fact]
    public async Task A_Subscriber_That_Falls_Behind_Is_Cut_Off_And_The_Others_Go_On()
    {
        var feed = new SurveillanceEventFeed();
        using var slow = feed.Subscribe(lastSequence: null);
        using var fast = feed.Subscribe(lastSequence: null);
        var cancellationToken = TestContext.Current.CancellationToken;

        for (var i = 0; i <= SurveillanceEventFeed.SubscriberCapacity; i++)
        {
            feed.Publish(Event(i));
            _ = await fast.Live.ReadAsync(cancellationToken);
        }

        var drain = async () =>
        {
            while (await slow.Live.WaitToReadAsync(cancellationToken))
            {
                _ = slow.Live.TryRead(out _);
            }
        };
        await drain.Should().ThrowAsync<IOException>();

        feed.Publish(Event(999));
        (await fast.Live.ReadAsync(cancellationToken)).Event.Should().Be(Event(999));
    }

    [Fact]
    public void A_Subscriber_That_Leaves_Gets_Nothing_More()
    {
        var feed = new SurveillanceEventFeed();
        var subscription = feed.Subscribe(lastSequence: null);

        subscription.Dispose();
        feed.Publish(Event(1));

        subscription.Live.TryRead(out _).Should().BeFalse();
    }

    private static SurveillanceEvent Event(int n)
        => new(
            Type: "motionDetected",
            CameraId: Front,
            Timestamp: new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero).AddSeconds(n));
}