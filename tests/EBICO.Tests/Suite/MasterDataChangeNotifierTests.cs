using AwesomeAssertions;
using EBICO.Suite.Services;

namespace EBICO.Tests.Suite;

/// <summary>
/// Unit tests for <see cref="MasterDataChangeNotifier"/> (issue #126): the broadcast contract the
/// Stammdaten islands rely on — every subscriber is reached, disposal really unsubscribes, and a
/// failing subscriber does not silence the others.
/// </summary>
public class MasterDataChangeNotifierTests
{
    [Fact]
    public async Task Notify_InvokesEverySubscriber()
    {
        var notifier = new MasterDataChangeNotifier();
        var first = 0;
        var second = 0;
        notifier.Subscribe(() => { first++; return Task.CompletedTask; });
        notifier.Subscribe(() => { second++; return Task.CompletedTask; });

        await notifier.NotifyChangedAsync();

        first.Should().Be(1);
        second.Should().Be(1);
    }

    [Fact]
    public async Task Notify_WithoutSubscribers_DoesNothing()
    {
        var notifier = new MasterDataChangeNotifier();

        await notifier.Invoking(n => n.NotifyChangedAsync()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task Dispose_RemovesSubscription()
    {
        var notifier = new MasterDataChangeNotifier();
        var calls = 0;
        var subscription = notifier.Subscribe(() => { calls++; return Task.CompletedTask; });

        await notifier.NotifyChangedAsync();
        subscription.Dispose();
        await notifier.NotifyChangedAsync();

        calls.Should().Be(1, "the handler must not be invoked after its subscription was disposed");
    }

    [Fact]
    public async Task Dispose_IsIdempotent()
    {
        var notifier = new MasterDataChangeNotifier();
        var survivor = 0;
        var subscription = notifier.Subscribe(() => Task.CompletedTask);
        notifier.Subscribe(() => { survivor++; return Task.CompletedTask; });

        subscription.Dispose();
        subscription.Dispose();

        // A second Dispose must not remove somebody else's handler.
        await notifier.NotifyChangedAsync();
        survivor.Should().Be(1);
    }

    [Fact]
    public async Task Notify_FailingSubscriber_StillReachesTheOthers()
    {
        var notifier = new MasterDataChangeNotifier();
        var reached = 0;
        notifier.Subscribe(() => throw new InvalidOperationException("boom"));
        notifier.Subscribe(() => { reached++; return Task.CompletedTask; });

        var act = async () => await notifier.NotifyChangedAsync();

        (await act.Should().ThrowAsync<AggregateException>())
            .Which.InnerExceptions.Should().ContainSingle().Which.Message.Should().Be("boom");
        reached.Should().Be(1, "a stale island must not stop the remaining ones from refreshing");
    }

    [Fact]
    public async Task Notify_SubscribingDuringBroadcast_DoesNotDisturbTheIteration()
    {
        var notifier = new MasterDataChangeNotifier();
        var added = 0;
        IDisposable? nested = null;
        notifier.Subscribe(() =>
        {
            nested ??= notifier.Subscribe(() => { added++; return Task.CompletedTask; });
            return Task.CompletedTask;
        });

        await notifier.NotifyChangedAsync();

        added.Should().Be(0, "the broadcast runs over a snapshot taken before the first handler ran");

        await notifier.NotifyChangedAsync();
        added.Should().Be(1);
    }

    [Fact]
    public void Subscribe_NullHandler_Throws()
    {
        var notifier = new MasterDataChangeNotifier();

        notifier.Invoking(n => n.Subscribe(null!)).Should().Throw<ArgumentNullException>();
    }
}
