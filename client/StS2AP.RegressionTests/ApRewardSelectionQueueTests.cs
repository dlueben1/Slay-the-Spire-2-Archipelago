using StS2AP.Utils;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class ApRewardSelectionQueueTests
{
    [Fact]
    public async Task FollowingRevealWaitsForEarlierRelicGrantAcrossAwait()
    {
        var queue = new ApRewardSelectionQueue();
        var release = new TaskCompletionSource();
        bool hasRelic = false;
        Task<bool> grant = queue.Run(async () => { await release.Task; hasRelic = true; return true; });
        Task<bool> reveal = queue.Run(() => Task.FromResult(hasRelic));
        Assert.False(reveal.IsCompleted);
        release.SetResult();
        Assert.True(await grant);
        Assert.True(await reveal);
    }

    [Fact]
    public async Task DelayedReplicaExecutesRevealOrderRatherThanReceiptOrder()
    {
        var owner = new ApRewardSelectionQueue();
        var replica = new ApRewardSelectionQueue();
        var release = new TaskCompletionSource();
        var local = new List<int>();
        var remote = new List<int>();
        Task<bool> delayed = replica.Run(async () => { await release.Task; return true; });
        var pending = new List<Task<bool>>();
        foreach (int receipt in new[] { 81, 80, 81, 2 })
        {
            await owner.Run(() => { local.Add(receipt); return Task.FromResult(false); });
            pending.Add(replica.Run(() => { remote.Add(receipt); return Task.FromResult(false); }));
        }
        Assert.Empty(remote);
        release.SetResult();
        await delayed;
        await Task.WhenAll(pending);
        Assert.Equal(local, remote);
    }

    [Fact]
    public async Task SkipAllowsLaterSelectionsButFailureStopsQueuedWork()
    {
        var queue = new ApRewardSelectionQueue();
        Assert.False(await queue.Run(() => Task.FromResult(false)));
        var release = new TaskCompletionSource();
        Task<bool> failed = queue.Run(async () => { await release.Task; throw new InvalidOperationException("mismatch"); });
        bool ran = false;
        Task<bool> next = queue.Run(() => { ran = true; return Task.FromResult(true); });
        release.SetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => failed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => next);
        Assert.False(ran);
    }

    [Fact]
    public async Task ReopenedMenuWaitsForTheExistingSelectionToFinish()
    {
        var queue = new ApRewardSelectionQueue();
        var release = new TaskCompletionSource();
        Task<bool> selection = queue.Run(async () => { await release.Task; return false; });
        Task reopenedMenu = queue.WhenIdle();
        Assert.False(reopenedMenu.IsCompleted);
        release.SetResult();
        await selection;
        await reopenedMenu;
    }

    [Fact]
    public async Task OnePlayerWaitingDoesNotBlockAnotherPlayersQueue()
    {
        var release = new TaskCompletionSource();
        Task<bool> waiting = new ApRewardSelectionQueue().Run(async () => { await release.Task; return false; });
        Assert.True(await new ApRewardSelectionQueue().Run(() => Task.FromResult(true)));
        Assert.False(waiting.IsCompleted);
        release.SetResult();
        Assert.False(await waiting);
    }
}
