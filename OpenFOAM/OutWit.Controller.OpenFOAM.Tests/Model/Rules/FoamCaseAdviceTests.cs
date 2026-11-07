using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Model.Rules;

namespace OutWit.Controller.OpenFOAM.Tests.Model.Rules;

/// <summary>
/// What a task would do without doing what was meant, said rather than
/// refused: ranks that do nothing, a setFields step over a swept
/// internalField (K5).
/// </summary>
[TestFixture]
public class FoamCaseAdviceTests
{
    #region Constants

    private const string SET_FIELDS_DICT =
        "FoamFile { object setFieldsDict; }\n" +
        "defaultFieldValues ( volScalarFieldValue alpha.water 0 volVectorFieldValue U (0 0 0) );\n" +
        "regions ( boxToCell { box (0 0 -1) (0.1461 0.292 1); fieldValues ( volScalarFieldValue alpha.water 1 volScalarFieldValue T 300 ); } );\n";

    #endregion

    #region Tools

    private static FoamCaseData Case(int threads, bool parallel)
    {
        return new FoamCaseData
        {
            Threads = threads,
            Recipe = new FoamRecipeData
            {
                Application = "interFoam",
                Steps = [new FoamStepData { Utility = "blockMesh" }, new FoamStepData { Utility = "interFoam", Parallel = parallel }]
            }
        };
    }

    private static FoamRecipeData Recipe(bool setFields)
    {
        var recipe = new FoamRecipeData { Application = "interFoam", Steps = [new FoamStepData { Utility = "blockMesh" }] };
        if (setFields)
            recipe.Steps.Add(new FoamStepData { Utility = "setFields" });
        recipe.Steps.Add(new FoamStepData { Utility = "interFoam" });
        return recipe;
    }

    #endregion

    #region Rank Tests

    [Test]
    public void RanksWithoutAParallelStepAreSaidToDoNothingTest()
    {
        Assert.That(FoamCaseAdvice.Of(Case(4, parallel: false)), Is.EqualTo(new[]
        {
            "The task asks for 4 ranks, and no step of the recipe runs in parallel: every step runs serially on one rank, and the other 3 do nothing. Mark the solve (and the steps that may) parallel, or ask for one thread."
        }));
        Assert.That(FoamCaseAdvice.Of(Case(4, parallel: true)), Is.Empty);
        Assert.That(FoamCaseAdvice.Of(Case(1, parallel: false)), Is.Empty);
        Assert.That(FoamCaseAdvice.Of(null), Is.Empty);
        Assert.That(FoamCaseAdvice.Of(new FoamCaseData { Threads = 4 }), Is.Empty, "no recipe: the rules refuse that, the advice says nothing");
    }

    #endregion

    #region SetFields Tests

    [Test]
    public void ASetFieldsStepOverASweptInternalFieldIsSaidToOverwriteItTest()
    {
        var files = new List<(string, string?)>
        {
            ("system/setFieldsDict", SET_FIELDS_DICT),
            ("0/alpha.water", "FoamFile { object alpha.water; }\ninternalField uniform {{oc1}};\n"),
            ("0/T", "internalField uniform {{oc2}};\n"),
            ("0/U", "internalField uniform (10 0 0);\n"),
            ("0/p_rgh", "internalField uniform {{oc3}};\n"),
            ("0/include/notAField", "internalField uniform {{oc4}};\n")
        };

        var advice = FoamCaseAdvice.OfSetFields(Recipe(setFields: true), files);

        Assert.That(advice, Is.EqualTo(new[]
        {
            "0/alpha.water: internalField carries {{oc1}}, which the study sweeps, and the recipe's setFields step overwrites it everywhere (system/setFieldsDict defaultFieldValues names alpha.water): the swept value has no effect.",
            "0/T: internalField carries {{oc2}}, which the study sweeps, and the recipe's setFields step overwrites it inside the regions of system/setFieldsDict: the swept value holds outside them only."
        }), "U is not swept, p_rgh is not set, a file in a sub-folder is not a field");
    }

    [Test]
    public void ASweptValueReachedThroughAReferenceToAnIncludedEntryIsNoticedTest()
    {
        // motorBike's shape: the velocity written once in 0/include/initialConditions
        // and reached by U's internalField through $flowVelocity; the token
        // sits in the include, the field itself carries none.
        var dictionary =
            "defaultFieldValues ( volVectorFieldValue U (0 0 0) volScalarFieldValue p 0 );\n" +
            "regions ( boxToCell { box (0 0 -1) (1 1 1); fieldValues ( volScalarFieldValue T 300 volScalarFieldValue k 1 ); } );\n";
        var files = new List<(string, string?)>
        {
            ("system/setFieldsDict", dictionary),
            ("0/include/initialConditions", "flowVelocity         ({{oc1}} 0 0);\npressure             {{oc3}};\n"),
            ("0/U", "FoamFile { class volVectorField; object U; }\n#include \"include/initialConditions\"\ndimensions [0 1 -1 0 0 0 0];\ninternalField uniform $flowVelocity;\nboundaryField\n{\n    inlet { type fixedValue; value $internalField; }\n}\n"),
            ("0/p", "#include \"include/initialConditions\"\ninternalField uniform ${pressure};\n"),
            ("0/T", "T0 {{oc2}};\ninternalField uniform $T0;\n"),
            ("0/k", "internalField uniform $missing;\n")
        };

        var advice = FoamCaseAdvice.OfSetFields(Recipe(setFields: true), files);

        Assert.That(advice, Is.EqualTo(new[]
        {
            "0/U: internalField carries {{oc1}} through $flowVelocity of 0/include/initialConditions, which the study sweeps, and the recipe's setFields step overwrites it everywhere (system/setFieldsDict defaultFieldValues names U): the swept value has no effect.",
            "0/p: internalField carries {{oc3}} through ${pressure} of 0/include/initialConditions, which the study sweeps, and the recipe's setFields step overwrites it everywhere (system/setFieldsDict defaultFieldValues names p): the swept value has no effect.",
            "0/T: internalField carries {{oc2}} through $T0, which the study sweeps, and the recipe's setFields step overwrites it inside the regions of system/setFieldsDict: the swept value holds outside them only."
        }), "a reference no file answers (k) says nothing");
    }

    [Test]
    public void AReferenceThatNeverEndsSaysNothingTest()
    {
        var files = new List<(string, string?)>
        {
            ("system/setFieldsDict", SET_FIELDS_DICT),
            ("0/alpha.water", "a $b;\nb $a;\ninternalField uniform $a;\n")
        };

        Assert.That(FoamCaseAdvice.OfSetFields(Recipe(setFields: true), files), Is.Empty);
    }

    [Test]
    public void WithoutASetFieldsStepOrItsDictionaryNothingIsSaidTest()
    {
        var files = new List<(string, string?)> { ("system/setFieldsDict", SET_FIELDS_DICT), ("0/alpha.water", "internalField uniform {{oc1}};\n") };

        Assert.That(FoamCaseAdvice.OfSetFields(Recipe(setFields: false), files), Is.Empty);
        Assert.That(FoamCaseAdvice.OfSetFields(null, files), Is.Empty);
        Assert.That(FoamCaseAdvice.OfSetFields(Recipe(setFields: true), [("0/alpha.water", "internalField uniform {{oc1}};\n")]), Is.Empty, "no dictionary read");
        Assert.That(FoamCaseAdvice.OfSetFields(Recipe(setFields: true), [("system/setFieldsDict", SET_FIELDS_DICT), ("0/alpha.water", null)]), Is.Empty, "a file not read");
    }

    #endregion
}
