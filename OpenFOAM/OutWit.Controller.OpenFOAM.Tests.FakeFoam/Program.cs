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
//   - otherwise it "solves" ITERATIONS steps (system/fake may say
//     "ITERATIONS=n"), converging on the last one, and writes <n>/U.

var caseDirectory = Directory.GetCurrentDirectory();
var control = Path.Combine(caseDirectory, "system", "fake");
var text = File.Exists(control) ? File.ReadAllText(control) : string.Empty;

Console.WriteLine("/*---------------------------------------------------------------------------*\\");
Console.WriteLine("| fake-foam: a stand-in for an OpenFOAM solver                                |");
Console.WriteLine("\\*---------------------------------------------------------------------------*/");
Console.WriteLine($"Case   : {caseDirectory}");
Console.WriteLine($"Args   : {string.Join(' ', args)}");
Console.WriteLine("nCells: 12225");

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

Console.WriteLine("End");
return 0;
