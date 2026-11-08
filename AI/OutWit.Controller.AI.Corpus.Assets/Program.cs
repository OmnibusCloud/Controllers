using System.Diagnostics;
using System.Text;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Assets;

/// <summary>
/// corpus-assets train-language-model &lt;out.bin&gt; &lt;wet.gz&gt;... [--profile N] [--min-docs N]
/// corpus-assets evaluate-language-model &lt;model.bin&gt; &lt;wet.gz&gt;
///
/// Trains the n-gram language model from the single-language conversion records of the given
/// WET files (labelled by Common Crawl's classifier) and prints its SHA-256; evaluates a model
/// against a WET file's labels and prints the agreement.
/// </summary>
internal static class Program
{
    #region Entry

    private static int Main(string[] args)
    {
        if (args.Length < 3)
            return Usage();

        try
        {
            return args[0] switch
            {
                "train-language-model" => Train(args[1], args.Skip(2).Where(a => !a.StartsWith("--", StringComparison.Ordinal) && !int.TryParse(a, out _)).ToList(),
                    OptionValue(args, "--profile", LanguageModelTrainer.DEFAULT_PROFILE_SIZE), OptionValue(args, "--min-docs", LanguageModelTrainer.DEFAULT_MIN_DOCUMENTS)),
                "evaluate-language-model" => Evaluate(args[1], args[2]),
                _ => Usage()
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"corpus-assets: {exception.Message}");
            return 1;
        }
    }

    #endregion

    #region Commands

    private static int Train(string outPath, IReadOnlyList<string> wetFiles, int profileSize, int minDocuments)
    {
        var trainer = new LanguageModelTrainer(profileSize, minDocuments);
        var stopwatch = Stopwatch.StartNew();
        foreach (var file in wetFiles)
        {
            var added = trainer.AddWet(file);
            Console.WriteLine($"  {Path.GetFileName(file)}: {added} single-language documents ({stopwatch.Elapsed.TotalSeconds:F0} s)");
        }

        var model = trainer.Build();
        var bytes = model.ToBytes();
        File.WriteAllBytes(outPath, bytes);

        var kept = model.Profiles.Select(p => p.Code).ToList();
        var dropped = trainer.Documents.Where(pair => pair.Value < minDocuments).OrderByDescending(pair => pair.Value).Select(pair => $"{pair.Key}={pair.Value}").ToList();
        Console.WriteLine($"model: {kept.Count} languages, {profileSize} n-grams each, {bytes.Length:N0} bytes, sha256 {model.Sha256()}");
        Console.WriteLine($"  languages: {string.Join(" ", kept)}");
        Console.WriteLine($"  below {minDocuments} documents ({dropped.Count}): {string.Join(" ", dropped.Take(40))}{(dropped.Count > 40 ? " …" : "")}");
        Console.WriteLine($"  documents per kept language: {string.Join(" ", kept.Select(code => $"{code}={trainer.Documents[code]}"))}");
        return 0;
    }

    private static int Evaluate(string modelPath, string wetFile)
    {
        var model = LanguageModel.Load(modelPath);
        var identifier = new LanguageIdentifier(model);
        var known = model.Profiles.Select(p => p.Code).ToHashSet(StringComparer.Ordinal);

        var total = 0;
        var agree = 0;
        var unknown = 0;
        var outsideModel = 0;
        var confusions = new Dictionary<string, int>(StringComparer.Ordinal);
        using var reader = WarcReader.Open(wetFile);
        foreach (var record in reader.ReadRecords())
        {
            var label = record.IdentifiedLanguage;
            if (record.Type != "conversion" || string.IsNullOrEmpty(label) || label.Contains(','))
                continue;

            if (!known.Contains(label))
            {
                outsideModel++;
                continue;
            }

            total++;
            var guess = identifier.Identify(Encoding.UTF8.GetString(record.Payload.Span));
            if (!guess.IsKnown)
            {
                unknown++;
                continue;
            }

            if (guess.Code == label)
                agree++;
            else
                confusions[$"{label}->{guess.Code}"] = confusions.GetValueOrDefault($"{label}->{guess.Code}") + 1;
        }

        Console.WriteLine($"{total} labelled documents in the model's languages ({outsideModel} outside): agree {agree} ({100.0 * agree / Math.Max(1, total):F1} %), unknown {unknown}, disagree {total - agree - unknown}");
        Console.WriteLine($"  top confusions: {string.Join(" ", confusions.OrderByDescending(pair => pair.Value).Take(15).Select(pair => $"{pair.Key}={pair.Value}"))}");
        return 0;
    }

    #endregion

    #region Tools

    private static int OptionValue(string[] args, string name, int fallback)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value) ? value : fallback;
    }

    private static int Usage()
    {
        Console.WriteLine("corpus-assets train-language-model <out.bin> <wet.gz>... [--profile N] [--min-docs N]");
        Console.WriteLine("corpus-assets evaluate-language-model <model.bin> <wet.gz>");
        return 2;
    }

    #endregion
}
