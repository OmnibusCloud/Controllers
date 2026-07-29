// Fake ccx for the SDK end-to-end tests. Honors the host contract the real
// solver is invoked with: bare jobname argument, cwd = job directory, deck at
// <jobname>.inp, artifacts written next to it, " Job finished" on success,
// exit code forwarded. Two behaviours, driven by the deck text:
//   - a deck containing "FAKE-FAIL" fails like a diverged solve: error line
//     on stderr, nonzero exit, no artifacts;
//   - any other deck "solves": the .frd echoes the deck text verbatim (which
//     lets tests assert placeholder substitution end-to-end through blobs),
//     plus a minimal .dat.

if (args.Length != 1)
{
    Console.Error.WriteLine("*ERROR: expected exactly one argument (the job name).");
    return 2;
}

var jobName = args[0];
var deckPath = $"{jobName}.inp";

if (!File.Exists(deckPath))
{
    Console.Error.WriteLine($"*ERROR: deck '{deckPath}' not found.");
    return 2;
}

var deckText = File.ReadAllText(deckPath);

if (deckText.Contains("FAKE-FAIL", StringComparison.Ordinal))
{
    Console.Error.WriteLine("*ERROR: fake failure requested by the deck.");
    return 201;
}

File.WriteAllText($"{jobName}.frd", deckText);
// Deliberately EMPTY, like real ccx on a deck without *NODE PRINT requests:
// the adapter must treat a zero-byte artifact as no artifact (uploading it
// failed the first live sweep).
File.WriteAllText($"{jobName}.dat", string.Empty);
Console.WriteLine(" Job finished");
return 0;
