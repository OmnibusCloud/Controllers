using System.Runtime.InteropServices;
using OutWit.Controller.Visualization.ParaView.Runtime;

namespace OutWit.Controller.Visualization.ParaView.Tests.Runtime;

/// <summary>
/// The allowlisted, task-private environment of the pvpython child.
/// </summary>
[TestFixture]
public sealed class ParaViewRunnerEnvironmentTests
{
    #region Fields

    private string m_dir = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_dir = Path.Combine(Path.GetTempPath(), $"pv_resolve_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_dir);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(ParaViewBinaryResolver.ENV_PVPYTHON_PATH, null);
        if (Directory.Exists(m_dir))
            Directory.Delete(m_dir, recursive: true);
    }

    #endregion

    #region Tests

    [Test]
    public void EnvironmentIsAllowlistedAndTaskPrivateTest()
    {
        Environment.SetEnvironmentVariable("DISPLAY", ":0");
        Environment.SetEnvironmentVariable("PV_PLUGIN_PATH", "/evil");
        Environment.SetEnvironmentVariable("OMNIBUSCLOUD_API_KEY_TEST_LEAK", "secret");
        try
        {
            var home = Path.Combine(m_dir, "home");
            var temp = Path.Combine(m_dir, "tmp");
            var pvpython = Path.Combine(m_dir, "rt", "bin", "pvpython");
            var environment = ParaViewRunnerEnvironment.Build(pvpython, home, temp, ParaViewRunnerEnvironment.OSMESA_WINDOW);

            Assert.Multiple(() =>
            {
                Assert.That(environment.Keys, Does.Not.Contain("DISPLAY"));
                Assert.That(environment.Keys, Does.Not.Contain("PV_PLUGIN_PATH"));
                Assert.That(environment.Keys, Does.Not.Contain("OMNIBUSCLOUD_API_KEY_TEST_LEAK"));
                Assert.That(environment["PATH"], Does.StartWith(Path.GetDirectoryName(pvpython)!));
                Assert.That(environment[ParaViewRunnerEnvironment.VTK_DEFAULT_OPENGL_WINDOW], Is.EqualTo(ParaViewRunnerEnvironment.OSMESA_WINDOW));
                Assert.That(environment["PYTHONNOUSERSITE"], Is.EqualTo("1"));
                Assert.That(environment["PYTHONDONTWRITEBYTECODE"], Is.EqualTo("1"));

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Assert.That(environment["USERPROFILE"], Is.EqualTo(home));
                    Assert.That(environment["APPDATA"], Is.EqualTo(home));
                    Assert.That(environment["TEMP"], Is.EqualTo(temp));
                    Assert.That(environment.Keys, Does.Contain("SystemRoot"));
                }
                else
                {
                    Assert.That(environment["HOME"], Is.EqualTo(home));
                    Assert.That(environment["TMPDIR"], Is.EqualTo(temp));
                    Assert.That(environment["LC_ALL"], Is.EqualTo("C.UTF-8"));
                }
            });

            var withoutSoftware = ParaViewRunnerEnvironment.Build(pvpython, home, temp, openGlWindow: null);
            Assert.That(withoutSoftware.Keys, Does.Not.Contain(ParaViewRunnerEnvironment.VTK_DEFAULT_OPENGL_WINDOW));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DISPLAY", null);
            Environment.SetEnvironmentVariable("PV_PLUGIN_PATH", null);
            Environment.SetEnvironmentVariable("OMNIBUSCLOUD_API_KEY_TEST_LEAK", null);
        }
    }

    #endregion
}
