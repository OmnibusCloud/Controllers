using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.AI.Verify.Adapters;
using OutWit.Controller.AI.Verify.Tests.Mock;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Tests.Adapters;

/// <summary>
/// The logger the adapters log through (hardening batch C, item C6): a logging call never
/// decides what becomes of a package. On cloud clients up to 2.2.5 the host's logger was an
/// in-memory list without a lock; it threw under a share of several packages, and when it threw
/// inside the catch block of the engine's adapter base its exception left the node in place of
/// the fixed words. These tests hold the two halves of the guard apart (the logger's exception
/// goes no further; two threads are never inside the logger at once) and hold every adapter of
/// the controller to it.
/// </summary>
[TestFixture]
public sealed class VerifyLoggerGuardedTests
{
    #region Constants

    private const int THREADS = 16;
    private const int LINES_A_THREAD = 2_000;

    private const string LOGGER_PROPERTY = "Logger";

    #endregion

    #region Guard Tests

    [Test]
    public void C6ALineReachesTheHostsLoggerAsItWasGivenTest()
    {
        var host = new VerifyUnlockedLogger();
        var logger = VerifyLoggerGuarded.Wrap(host);

        logger.LogWarning(new InvalidOperationException("why"), "task {Index} has no verdict", 7);
        logger.LogInformation("done");

        Assert.Multiple(() =>
        {
            Assert.That(host.Lines, Is.EqualTo(new[] { "Warning: task 7 has no verdict", "Information: done" }));
            Assert.That(logger.IsEnabled(LogLevel.Debug), Is.True);
            Assert.That(logger.BeginScope("scope"), Is.Null, "a logger without scopes gives none");
        });
    }

    [Test]
    public void C6AnExceptionOfTheHostsLoggerGoesNoFurtherTest()
    {
        var host = new VerifyThrowingLogger();
        var logger = VerifyLoggerGuarded.Wrap(host);
        var before = VerifyLoggerGuarded.Swallowed;

        Assert.Multiple(() =>
        {
            Assert.That(() => logger.LogError(new IOException("disk"), "a package failed on this node"), Throws.Nothing);
            Assert.That(() => logger.LogInformation("a line"), Throws.Nothing);
            Assert.That(logger.IsEnabled(LogLevel.Error), Is.False, "a logger that cannot say is taken as off");
            Assert.That(logger.BeginScope("scope"), Is.Null, "a scope the logger could not open is no scope");
        });

        Assert.Multiple(() =>
        {
            Assert.That(host.Calls, Is.EqualTo(4), "the logger was asked every time");
            Assert.That(VerifyLoggerGuarded.Swallowed - before, Is.EqualTo(4), "and every one of its exceptions is counted");
        });
    }

    [Test]
    public void C6AScopeThatThrowsWhenItEndsEndsQuietlyTest()
    {
        var logger = VerifyLoggerGuarded.Wrap(new VerifyScopeLogger());
        var before = VerifyLoggerGuarded.Swallowed;

        var scope = logger.BeginScope("scope");

        Assert.That(scope, Is.Not.Null);
        Assert.That(() => scope!.Dispose(), Throws.Nothing);
        Assert.That(() => scope!.Dispose(), Throws.Nothing, "ended once");
        Assert.That(VerifyLoggerGuarded.Swallowed - before, Is.EqualTo(1));
    }

    [Test]
    public void C6ThreadsAreNeverInsideTheHostsLoggerTogetherTest()
    {
        // The unlocked logger notices a second thread for certain: it counts it and throws, as
        // the client's list did. Straight into it, sixteen threads meet at once; through the
        // guard none does, and no line is lost.
        var host = new VerifyUnlockedLogger();
        var logger = VerifyLoggerGuarded.Wrap(host);

        Hammer(logger);

        Assert.Multiple(() =>
        {
            Assert.That(host.Overlaps, Is.Zero, "two threads were inside the host's logger at once");
            Assert.That(host.Lines, Has.Count.EqualTo(THREADS * LINES_A_THREAD), "every line arrived");
        });
    }

    [Test]
    public void C6TheUnlockedLoggerBreaksWithoutTheGuardTest()
    {
        // The control of the test above: the same lines straight into the same logger. Were this
        // to pass without overlaps, the test above would prove nothing.
        var host = new VerifyUnlockedLogger();
        var thrown = 0;

        Hammer(host, () => Interlocked.Increment(ref thrown));

        Assert.Multiple(() =>
        {
            Assert.That(host.Overlaps, Is.GreaterThan(0), "sixteen threads never met inside an unlocked logger: the control is broken");
            Assert.That(thrown, Is.EqualTo(host.Overlaps), "every meeting threw into the caller");
        });
    }

    [Test]
    public void C6ALoggerIsGuardedOnceAndAMissingOneKeepsNothingTest()
    {
        var guarded = VerifyLoggerGuarded.Wrap(NullLogger.Instance);

        Assert.Multiple(() =>
        {
            Assert.That(VerifyLoggerGuarded.Wrap(guarded), Is.SameAs(guarded), "a guarded logger is not wrapped again");
            Assert.That(() => VerifyLoggerGuarded.Wrap(null).LogInformation("a host without a logger"), Throws.Nothing);
        });
    }

    [Test]
    public void C6EveryAdapterLogsThroughTheGuardTest()
    {
        // The engine's adapter base logs through the logger its constructor was given, in its own
        // catch block too: an adapter that hands it the host's logger as it came is the leak.
        var adapters = typeof(VerifyLoggerGuarded).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IWitProcessingAdapter).IsAssignableFrom(type))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToList();

        Assert.That(adapters, Has.Count.EqualTo(14), "the adapters of the controller, as its module registers them");

        Assert.Multiple(() =>
        {
            foreach (var type in adapters)
            {
                var adapter = Create(type, new VerifyThrowingLogger());
                var logger = LoggerOf(adapter);

                Assert.That(logger, Is.InstanceOf<VerifyLoggerGuarded>(), $"{type.Name} hands the engine the host's logger unguarded");
            }
        });
    }

    #endregion

    #region Tools

    private static void Hammer(ILogger logger, Action? onThrown = null)
    {
        using var start = new ManualResetEventSlim();
        var threads = Enumerable.Range(0, THREADS).Select(thread => new Thread(() =>
        {
            start.Wait();
            for (var line = 0; line < LINES_A_THREAD; line++)
            {
                try
                {
                    logger.LogInformation("thread {Thread} line {Line}", thread, line);
                }
                catch (IndexOutOfRangeException)
                {
                    onThrown?.Invoke();
                }
            }
        })).ToList();

        foreach (var thread in threads)
            thread.Start();

        start.Set();
        foreach (var thread in threads)
            thread.Join();
    }

    private static object Create(Type adapter, ILogger logger)
    {
        var constructor = adapter.GetConstructors().Single();
        var arguments = constructor.GetParameters().Select(parameter => Argument(parameter.ParameterType, logger)).ToArray();
        return constructor.Invoke(arguments);
    }

    private static object Argument(Type type, ILogger logger)
    {
        if (type == typeof(ILogger))
            return logger;
        if (type == typeof(IWitProcessingManager))
            return new VerifyTestProcessingManager();
        if (type == typeof(IWitBlobService))
            return new VerifyThrowingBlobService("no blob travels in this test");
        if (type == typeof(IServiceProvider))
            return new VerifyNoServices();

        throw new InvalidOperationException($"an adapter asks for a {type.Name}: teach this test to give one");
    }

    private static object? LoggerOf(object adapter)
    {
        for (var type = adapter.GetType(); type != null; type = type.BaseType)
        {
            var property = type.GetProperty(LOGGER_PROPERTY, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (property != null)
                return property.GetValue(adapter);
        }

        throw new InvalidOperationException($"{adapter.GetType().Name} has no {LOGGER_PROPERTY}: the engine's adapter base changed");
    }

    #endregion
}
