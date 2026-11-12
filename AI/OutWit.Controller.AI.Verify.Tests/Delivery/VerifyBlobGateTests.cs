using OutWit.Controller.AI.Verify.Delivery;

namespace OutWit.Controller.AI.Verify.Tests.Delivery;

/// <summary>
/// The gate in front of the node's blob channel: transfers pass one at a time until one
/// succeeds, side by side after that, and one at a time again after a failure.
/// </summary>
[TestFixture]
public sealed class VerifyBlobGateTests
{
    #region Constants

    private const int TRANSFERS = 6;

    #endregion

    #region Fields

    private int m_inFlight;
    private int m_peak;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_inFlight = 0;
        m_peak = 0;
    }

    #endregion

    #region Gate Tests

    [Test]
    public async Task TheFirstTransfersPassOneAtATimeTest()
    {
        var gate = new VerifyBlobGate();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Six transfers arrive together; the first holds the channel until it is let go.
        var transfers = Enumerable.Range(0, TRANSFERS)
            .Select(index => gate.RunAsync(() => TransferAsync(index == 0 ? release.Task : Task.CompletedTask), CancellationToken.None))
            .ToList();

        await Task.Delay(100);
        Assert.Multiple(() =>
        {
            Assert.That(m_peak, Is.EqualTo(1), "nothing joins the first transfer");
            Assert.That(gate.IsOpen, Is.False);
        });

        release.SetResult();
        await Task.WhenAll(transfers);

        Assert.That(gate.IsOpen, Is.True);
    }

    [Test]
    public async Task AfterASuccessTransfersRunSideBySideTest()
    {
        var gate = new VerifyBlobGate();
        await gate.RunAsync(() => TransferAsync(Task.CompletedTask), CancellationToken.None);
        m_peak = 0;

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transfers = Enumerable.Range(0, TRANSFERS)
            .Select(_ => gate.RunAsync(() => TransferAsync(release.Task), CancellationToken.None))
            .ToList();

        await Task.Delay(100);
        Assert.That(m_peak, Is.EqualTo(TRANSFERS));

        release.SetResult();
        await Task.WhenAll(transfers);
    }

    [Test]
    public async Task AFailedFirstTransferLeavesTheGateToTheNextTest()
    {
        var gate = new VerifyBlobGate();

        Assert.ThrowsAsync<IOException>(() => gate.RunAsync<int>(() => throw new IOException("no channel"), CancellationToken.None));
        Assert.That(gate.IsOpen, Is.False);

        var result = await gate.RunAsync(() => TransferAsync(Task.CompletedTask), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(1));
            Assert.That(gate.IsOpen, Is.True);
        });
    }

    [Test]
    public async Task AFailureAfterTheGateOpenedClosesItAgainTest()
    {
        var gate = new VerifyBlobGate();
        await gate.RunAsync(() => TransferAsync(Task.CompletedTask), CancellationToken.None);

        Assert.ThrowsAsync<IOException>(() => gate.RunAsync<int>(() => throw new IOException("the channel went away"), CancellationToken.None));

        Assert.That(gate.IsOpen, Is.False, "the next transfers reconnect one at a time");
    }

    [Test]
    public void WaitingForTheGateStopsWithTheJobTest()
    {
        var gate = new VerifyBlobGate();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = gate.RunAsync(() => TransferAsync(release.Task), CancellationToken.None);

        using var cancelled = new CancellationTokenSource();
        var second = gate.RunAsync(() => TransferAsync(Task.CompletedTask), cancelled.Token);
        cancelled.Cancel();

        Assert.That(async () => await second, Throws.InstanceOf<OperationCanceledException>());

        release.SetResult();
        Assert.That(async () => await first, Throws.Nothing);
    }

    #endregion

    #region Tools

    private async Task<int> TransferAsync(Task hold)
    {
        var now = Interlocked.Increment(ref m_inFlight);
        InterlockedMax(ref m_peak, now);
        try
        {
            await hold;
            return 1;
        }
        finally
        {
            Interlocked.Decrement(ref m_inFlight);
        }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        do
        {
            seen = Volatile.Read(ref target);
            if (value <= seen)
                return;
        } while (Interlocked.CompareExchange(ref target, value, seen) != seen);
    }

    #endregion
}
