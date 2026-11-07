using System.Text.RegularExpressions;

namespace OutWit.Controller.CalculiX.Runtime;

/// <summary>
/// The equation solver a solve ends up in, as far as its thread count goes. ccx takes PARDISO
/// by default where it is linked (the Windows and Linux kits carry oneMKL) and SPOOLES where it
/// is not (the macOS kit: oneMKL has no arm64 build), and SPOOLES wherever a step card says
/// <c>SOLVER=SPOOLES</c>. The kits link SPOOLES multithreaded, and multithreaded SPOOLES returns
/// a wrong field now and then: a 14 k-node bracket solved 60 times in concurrent pairs came
/// back different in 2 runs (by up to 0.6 %), and a live sweep variant on an arm64 Mac was 10 %
/// off - with no error and exit 0. PARDISO and single-threaded SPOOLES gave the same answer every
/// time, so a SPOOLES solve gets one equation-solver thread (about 15 % slower on that bracket);
/// assembly and stress recovery keep their OpenMP threads, and a PARDISO solve keeps all of them.
/// </summary>
public static class CcxEquationSolver
{
    #region Constants

    /// <summary>The equation-solver thread count of a solve that uses SPOOLES.</summary>
    public const int SPOOLES_THREADS = 1;

    /// <summary>The environment variable ccx reads the equation solver's thread count from.</summary>
    public const string THREADS_VARIABLE = "CCX_NPROC_EQUATION_SOLVER";

    private static readonly Regex EXPLICIT_SPOOLES = new(@"SOLVER\s*=\s*SPOOLES", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    #endregion

    #region Functions

    /// <summary>
    /// Whether the bundled ccx of a platform links PARDISO.
    /// </summary>
    /// <param name="isMacOS">True on macOS.</param>
    /// <returns>False on macOS, true elsewhere.</returns>
    public static bool PardisoLinked(bool isMacOS)
    {
        return !isMacOS;
    }

    /// <summary>
    /// True when a deck's solve may go through SPOOLES: always where PARDISO is not linked, and
    /// wherever a line of the deck names <c>SOLVER=SPOOLES</c> (a comment that does only costs
    /// the solve its equation-solver threads).
    /// </summary>
    /// <param name="deckLines">The deck, line by line; read only when PARDISO is linked.</param>
    /// <param name="pardisoLinked">Whether this platform's ccx links PARDISO.</param>
    /// <returns>True for a solve that may use SPOOLES.</returns>
    public static bool UsesSpooles(IEnumerable<string> deckLines, bool pardisoLinked)
    {
        return !pardisoLinked || deckLines.Any(line => EXPLICIT_SPOOLES.IsMatch(line));
    }

    /// <summary>
    /// The equation-solver thread count a deck's solve on this machine must get, or null to
    /// leave it to OpenMP (<see cref="SPOOLES_THREADS"/> for a solve that may use SPOOLES).
    /// </summary>
    /// <param name="deckPath">The deck ccx will read.</param>
    /// <returns>The thread count, or null.</returns>
    public static int? ThreadsFor(string deckPath)
    {
        if (!PardisoLinked(OperatingSystem.IsMacOS()))
            return SPOOLES_THREADS;

        return UsesSpooles(File.ReadLines(deckPath), pardisoLinked: true) ? SPOOLES_THREADS : null;
    }

    #endregion
}
