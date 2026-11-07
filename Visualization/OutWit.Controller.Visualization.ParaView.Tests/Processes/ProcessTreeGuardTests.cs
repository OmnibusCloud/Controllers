using System.Diagnostics;
using OutWit.Controller.Visualization.ParaView.Processes;

namespace OutWit.Controller.Visualization.ParaView.Tests.Processes;

/// <summary>
/// The kill-on-close job object (audit wave 2 — internal seams "so tests can verify", none
/// compiled): on Windows a child assigned to the job dies when the job handle closes, which is
/// what happens to the guard's handle when this process dies; off Windows the guard is a no-op
/// and the runners' parent watchdog (C-H2) takes over.
/// </summary>
[TestFixture]
public sealed class ProcessTreeGuardTests
{
    #region Tests

    [Test]
    [Platform(Include = "Win")]
    public void ChildAssignedToTheJobDiesWhenTheJobClosesTest()
    {
        var job = ProcessTreeGuard.CreateKillOnCloseJob();
        Assert.That(job, Is.Not.EqualTo((nint)0), "a kill-on-close job object is created on Windows");

        using var child = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 60 127.0.0.1 > nul")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        })!;

        try
        {
            Assert.That(ProcessTreeGuard.AssignToJob(job, child), Is.True, "the child joins the job");
            Assert.That(child.HasExited, Is.False, "the child runs while the job is open");

            ProcessTreeGuard.CloseJob(job);

            Assert.That(child.WaitForExit(TimeSpan.FromSeconds(10)), Is.True, "closing the job kills the child");
        }
        finally
        {
            if (!child.HasExited)
                child.Kill(entireProcessTree: true);
        }
    }

    [Test]
    [Platform(Include = "Win")]
    public void AttachToParentLifetimeAssignsWithoutThrowingTest()
    {
        using var child = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        })!;

        Assert.DoesNotThrow(() => ProcessTreeGuard.AttachToParentLifetime(child));
        child.WaitForExit(TimeSpan.FromSeconds(10));
    }

    [Test]
    [Platform(Exclude = "Win")]
    public void GuardIsANoOpOffWindowsTest()
    {
        Assert.That(ProcessTreeGuard.CreateKillOnCloseJob(), Is.EqualTo((nint)0));

        using var child = Process.Start(new ProcessStartInfo("/bin/sh", "-c \"exit 0\"") { UseShellExecute = false })!;
        Assert.DoesNotThrow(() => ProcessTreeGuard.AttachToParentLifetime(child));
        child.WaitForExit(TimeSpan.FromSeconds(10));
    }

    #endregion
}
