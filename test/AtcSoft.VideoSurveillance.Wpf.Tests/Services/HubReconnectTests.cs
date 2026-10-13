namespace AtcSoft.VideoSurveillance.Wpf.Services;

/// <summary>
/// The client stays in front of a wall for days. A server that is restarted,
/// or a network that is away for an hour, must not leave it disconnected
/// until someone restarts the client: it keeps trying, soon at first and
/// then every half minute, for as long as it runs.
/// </summary>
public sealed class HubReconnectTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(2, 5)]
    [InlineData(3, 10)]
    [InlineData(4, 20)]
    [InlineData(5, 30)]
    [InlineData(6, 30)]
    [InlineData(5000, 30)]
    public void It_Tries_Again_Soon_At_First_And_Then_Every_Half_Minute(
        int attemptsSoFar,
        int expectedSeconds)
        => HubReconnectPolicy.DelayFor(attemptsSoFar).Should().Be(TimeSpan.FromSeconds(expectedSeconds));

    [Fact]
    public void It_Never_Gives_Up()
    {
        var policy = new HubReconnectPolicy();

        var delay = policy.NextRetryDelay(new Microsoft.AspNetCore.SignalR.Client.RetryContext
        {
            PreviousRetryCount = 100_000,
            ElapsedTime = TimeSpan.FromDays(30),
        });

        delay.Should().Be(TimeSpan.FromSeconds(30), "no answer would mean: stop trying");
    }

    [Fact]
    public async Task A_Server_That_Is_Not_There_At_The_Start_Is_Tried_Until_It_Answers()
    {
        var waits = new List<TimeSpan>();
        var attempts = 0;

        var connected = await HubReconnectPolicy.KeepTryingAsync(
            _ => Task.FromResult(++attempts == 4),
            (delay, _) =>
            {
                waits.Add(delay);
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        connected.Should().BeTrue();
        attempts.Should().Be(4);
        waits.Should().Equal(
            [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20)],
            "the attempt at the start was the one without a wait");
    }

    [Fact]
    public async Task An_Attempt_That_Throws_Counts_As_Not_Connected()
    {
        var attempts = 0;

        var connected = await HubReconnectPolicy.KeepTryingAsync(
            _ => ++attempts < 3 ? throw new InvalidOperationException("No connection could be made.") : Task.FromResult(true),
            (_, _) => Task.CompletedTask,
            TestContext.Current.CancellationToken);

        connected.Should().BeTrue();
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task Trying_Ends_When_The_Client_Is_Closed()
    {
        using var closing = new CancellationTokenSource();
        var attempts = 0;

        var connected = await HubReconnectPolicy.KeepTryingAsync(
            async _ =>
            {
                if (++attempts == 3)
                {
                    await closing.CancelAsync().ConfigureAwait(false);
                }

                return false;
            },
            (_, token) => token.IsCancellationRequested ? Task.FromCanceled(token) : Task.CompletedTask,
            closing.Token);

        connected.Should().BeFalse();
        attempts.Should().Be(3);
    }
}