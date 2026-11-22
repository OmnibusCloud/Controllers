using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.AI.Corpus.Logging;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Tests.Logging;

/// <summary>
/// The guard Corpus writes to a host's log through: a line reaches the host's logger as the
/// caller wrote it, one call at a time whatever the number of threads and of adapters, and a
/// logger that fails loses the line and nothing else. Every activity adapter of the controller
/// hands the engine's adapter base this guard instead of the host's logger: an adapter added
/// without it fails here.
/// </summary>
[TestFixture]
public sealed class CorpusLoggerTests
{
    #region Fields

    private string m_root = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"witai_corpus_logger_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_root);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        CorpusTestFolders.Delete(m_root);
    }

    #endregion

    #region Guard Tests

    [Test]
    public void AGuardIsNotGuardedTwiceAndNoLoggerIsAQuietOneTest()
    {
        var host = new CorpusTestLoggerUnguarded();

        var guard = CorpusLogger.Wrap(host);
        var none = CorpusLogger.Wrap(null);

        Assert.Multiple(() =>
        {
            Assert.That(guard, Is.InstanceOf<CorpusLogger>());
            Assert.That(CorpusLogger.Wrap(guard), Is.SameAs(guard), "a guard over a guard would take the gate twice for nothing");
            Assert.That(none, Is.InstanceOf<CorpusLogger>());
            Assert.That(() => none.LogWarning("nobody reads this"), Throws.Nothing);
        });
    }

    [Test]
    public void ALineReachesTheHostsLoggerAsItWasWrittenTest()
    {
        var host = new CorpusTestLoggerUnguarded();
        var guard = CorpusLogger.Wrap(host);

        guard.LogInformation("Corpus job {JobId}: {Shards} shard(s)", "j-1", 12);
        guard.LogWarning(new IOException("the cause"), "a fault ({Kind})", "IOException");

        Assert.Multiple(() =>
        {
            Assert.That(host.Lines, Is.EqualTo(new[] { "Corpus job j-1: 12 shard(s)", "a fault (IOException)" }));
            Assert.That(guard.IsEnabled(LogLevel.Information), Is.True);
            Assert.That(host.Overlaps, Is.Zero);
        });
    }

    [Test]
    public void AHostsLoggerThatFailsLosesTheLineAndNothingElseTest()
    {
        var host = new CorpusTestLoggerThrowing();
        var guard = CorpusLogger.Wrap(host);
        var lostBefore = CorpusLogger.Lost;

        Assert.Multiple(() =>
        {
            Assert.That(() => guard.LogInformation("a line"), Throws.Nothing);
            Assert.That(() => guard.LogError(new InvalidOperationException("a cause"), "another"), Throws.Nothing);
            Assert.That(guard.IsEnabled(LogLevel.Error), Is.False, "a logger that cannot answer is not asked to write");
            Assert.That(guard.BeginScope("a scope"), Is.Null);
        });

        Assert.Multiple(() =>
        {
            Assert.That(host.Calls, Is.EqualTo(4), "every call was made: the guard does not guess");
            Assert.That(CorpusLogger.Lost - lostBefore, Is.GreaterThanOrEqualTo(4), "and every failure is counted");
        });
    }

    [Test]
    public void AScopeOfTheHostsLoggerIsHandedOutAndEndsOnceTest()
    {
        var guard = CorpusLogger.Wrap(NullLogger.Instance);

        var scope = guard.BeginScope("a scope");

        Assert.Multiple(() =>
        {
            Assert.That(scope, Is.InstanceOf<CorpusLoggerScope>());
            Assert.That(() => scope!.Dispose(), Throws.Nothing);
            Assert.That(() => scope!.Dispose(), Throws.Nothing, "ending it twice is ending it once");
        });
    }

    [Test]
    public void CallsFromManyThreadsAndManyAdaptersReachTheHostOneAtATimeTest()
    {
        // The host's logger is one object; every adapter of the controller has a guard of its own
        // over it. The gate is the controller's, not the guard's: two guards over one logger take turns.
        const int threads = 16;
        const int lines = 40;
        var host = new CorpusTestLoggerUnguarded();
        var guards = new[] { CorpusLogger.Wrap(host), CorpusLogger.Wrap(host), CorpusLogger.Wrap(host) };
        var thrown = 0;
        using var start = new ManualResetEventSlim(false);

        var workers = Enumerable.Range(0, threads).Select(thread => new Thread(() =>
        {
            start.Wait();
            for (var line = 0; line < lines; line++)
            {
                try
                {
                    guards[(thread + line) % guards.Length].LogInformation("thread {Thread} line {Line}", thread, line);
                }
                catch (Exception)
                {
                    // Counted, not let out: an exception on a thread of its own would end the test host.
                    Interlocked.Increment(ref thrown);
                }
            }
        })).ToList();
        workers.ForEach(worker => worker.Start());
        start.Set();
        workers.ForEach(worker => worker.Join());

        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.Zero, "no call threw into its caller");
            Assert.That(host.Overlaps, Is.Zero, "no call found another one inside the host's logger");
            Assert.That(host.Lines, Has.Count.EqualTo(threads * lines), "and no line was lost");
            Assert.That(host.Lines.Distinct().Count(), Is.EqualTo(threads * lines));
        });
    }

    [Test]
    public void TheSameCallsWithoutTheGuardDoMeetTest()
    {
        // The other half of the test above: the logger it trusts does notice two callers at once.
        const int threads = 16;
        const int lines = 40;
        var host = new CorpusTestLoggerUnguarded();
        var failed = 0;
        using var start = new ManualResetEventSlim(false);

        var workers = Enumerable.Range(0, threads).Select(thread => new Thread(() =>
        {
            start.Wait();
            for (var line = 0; line < lines; line++)
            {
                try
                {
                    host.LogInformation("thread {Thread} line {Line}", thread, line);
                }
                catch (InvalidOperationException)
                {
                    Interlocked.Increment(ref failed);
                }
            }
        })).ToList();
        workers.ForEach(worker => worker.Start());
        start.Set();
        workers.ForEach(worker => worker.Join());

        Assert.Multiple(() =>
        {
            Assert.That(host.Overlaps, Is.GreaterThan(0));
            Assert.That(failed, Is.EqualTo(host.Overlaps), "each overlap is a call that threw into its caller");
        });
    }

    #endregion

    #region Adapter Tests

    [Test]
    public void EveryAdapterOfTheControllerWritesThroughTheGuardTest()
    {
        var adapters = typeof(WitControllerCorpusModule).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsClass: true } && typeof(IWitActivityAdapter).IsAssignableFrom(type))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToList();
        var host = new CorpusTestLoggerThrowing();
        var unguarded = new List<string>();

        foreach (var type in adapters)
        {
            var constructor = type.GetConstructors().Single();
            var adapter = constructor.Invoke(constructor.GetParameters().Select(parameter => Argument(parameter.ParameterType, host)).ToArray());
            var logger = type.GetProperty("Logger", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(adapter);

            if (logger is not CorpusLogger)
                unguarded.Add(type.Name);
        }

        Assert.Multiple(() =>
        {
            Assert.That(adapters, Has.Count.EqualTo(12), "the controller's twelve activities");
            Assert.That(unguarded, Is.Empty, "an adapter that hands the engine's base the host's logger lets a failing logger decide its activity's fate");
        });
    }

    #endregion

    #region Tools

    private object Argument(Type type, ILogger logger)
    {
        if (type == typeof(ILogger))
            return logger;
        if (type == typeof(IWitProcessingManager))
            return new CorpusTestProcessingManager();
        if (type == typeof(IWitBlobService))
            return new CorpusTestBlobService(Path.Combine(m_root, "blobs"));
        if (type == typeof(IWitTempStorage))
            return new WitTempStorageDefault(Path.Combine(m_root, "temp"));
        if (type == typeof(IServiceProvider))
            return new ServiceCollection().BuildServiceProvider();

        throw new InvalidOperationException($"The test cannot make a {type.Name} for an adapter's constructor: teach it.");
    }

    #endregion
}
