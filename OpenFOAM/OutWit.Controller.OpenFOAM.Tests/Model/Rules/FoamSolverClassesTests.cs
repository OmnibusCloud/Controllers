using OutWit.Controller.OpenFOAM.Model.Rules;

namespace OutWit.Controller.OpenFOAM.Tests.Model.Rules;

[TestFixture]
public class FoamSolverClassesTests
{
    #region Vocabulary Tests

    [Test]
    public void TheSolversOfTheSupportedInputsHaveTheirClassesTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FoamSolverClasses.ClassOf("simpleFoam"), Is.EqualTo(FoamSolverClasses.INCOMPRESSIBLE_STEADY));
            Assert.That(FoamSolverClasses.ClassOf("pimpleFoam"), Is.EqualTo(FoamSolverClasses.INCOMPRESSIBLE_TRANSIENT));
            Assert.That(FoamSolverClasses.ClassOf("icoFoam"), Is.EqualTo(FoamSolverClasses.INCOMPRESSIBLE_TRANSIENT));
            Assert.That(FoamSolverClasses.ClassOf("rhoSimpleFoam"), Is.EqualTo(FoamSolverClasses.COMPRESSIBLE_STEADY));
            Assert.That(FoamSolverClasses.ClassOf("rhoPimpleFoam"), Is.EqualTo(FoamSolverClasses.COMPRESSIBLE_TRANSIENT));
            Assert.That(FoamSolverClasses.ClassOf("buoyantSimpleFoam"), Is.EqualTo(FoamSolverClasses.THERMAL_STEADY));
            Assert.That(FoamSolverClasses.ClassOf("buoyantPimpleFoam"), Is.EqualTo(FoamSolverClasses.THERMAL_TRANSIENT));
            Assert.That(FoamSolverClasses.ClassOf("interFoam"), Is.EqualTo(FoamSolverClasses.MULTIPHASE_TRANSIENT));
        });
    }

    [Test]
    public void AnUnclassifiedSolverHasNoClassTest()
    {
        Assert.That(FoamSolverClasses.ClassOf("reactingFoam"), Is.Empty);
        Assert.That(FoamSolverClasses.ClassOf(string.Empty), Is.Empty);
        Assert.That(FoamSolverClasses.ClassOf("SIMPLEFOAM"), Is.Empty, "application names are case-sensitive, as on the node");
    }

    [Test]
    public void EveryMappedApplicationNamesAClassOfTheVocabularyTest()
    {
        foreach (var (application, solverClass) in FoamSolverClasses.APPLICATIONS)
        {
            Assert.That(FoamAllowList.IsSolverName(application), Is.True, application);
            Assert.That(FoamSolverClasses.ALL, Does.Contain(solverClass), application);
        }
    }

    [Test]
    public void TheVocabularyIsKnownAndNothingElseIsTest()
    {
        Assert.That(FoamSolverClasses.ALL, Has.Count.EqualTo(7));
        Assert.That(FoamSolverClasses.ALL.All(FoamSolverClasses.IsKnown), Is.True);
        Assert.That(FoamSolverClasses.IsKnown("Incompressible-Steady"), Is.True, "a class is matched ignoring case, as the work estimate always did");
        Assert.That(FoamSolverClasses.IsKnown("exotic"), Is.False);
    }

    #endregion
}
