using System.Diagnostics;
using System.Globalization;
using OutWit.Controller.AI.Corpus.Language;

namespace OutWit.Controller.AI.Corpus.Assets;

/// <summary>
/// <c>build-language-pack</c> and <c>evaluate-language-pack</c>: a language pack from a folder of
/// per-language source data and Common Crawl WET files (training, calibration), and its
/// measurement against the crawl's labels on other files.
/// </summary>
internal static class LanguagePackCommands
{
    #region Functions

    /// <summary>
    /// build-language-pack &lt;out.bin&gt; --languages &lt;dir&gt; --train &lt;wet&gt;... [--known-train &lt;wet&gt;...]
    /// --calibrate &lt;wet&gt;... [--recall R] [--name NAME] [--profile N] [--min-docs N] [--known-min-docs N] [--threads N]
    /// </summary>
    /// <param name="line">The arguments.</param>
    /// <returns>The exit code.</returns>
    public static int Build(CommandLine line)
    {
        if (line.Positional.Count != 1 || !line.Has("--languages") || line.Values("--train").Count == 0)
            return Program.Usage();

        var outPath = line.Positional[0];
        var recall = line.Decimal("--recall", LanguagePackCalibrator.DEFAULT_RECALL_PPM / (decimal)LanguageIdentifier.PPM);
        if (recall is < 0 or > 1)
            throw new ArgumentException("--recall is between 0 (no distance limits) and 1.");
        if (recall > 0 && line.Values("--calibrate").Count == 0)
            throw new ArgumentException("--calibrate names the files the limits are calibrated on (or --recall 0 builds without limits).");

        var supported = LanguagePackSource.Read(line.Value("--languages", ""));
        var builder = new LanguagePackBuilder(supported, line.Int("--profile", LanguageModelTrainer.DEFAULT_PROFILE_SIZE),
            line.Int("--min-docs", LanguageModelTrainer.DEFAULT_MIN_DOCUMENTS), line.Int("--known-min-docs", LanguagePackBuilder.DEFAULT_KNOWN_MIN_DOCUMENTS));
        Console.WriteLine($"supported: {string.Join(" ", supported.Languages.Select(language => $"{language.Code}/{language.Script}"))}");

        var stopwatch = Stopwatch.StartNew();
        foreach (var file in line.Values("--train"))
            Console.WriteLine($"  train {Path.GetFileName(file)}: {builder.AddTrainingWet(file)} single-language documents ({stopwatch.Elapsed.TotalSeconds:F0} s)");
        foreach (var file in line.Values("--known-train"))
            Console.WriteLine($"  known {Path.GetFileName(file)}: {builder.AddKnownTrainingWet(file)} documents of other languages ({stopwatch.Elapsed.TotalSeconds:F0} s)");

        var pack = builder.Build(line.Value("--name", Path.GetFileNameWithoutExtension(outPath)));
        var known = pack.Profiles.Select(profile => profile.Code).Where(code => !supported.TryGet(code, out _)).ToList();
        Console.WriteLine($"profiles: {pack.Languages.Count} supported ({string.Join(" ", pack.Languages.Select(code => $"{code}={builder.Documents.GetValueOrDefault(code)}"))})");
        Console.WriteLine($"          {known.Count} known, not supported ({string.Join(" ", known.Select(code => $"{code}={builder.KnownDocuments.GetValueOrDefault(code)}"))})");

        if (recall > 0)
        {
            var calibrators = Parallel(line.Values("--calibrate"), line.Int("--threads", Environment.ProcessorCount), file =>
            {
                var calibrator = new LanguagePackCalibrator(pack);
                var samples = calibrator.AddWet(file);
                Console.WriteLine($"  calibrate {Path.GetFileName(file)}: {samples} samples");
                return calibrator;
            });

            var calibration = calibrators[0];
            foreach (var other in calibrators.Skip(1))
                calibration.Add(other);

            var recallPpm = (int)Math.Round(recall * LanguageIdentifier.PPM);
            pack = calibration.Apply(recallPpm);
            Console.WriteLine($"limits at recall {recall.ToString(CultureInfo.InvariantCulture)} (ppm of the worst distance; calibration documents):");
            Console.WriteLine($"  {string.Join(" ", pack.Languages.Select(code => $"{code}={pack.Acceptance!.MaxDistancePpm(code)} ({calibration.Documents[code]})"))}");
        }
        else
            Console.WriteLine("no distance limits (--recall 0): only the known languages' profiles turn a document unknown");

        var bytes = pack.ToBytes();
        File.WriteAllBytes(outPath, bytes);
        Console.WriteLine($"pack {pack.Name}: {Path.GetFileName(outPath)}, {bytes.Length:N0} bytes, {pack.Format}, sha256 {pack.Sha256()}");
        return 0;
    }

    /// <summary>evaluate-language-pack &lt;pack.bin&gt; &lt;wet&gt;... [--no-judge] [--threads N]</summary>
    /// <param name="line">The arguments.</param>
    /// <returns>The exit code.</returns>
    public static int Evaluate(CommandLine line)
    {
        if (line.Positional.Count < 2)
            return Program.Usage();

        var pack = LanguageModel.Load(line.Positional[0]);
        var judge = !line.Has("--no-judge");
        var known = pack.Profiles.Select(profile => profile.Code).Where(code => !pack.Languages.Contains(code)).ToList();
        Console.WriteLine($"pack {(pack.Name.Length > 0 ? pack.Name : Path.GetFileName(line.Positional[0]))}: {pack.Format}, sha256 {pack.Sha256()}, {pack.Languages.Count} supported, {known.Count} known not supported");
        if (pack.Acceptance != null)
            Console.WriteLine($"  limits: {string.Join(" ", pack.Languages.Select(code => $"{code}={pack.Acceptance.MaxDistancePpm(code)}"))}");

        var evaluators = Parallel(line.Positional.Skip(1).ToList(), line.Int("--threads", Environment.ProcessorCount), file =>
        {
            var evaluator = new LanguagePackEvaluator(pack, judge);
            var documents = evaluator.AddWet(file);
            Console.WriteLine($"  {Path.GetFileName(file)}: {documents} labelled documents");
            return evaluator;
        });

        var evaluation = evaluators[0];
        foreach (var other in evaluators.Skip(1))
            evaluation.Add(other);

        PrintSupported(evaluation, pack, judge);
        PrintOthers(evaluation, pack, judge);
        PrintConfusion(evaluation, pack);
        PrintMixed(evaluation, judge);
        if (judge)
            Console.WriteLine("kept: the line stage and the quality filter keep the document under its nearest supported language, as a model without the unsupported test labels it");
        return 0;
    }

    private static void PrintSupported(LanguagePackEvaluator evaluation, LanguageModel pack, bool judge)
    {
        Console.WriteLine();
        Console.WriteLine("The crawl's label is a supported language: recall (labelled with it) without the unsupported test -> with it");
        Console.WriteLine($"  {"label",-6} {"docs",8} {"without",8} {"with",8} {"unsupp.",8} {"short",6} {"other",6}{(judge ? $" {"kept",8} {"kept lost",10}" : "")}");
        var all = new LanguagePackTally();
        var correctWithout = 0;
        var correctWith = 0;
        foreach (var code in pack.Languages)
        {
            if (!evaluation.Labels.TryGetValue(code, out var tally))
                continue;

            all.Add(tally);
            correctWithout += tally.NearestIs(code);
            correctWith += tally.LabelledAs(code);
            var other = tally.Labelled.Where(pair => pair.Key != code).Sum(pair => pair.Value);
            Console.WriteLine($"  {code,-6} {tally.Documents,8} {Share(tally.NearestIs(code), tally.Documents),8} {Share(tally.LabelledAs(code), tally.Documents),8} {tally.Unsupported,8} {tally.TooShort,6} {other,6}{(judge ? $" {tally.Kept,8} {tally.KeptUnsupported,10}" : "")}");
        }

        Console.WriteLine($"  {"all",-6} {all.Documents,8} {Share(correctWithout, all.Documents),8} {Share(correctWith, all.Documents),8} {all.Unsupported,8} {all.TooShort,6} {"",6}{(judge ? $" {all.Kept,8} {all.KeptUnsupported,10}" : "")}");
        if (judge)
            Console.WriteLine($"  kept documents lost to the test: {Share(all.KeptUnsupported, all.Kept)} ({all.KeptUnsupported} of {all.Kept}); characters {Share(all.KeptCharsUnsupported, all.KeptChars)}");
    }

    private static void PrintOthers(LanguagePackEvaluator evaluation, LanguageModel pack, bool judge)
    {
        Console.WriteLine();
        Console.WriteLine("The crawl's label is another language: share unknown (unsupported or too short) and where the rest went; (k) = a known profile of the pack");
        Console.WriteLine($"  {"label",-6} {"docs",8} {"unknown",8} {"unsupp.",8}{(judge ? $" {"kept",6} {"rejected",9}" : "")}  labelled as");
        var all = new LanguagePackTally();
        foreach (var (label, tally) in evaluation.Labels.Where(pair => !pair.Key.StartsWith('(') && !pack.Languages.Contains(pair.Key)).OrderByDescending(pair => pair.Value.Documents).ThenBy(pair => pair.Key, StringComparer.Ordinal))
        {
            all.Add(tally);
            var knownMark = pack.Profiles.Any(profile => profile.Code == label) ? "(k)" : "";
            var labelled = string.Join(" ", tally.Labelled.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).Take(3).Select(pair => $"{pair.Key}={pair.Value}"));
            Console.WriteLine($"  {label + knownMark,-6} {tally.Documents,8} {Share(tally.Unsupported + tally.TooShort, tally.Documents),8} {tally.Unsupported,8}{(judge ? $" {tally.Kept,6} {Share(tally.KeptUnsupported, tally.Kept),9}" : "")}  {labelled}");
        }

        Console.WriteLine($"  {"all",-6} {all.Documents,8} {Share(all.Unsupported + all.TooShort, all.Documents),8} {all.Unsupported,8}{(judge ? $" {all.Kept,6} {Share(all.KeptUnsupported, all.Kept),9}" : "")}");
        if (judge)
            Console.WriteLine($"  kept documents rejected: {Share(all.KeptUnsupported, all.Kept)} ({all.KeptUnsupported} of {all.Kept}); characters {Share(all.KeptCharsUnsupported, all.KeptChars)}");
    }

    private static void PrintConfusion(LanguagePackEvaluator evaluation, LanguageModel pack)
    {
        var confusions = pack.Languages
            .Where(code => evaluation.Labels.ContainsKey(code))
            .SelectMany(code => evaluation.Labels[code].Labelled.Where(pair => pair.Key != code).Select(pair => (Pair: $"{code}->{pair.Key}", Count: pair.Value)))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Pair, StringComparer.Ordinal)
            .Take(20)
            .ToList();

        Console.WriteLine();
        Console.WriteLine($"Confusion between supported languages (label->labelled, documents): {(confusions.Count == 0 ? "none" : string.Join(" ", confusions.Select(item => $"{item.Pair}={item.Count}")))}");
    }

    private static void PrintMixed(LanguagePackEvaluator evaluation, bool judge)
    {
        Console.WriteLine();
        Console.WriteLine("Documents with several labels (the main one first):");
        foreach (var key in new[] { LanguagePackEvaluator.MIXED_SUPPORTED, LanguagePackEvaluator.MIXED_OTHER })
        {
            if (!evaluation.Labels.TryGetValue(key, out var tally))
                continue;

            var labelled = tally.Labelled.Values.Sum();
            Console.WriteLine($"  {key}: {tally.Documents} documents, labelled {Share(labelled, tally.Documents)}, unsupported {Share(tally.Unsupported, tally.Documents)}, too short {Share(tally.TooShort, tally.Documents)}{(judge ? $"; kept {tally.Kept}, of them unsupported {tally.KeptUnsupported}" : "")}");
        }
    }

    private static List<T> Parallel<T>(IReadOnlyList<string> files, int threads, Func<string, T> work)
    {
        var results = new T[files.Count];
        System.Threading.Tasks.Parallel.For(0, files.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, threads) }, i => results[i] = work(files[i]));
        return [.. results];
    }

    private static string Share(long part, long whole)
    {
        return whole == 0 ? "-" : (100.0 * part / whole).ToString("0.00", CultureInfo.InvariantCulture) + "%";
    }

    #endregion
}
