using System.Globalization;

namespace OutWit.Controller.AI.Smt.Tests.Differential;

/// <summary>
/// The terms the differential tests run on: for each theory a number of terms from a generator
/// of the theory's own, seeded from one fixed seed, so every fixture and every machine sees the
/// same terms, and the terms of a theory do not depend on how many the others have.
/// </summary>
internal static class SmtDifferentialTerms
{
    #region Constants

    /// <summary>The variable that sets the number of terms of each theory.</summary>
    public const string COUNT_VARIABLE = "SMT_DIFFERENTIAL_TERMS";

    /// <summary>The number of terms of each theory when the variable is not set.</summary>
    public const int LOCAL_TERMS = 40;

    /// <summary>The seed of the generators.</summary>
    public const int SEED = 20261123;

    /// <summary>What sets the seeds of two theories apart.</summary>
    private const int THEORY_STRIDE = 7_919;

    #endregion

    #region Functions

    /// <summary>
    /// The number of terms of each theory: the variable's, or <see cref="LOCAL_TERMS"/>.
    /// </summary>
    /// <returns>The number.</returns>
    public static int Count()
    {
        return Environment.GetEnvironmentVariable(COUNT_VARIABLE) is { Length: > 0 } text ? int.Parse(text, CultureInfo.InvariantCulture) : LOCAL_TERMS;
    }

    /// <summary>
    /// Generates the terms of every theory.
    /// </summary>
    /// <param name="count">The number of terms of each theory.</param>
    /// <returns>The terms, theory by theory.</returns>
    public static List<SmtGeneratedTerm> Generate(int count)
    {
        var terms = new List<SmtGeneratedTerm>();
        foreach (var theory in Enum.GetValues<SmtDifferentialTheory>())
        {
            var generator = new SmtTermGenerator(SEED + (int)theory * THEORY_STRIDE);
            for (var number = 0; number < count; number++)
                terms.Add(generator.Next(theory, number));
        }

        return terms;
    }

    #endregion
}
