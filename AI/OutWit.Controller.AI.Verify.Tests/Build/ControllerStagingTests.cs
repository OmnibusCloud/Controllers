using OutWit.Controller.AI.Verify.Tests.Mock;

namespace OutWit.Controller.AI.Verify.Tests.Build;

/// <summary>
/// What the shared build files do with a module folder (<c>Build/OutWit.Controller.Staging.targets</c>),
/// held on a solution of its own (<see cref="ControllerStagingProbe"/>) that is built twice:
/// <list type="number">
/// <item>a build records what it staged, outside the module folders;</item>
/// <item>the next build removes only what the record lists, it no longer stages and nobody has
/// changed since: a file and a folder that were put into the module by hand are there after both
/// builds;</item>
/// <item>the zip and the package hold the recorded files, not whatever the folder holds, and
/// the content digest is that of what the package ships.</item>
/// </list>
/// The module folder is not the build's alone. People keep copies of large tools there for
/// local runs; a build that cleaned the folder would cost them hours, and one that packed the
/// folder would ship whatever lay in it.
/// </summary>
[TestFixture]
public sealed class ControllerStagingTests
{
    #region Constants

    private const string BY_HAND_FILE = "HAND.txt";
    private const string BY_HAND_FOLDER_FILE = "byhand/deep/keep.bin";
    private const string BEFORE_THE_RECORD = "Old.Helper.dll";
    private const string BY_HAND_ASSET = ControllerStagingProbe.ASSET_FOLDER + "tool.bin";
    private const string BY_HAND_RESOURCE = "Resources/by-hand.dat";
    private const string SWAPPED = ControllerStagingProbe.SWAPPED_FILE;
    private const string MANIFEST = "controller.json";

    // Put into the module before the first build: no record ever lists them.
    private static readonly string[] BY_HAND = [BY_HAND_FILE, BY_HAND_FOLDER_FILE, BEFORE_THE_RECORD, BY_HAND_ASSET, BY_HAND_RESOURCE];

    private static readonly string[] MODEL_STAGED =
    [
        ControllerStagingProbe.MODEL + ".deps.json",
        ControllerStagingProbe.MODEL + ".dll"
    ];

    // What the controller's project stages while all of its removable sources are there.
    private static readonly string[] CONTROLLER_STAGED =
    [
        ControllerStagingProbe.CONTROLLER + ".deps.json",
        ControllerStagingProbe.CONTROLLER + ".dll",
        ControllerStagingProbe.DROPPED_RESOURCE,
        ControllerStagingProbe.KEPT_RESOURCE,
        MANIFEST,
        ControllerStagingProbe.EXTRA_FILE,
        SWAPPED
    ];

    // Sources taken away between the builds. The second build removes what the first staged of them.
    private static readonly string[] TAKEN_AWAY = [ControllerStagingProbe.EXTRA_FILE, ControllerStagingProbe.DROPPED_RESOURCE];

    // No longer staged by the second build: the above, and the file somebody replaced in the
    // module after the first build, which is theirs now and stays.
    private static readonly string[] NO_LONGER_STAGED = [.. TAKEN_AWAY, SWAPPED];

    #endregion

    #region Fields

    private ControllerStagingProbe m_probe = null!;
    private ControllerStagingState m_first = null!;
    private ControllerStagingState m_second = null!;
    private string m_secondBuild = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void BuildTwice()
    {
        var root = VerifyRigModules.SolutionRoot();
        if (root == null)
            VerifyRigModules.Missing("Solution root not found");

        m_probe = new ControllerStagingProbe(root);

        // Before any build, so before any record: what a person keeps in a module folder, and
        // what a build of an older day left there.
        foreach (var inside in BY_HAND)
            m_probe.PutByHand(inside, Content(inside));

        m_probe.Build();
        m_first = m_probe.Read();

        // Between the builds: two sources go, and so does the source of a file that somebody
        // has meanwhile replaced in the module with one of their own.
        foreach (var source in NO_LONGER_STAGED)
            m_probe.TakeAway(source);

        m_probe.PutByHand(SWAPPED, Content(SWAPPED));

        m_secondBuild = m_probe.Build();
        m_second = m_probe.Read();
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        m_probe?.Dispose();
    }

    #endregion

    #region Tests

    [Test]
    public void ABuildRecordsWhatItStagedOutsideTheModuleTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(m_first.Records.Keys, Is.EquivalentTo(new[] { ControllerStagingProbe.CONTROLLER, ControllerStagingProbe.MODEL }), "one record a project that stages");
            Assert.That(m_first.Records.GetValueOrDefault(ControllerStagingProbe.CONTROLLER), Is.EqualTo(InModule(CONTROLLER_STAGED)), "the controller's record after the first build");
            Assert.That(m_first.Records.GetValueOrDefault(ControllerStagingProbe.MODEL), Is.EqualTo(InModule(MODEL_STAGED)), "the Model's record: it stages into the controller's module from a project of its own");

            Assert.That(m_second.Records.GetValueOrDefault(ControllerStagingProbe.CONTROLLER), Is.EqualTo(InModule(CONTROLLER_STAGED.Except(NO_LONGER_STAGED))), "the record follows the build: what is no longer staged is no longer listed");
            Assert.That(m_second.Records.GetValueOrDefault(ControllerStagingProbe.MODEL), Is.EqualTo(InModule(MODEL_STAGED)));

            // The records are no part of a module: the folder holds what was staged and what was put there by hand.
            Assert.That(m_first.Folder.Keys, Is.EquivalentTo(CONTROLLER_STAGED.Concat(MODEL_STAGED).Concat(BY_HAND)), "the module folder after the first build");
            Assert.That(m_probe.Records, Does.Not.StartWith(m_probe.Module), "the records lie outside the module folder");
        });
    }

    [Test]
    public void ABuildRemovesOnlyWhatItsRecordListsAndItNoLongerStagesTest()
    {
        Assert.Multiple(() =>
        {
            // Removed: what the first build staged and the second does not.
            foreach (var gone in TAKEN_AWAY)
            {
                Assert.That(m_first.Folder.Keys, Does.Contain(gone), $"{gone} was staged by the first build");
                Assert.That(m_second.Folder.Keys, Does.Not.Contain(gone), $"{gone} is no longer staged and was not removed from the module");
                Assert.That(m_secondBuild, Does.Contain("removed " + ControllerStagingProbe.MODULE + "/" + gone), "the build says what it removed");
            }

            // Kept: everything no record lists. A file and a folder put there by hand, a file of a
            // declared data asset's folder, a file among the resources, an assembly from before
            // records were kept: after two builds, with their bytes.
            foreach (var kept in BY_HAND)
            {
                Assert.That(m_first.Folder.GetValueOrDefault(kept), Is.EqualTo(Content(kept)), $"{kept} was put into the module by hand and the first build did not leave it as it was");
                Assert.That(m_second.Folder.GetValueOrDefault(kept), Is.EqualTo(Content(kept)), $"{kept} was put into the module by hand and the second build did not leave it as it was");
            }

            // Kept: a file the first build staged, which somebody replaced with their own before
            // its source went. The record lists the path, not these bytes: it is not the build's to remove.
            Assert.That(m_first.Folder.GetValueOrDefault(SWAPPED), Is.Not.EqualTo(Content(SWAPPED)), "the first build staged the project's own file");
            Assert.That(m_second.Folder.GetValueOrDefault(SWAPPED), Is.EqualTo(Content(SWAPPED)), $"{SWAPPED} was replaced in the module by hand and the second build did not leave it as it was");
            Assert.That(m_secondBuild, Does.Contain("left " + ControllerStagingProbe.MODULE + "/" + SWAPPED + " alone"), "the build says what it left alone and why");

            Assert.That(m_second.Folder.Keys, Is.EquivalentTo(CONTROLLER_STAGED.Except(TAKEN_AWAY).Concat(MODEL_STAGED).Concat(BY_HAND)), "the module folder after the second build");
        });
    }

    [Test]
    public void TheZipAndThePackageHoldWhatTheBuildStagedTest()
    {
        Assert.Multiple(() =>
        {
            Holds(m_first, CONTROLLER_STAGED, [], "the first build");
            Holds(m_second, CONTROLLER_STAGED.Except(NO_LONGER_STAGED), [SWAPPED], "the second build");

            Assert.That(m_second.StatedDigest, Is.Not.EqualTo(m_first.StatedDigest), "the digest follows what ships: files left the module between the builds");
        });
    }

    #endregion

    #region Tools

    private static void Holds(ControllerStagingState state, IEnumerable<string> controllerStaged, string[] alsoByHand, string build)
    {
        var staged = controllerStaged.Concat(MODEL_STAGED).ToArray();

        // The zip: the recorded files, and what lies under the folder a declared data asset
        // extracts to (a copy for local runs, kept in the zip as it always was).
        Assert.That(state.Zipped, Is.EqualTo(staged.Append(BY_HAND_ASSET).Order(StringComparer.Ordinal)), $"the zip of {build}");

        // The package: the recorded files without Resources/, which it has always left out. No data asset, nothing by hand.
        Assert.That(state.Packed, Is.EqualTo(staged.Where(file => !file.StartsWith("Resources/", StringComparison.Ordinal)).Order(StringComparer.Ordinal)), $"the package of {build}");

        foreach (var byHand in BY_HAND.Concat(alsoByHand).Where(file => file != BY_HAND_ASSET))
            Assert.That(state.Zipped, Does.Not.Contain(byHand), $"{byHand} lies in the module folder and is in the zip of {build}");

        foreach (var byHand in BY_HAND.Concat(alsoByHand))
            Assert.That(state.Packed, Does.Not.Contain(byHand), $"{byHand} lies in the module folder and is in the package of {build}");

        Assert.That(state.StatedDigest, Is.EqualTo(state.PackedDigest), $"the content digest the manifest of {build} states is not that of the files its package ships");
    }

    private static string[] InModule(IEnumerable<string> files)
    {
        return files.Select(file => ControllerStagingProbe.MODULE + "/" + file).Order(StringComparer.Ordinal).ToArray();
    }

    private static string Content(string inside)
    {
        return $"put into the module by hand: {inside}\n";
    }

    #endregion
}
