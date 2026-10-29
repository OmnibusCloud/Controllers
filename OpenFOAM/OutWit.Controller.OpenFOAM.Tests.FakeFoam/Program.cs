// Fake OpenFOAM solver for the controller's tests. Honors the contract a
// real solver is invoked with: cwd = the case directory, the log on stdout
// in OpenFOAM's shape ("Time = N", "Solving for ...", "End"), the last time
// written as a directory. Behaviour is driven by system/fake, a text file
// the test writes into the case:
//   - a file containing "FAKE-FAIL" fails like a diverged solve: a FOAM
//     FATAL ERROR line, nonzero exit, no time directory;
//   - a file containing "FAKE-HANG" sleeps like a wedged solve - the
//     cancellation gates kill it through the process tree;
//   - a file containing "FAKE-ECHO" prints every environment variable it
//     was given (the environment tests read them back);
//   - a file containing "FAKE-COEFFS" also writes a force-coefficient
//     history under postProcessing/coeffs/<n>/coefficient.dat, in the shape
//     the forceCoeffs function object writes;
//   - a file containing "FAKE-STALE-COEFFS" writes the same history under
//     postProcessing/coeffs/0/ one time short of the run (rows at n-2 and
//     n-1): a function object that stopped writing before the run ended;
//   - a line "FAKE-PROBES=<name>" also writes the probes files p and U under
//     postProcessing/<name>/0/ in v2606's shape, a row per time, probe 1 at
//     (9 9 9) outside the mesh ("# Not Found", -1e+300);
//   - otherwise it "solves" ITERATIONS steps (system/fake may say
//     "ITERATIONS=n"), converging on the last one, and writes <n>/U.
// Every copy prints "nCells: 12225" unless system/fake says
// "CELLS.<utility>=n" for the copy's name: n cells, or no count for 0.

var caseDirectory = Directory.GetCurrentDirectory();
var control = Path.Combine(caseDirectory, "system", "fake");
var text = File.Exists(control) ? File.ReadAllText(control) : string.Empty;
var name = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "fake-foam");

Console.WriteLine("/*---------------------------------------------------------------------------*\\");
Console.WriteLine("| fake-foam: a stand-in for an OpenFOAM solver                                |");
Console.WriteLine("\\*---------------------------------------------------------------------------*/");
Console.WriteLine($"Case   : {caseDirectory}");
Console.WriteLine($"Args   : {string.Join(' ', args)}");

var cells = 12225L;
foreach (var line in text.Split('\n'))
{
    if (line.StartsWith($"CELLS.{name}=", StringComparison.Ordinal) && long.TryParse(line[$"CELLS.{name}=".Length..].Trim(), out var value))
        cells = value;
}

if (cells > 0)
    Console.WriteLine($"nCells: {cells}");

if (text.Contains("FAKE-ECHO", StringComparison.Ordinal))
{
    foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        Console.WriteLine($"ENV {entry.Key}={entry.Value}");
}

if (text.Contains("FAKE-FAIL", StringComparison.Ordinal))
{
    Console.WriteLine("Time = 1");
    Console.WriteLine("smoothSolver:  Solving for Ux, Initial residual = 1, Final residual = 0.05, No Iterations 3");
    Console.WriteLine("--> FOAM FATAL ERROR: (fake) the solve diverged as requested by system/fake");
    Console.Error.WriteLine("FOAM exiting");
    return 1;
}

if (text.Contains("FAKE-HANG", StringComparison.Ordinal))
{
    Console.WriteLine("Time = 1");
    Console.WriteLine(" hanging as requested by system/fake");
    Thread.Sleep(TimeSpan.FromMinutes(5));
    return 0;
}

var iterations = 5;
foreach (var line in text.Split('\n'))
{
    if (line.StartsWith("ITERATIONS=", StringComparison.Ordinal) && int.TryParse(line["ITERATIONS=".Length..].Trim(), out var value))
        iterations = value;
}

for (var step = 1; step <= iterations; step++)
{
    Console.WriteLine($"Time = {step}");
    Console.WriteLine($"smoothSolver:  Solving for Ux, Initial residual = {1.0 / (step + 1):E3}, Final residual = 1e-06, No Iterations 3");
    Console.WriteLine($"GAMG:  Solving for p, Initial residual = {0.5 / (step + 1):E3}, Final residual = 1e-06, No Iterations 8");
    if (step == 2)
        Console.WriteLine("--> FOAM Warning : (fake) a warning, as real solvers print them");
}

Console.WriteLine();
Console.WriteLine($"SIMPLE solution converged in {iterations} iterations");
Console.WriteLine();

var timeDirectory = Path.Combine(caseDirectory, iterations.ToString());
Directory.CreateDirectory(timeDirectory);
File.WriteAllText(Path.Combine(timeDirectory, "U"), "FoamFile { class volVectorField; object U; }\ninternalField uniform (1 0 0);\n");

if (text.Contains("FAKE-COEFFS", StringComparison.Ordinal))
{
    var coeffs = Path.Combine(caseDirectory, "postProcessing", "coeffs", iterations.ToString());
    Directory.CreateDirectory(coeffs);
    File.WriteAllText(Path.Combine(coeffs, "coefficient.dat"),
        "# Force coefficients\n# Time        \tCd            \tCl            \tCmPitch\n" +
        $"{iterations - 1}\t0.44\t0.10\t0.01\n{iterations}\t0.418\t0.092\t0.009\n");
}

if (text.Contains("FAKE-STALE-COEFFS", StringComparison.Ordinal))
{
    var coeffs = Path.Combine(caseDirectory, "postProcessing", "coeffs", "0");
    Directory.CreateDirectory(coeffs);
    File.WriteAllText(Path.Combine(coeffs, "coefficient.dat"),
        "# Force coefficients\n# Time        \tCd            \tCl            \tCmPitch\n" +
        $"{iterations - 2}\t0.44\t0.10\t0.01\n{iterations - 1}\t0.418\t0.092\t0.009\n");
}

foreach (var line in text.Split('\n'))
{
    if (!line.StartsWith("FAKE-PROBES=", StringComparison.Ordinal))
        continue;

    var probes = Path.Combine(caseDirectory, "postProcessing", line["FAKE-PROBES=".Length..].Trim(), "0");
    Directory.CreateDirectory(probes);
    const string PROBES_HEADER = "# Probe 0 (0.1 0 0)\n# Probe 1 (9 9 9)  # Not Found\n# Time 0 1\n";
    var scalar = new System.Text.StringBuilder(PROBES_HEADER);
    var vector = new System.Text.StringBuilder(PROBES_HEADER);
    for (var step = 1; step <= iterations; step++)
    {
        scalar.Append($"{step} -3.68615 -1e+300\n");
        vector.Append($"{step} (6.4 -0.5 0) (-1e+300 -1e+300 -1e+300)\n");
    }

    File.WriteAllText(Path.Combine(probes, "p"), scalar.ToString());
    File.WriteAllText(Path.Combine(probes, "U"), vector.ToString());
}

Console.WriteLine("End");
return 0;
