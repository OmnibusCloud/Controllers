using System.Diagnostics;
using OutWit.Controller.Render.Utils;

namespace OutWit.Controller.Render.Tests.Utils;

/// <summary>
/// Orphan-process protection (audit A4): children assigned to the kill-on-close job object must
/// die when the job handle closes — which is exactly what happens when the worker process dies
/// for any reason (crash, hard kill), because the production job handle is held for the process
/// lifetime and the OS closes it at death.
/// </summary>
[TestFixture]
[Platform("Win")]
public class ProcessTreeGuardTests
{
    [Test]
    public void KillOnCloseJobKillsChildWhenHandleClosesTest()
    {
        var job = ProcessTreeGuard.CreateKillOnCloseJob();
        Assert.That(job, Is.Not.EqualTo((nint)0), "job object must be creatable");

        using var child = StartLongRunningChild();
        try
        {
            Assert.That(ProcessTreeGuard.AssignToJob(job, child), Is.True, "child must join the job");

            // Closing the job's last handle is what the OS does when the parent dies — the child
            // must be killed by the job, not survive as an orphan.
            ProcessTreeGuard.CloseJob(job);

            Assert.That(child.WaitForExit(5000), Is.True,
                "the child must be killed when the kill-on-close job handle closes");
        }
        finally
        {
            try
            {
                if (!child.HasExited)
                    child.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    [Test]
    public void AttachToParentLifetimeDoesNotDisturbNormalChildTest()
    {
        // The guard must never break a normally-running child: it still runs to completion with
        // its own exit code while the test host (the "parent") stays alive.
        using var child = StartShortLivedChild();

        Assert.DoesNotThrow(() => ProcessTreeGuard.AttachToParentLifetime(child));

        Assert.That(child.WaitForExit(10000), Is.True, "the guarded child must still finish normally");
        Assert.That(child.ExitCode, Is.EqualTo(0));
    }

    #region Tools

    private static Process StartLongRunningChild()
    {
        return Start("cmd.exe", "/c ping -n 60 127.0.0.1 >nul");
    }

    private static Process StartShortLivedChild()
    {
        return Start("cmd.exe", "/c exit 0");
    }

    private static Process Start(string fileName, string args)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        return process;
    }

    #endregion
}
