using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Runtime;

namespace OutWit.Controller.OpenFOAM.Tests.Runtime;

[TestFixture]
public class FoamWorkEstimateTests
{
    #region Estimate Tests

    [Test]
    public void TheReferenceSteadyCaseCostsOneUnitTest()
    {
        var data = new FoamCaseData { CellCount = (long)FoamWorkEstimate.REFERENCE_CELLS, SolverClass = "incompressible-steady" };

        Assert.That(FoamWorkEstimate.Estimate(data), Is.EqualTo(1.0).Within(1e-12));
    }

    [Test]
    public void CellsScaleLinearlyAndTheSolverClassMultipliesTest()
    {
        var steady = new FoamCaseData { CellCount = 350_000, SolverClass = "incompressible-steady" };
        var transient = new FoamCaseData { CellCount = 350_000, SolverClass = "INCOMPRESSIBLE-TRANSIENT" };

        Assert.That(FoamWorkEstimate.Estimate(steady), Is.EqualTo(3.5).Within(1e-12));
        Assert.That(FoamWorkEstimate.Estimate(transient), Is.EqualTo(3.5 * 6.0).Within(1e-12), "the class is matched regardless of case");
    }

    [Test]
    public void MeshingPerVariantAddsItsSurchargeTest()
    {
        var data = new FoamCaseData
        {
            CellCount = 100_000,
            SolverClass = "compressible-steady",
            Recipe = new FoamRecipeData { Application = "rhoSimpleFoam", MeshesPerVariant = true }
        };

        Assert.That(FoamWorkEstimate.Estimate(data), Is.EqualTo(2.0 * FoamWorkEstimate.MESHING_FACTOR).Within(1e-12));
    }

    [Test]
    public void AnUnknownClassCountsAsSteadyAndNoCellCountIsOneUnitTest()
    {
        Assert.That(FoamWorkEstimate.Estimate(new FoamCaseData { CellCount = 200_000, SolverClass = "exotic" }), Is.EqualTo(2.0).Within(1e-12));
        Assert.That(FoamWorkEstimate.Estimate(new FoamCaseData { CellCount = 200_000, SolverClass = "" }), Is.EqualTo(2.0).Within(1e-12));
        Assert.That(FoamWorkEstimate.Estimate(new FoamCaseData { CellCount = 0, SolverClass = "incompressible-steady" }), Is.EqualTo(FoamWorkEstimate.UNKNOWN));
        Assert.That(FoamWorkEstimate.Estimate(null), Is.EqualTo(FoamWorkEstimate.UNKNOWN), "a task without a case");
    }

    #endregion
}
