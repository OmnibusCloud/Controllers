using System.Text;

namespace OutWit.Controller.AI.Corpus.Language;

/// <summary>
/// The two model layouts, each written by <see cref="BinaryWriter"/> (strings as UTF-8 with a
/// 7-bit length prefix, integers and doubles little-endian):
/// <code>
/// WITLID1: "WITLID1", profiles:int, per profile in code order { code, grams:int, gram... }
///
/// WITLID2: "WITLID2", name,
///          metadata:int, per entry in key order { key, value },
///          profiles:int, per profile in code order {
///            code, grams:int, gram... (most frequent first),
///            supported:bool, when supported {
///              script, maxDistancePpm:int,
///              stopWords:int, word... (ordinal order), minStopWords:int,
///              overrides:int, per override in name order { name, values:int, value:double... } } }
/// </code>
/// A WITLID2 file is read strictly: every list in its canonical order, no byte after the end,
/// every value valid for this controller (an unknown threshold name is an error). So reading
/// and writing again gives the same bytes, and the hash a manifest pins is the pack's meaning.
/// </summary>
public static class LanguageModelFormat
{
    #region Constants

    /// <summary>The profiles-only format (Corpus 0.1.x).</summary>
    public const string WITLID1 = "WITLID1";

    /// <summary>The language-pack format (Corpus 0.2.0).</summary>
    public const string WITLID2 = "WITLID2";

    /// <summary>The formats this controller reads, oldest first.</summary>
    public static readonly IReadOnlyList<string> READ = [WITLID1, WITLID2];

    #endregion

    #region Functions

    /// <summary>Serializes a model in its own format.</summary>
    /// <param name="model">The model.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Write(LanguageModel model)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(model.Format);
            if (model.Format == WITLID1)
                WriteWitlid1(writer, model);
            else
                WriteWitlid2(writer, model);
        }

        return stream.ToArray();
    }

    /// <summary>Reads a model of either format.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The model.</returns>
    /// <exception cref="FormatException">The bytes are not a model this controller reads.</exception>
    public static LanguageModel Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream, Encoding.UTF8);

        try
        {
            var magic = reader.ReadString();
            return magic switch
            {
                WITLID1 => ReadWitlid1(reader),
                WITLID2 => ReadWitlid2(reader, stream),
                _ => throw new FormatException("not a language model")
            };
        }
        catch (EndOfStreamException)
        {
            throw new FormatException("the language model is truncated");
        }
        catch (ArgumentException exception)
        {
            throw new FormatException($"the language pack is invalid: {exception.Message}", exception);
        }
    }

    private static void WriteWitlid1(BinaryWriter writer, LanguageModel model)
    {
        writer.Write(model.Profiles.Count);
        foreach (var profile in model.Profiles)
        {
            writer.Write(profile.Code);
            writer.Write(profile.Grams.Count);
            foreach (var gram in profile.Grams)
                writer.Write(gram);
        }
    }

    private static LanguageModel ReadWitlid1(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var profiles = new List<LanguageProfile>(count);
        for (var i = 0; i < count; i++)
        {
            var code = reader.ReadString();
            var gramCount = reader.ReadInt32();
            var grams = new List<string>(gramCount);
            for (var j = 0; j < gramCount; j++)
                grams.Add(reader.ReadString());
            profiles.Add(new LanguageProfile(code, grams));
        }

        return new LanguageModel(profiles);
    }

    private static void WriteWitlid2(BinaryWriter writer, LanguageModel model)
    {
        writer.Write(model.Name);

        var metadata = model.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList();
        writer.Write(metadata.Count);
        foreach (var (key, value) in metadata)
        {
            writer.Write(key);
            writer.Write(value);
        }

        writer.Write(model.Profiles.Count);
        foreach (var profile in model.Profiles)
        {
            writer.Write(profile.Code);
            writer.Write(profile.Grams.Count);
            foreach (var gram in profile.Grams)
                writer.Write(gram);

            var supported = model.Rules.TryGet(profile.Code, out var rules);
            writer.Write(supported);
            if (!supported)
                continue;

            writer.Write(rules.Script);
            writer.Write(model.Acceptance!.MaxDistancePpm(profile.Code));
            writer.Write(rules.StopWords.Count);
            foreach (var word in rules.StopWords)
                writer.Write(word);
            writer.Write(rules.MinStopWords);
            writer.Write(rules.Overrides.Count);
            foreach (var (key, values) in rules.Overrides)
            {
                writer.Write(key);
                writer.Write(values.Count);
                foreach (var value in values)
                    writer.Write(value);
            }
        }
    }

    private static LanguageModel ReadWitlid2(BinaryReader reader, Stream stream)
    {
        var name = reader.ReadString();

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        var previous = (string?)null;
        var metadataCount = Count(reader, stream);
        for (var i = 0; i < metadataCount; i++)
        {
            var key = reader.ReadString();
            previous = InOrder(previous, key, "metadata");
            metadata[key] = reader.ReadString();
        }

        var profiles = new List<LanguageProfile>();
        var rules = new List<LanguageRules>();
        var limits = new Dictionary<string, int>(StringComparer.Ordinal);
        previous = null;
        var profileCount = Count(reader, stream);
        for (var i = 0; i < profileCount; i++)
        {
            var code = reader.ReadString();
            previous = InOrder(previous, code, "profiles");

            var gramCount = Count(reader, stream);
            var grams = new List<string>(gramCount);
            for (var j = 0; j < gramCount; j++)
                grams.Add(reader.ReadString());
            profiles.Add(new LanguageProfile(code, grams));

            if (!reader.ReadBoolean())
                continue;

            var script = reader.ReadString();
            var limit = reader.ReadInt32();

            var words = new List<string>();
            var previousWord = (string?)null;
            var wordCount = Count(reader, stream);
            for (var j = 0; j < wordCount; j++)
            {
                var word = reader.ReadString();
                previousWord = InOrder(previousWord, word, $"{code} stop words");
                words.Add(word);
            }

            var minStopWords = reader.ReadInt32();

            var overrides = new List<KeyValuePair<string, IReadOnlyList<double>>>();
            var previousKey = (string?)null;
            var overrideCount = Count(reader, stream);
            for (var j = 0; j < overrideCount; j++)
            {
                var key = reader.ReadString();
                previousKey = InOrder(previousKey, key, $"{code} thresholds");
                var valueCount = Count(reader, stream);
                var values = new double[valueCount];
                for (var v = 0; v < valueCount; v++)
                    values[v] = reader.ReadDouble();
                overrides.Add(new KeyValuePair<string, IReadOnlyList<double>>(key, values));
            }

            rules.Add(new LanguageRules(code, script, words, minStopWords, overrides));
            limits[code] = limit;
        }

        if (stream.Position != stream.Length)
            throw new FormatException("the language pack has bytes after its end");

        return new LanguageModel(name, profiles, new LanguageRuleSet(rules), limits, metadata);
    }

    private static int Count(BinaryReader reader, Stream stream)
    {
        // Every element takes at least one byte: a count past the bytes left is not a pack.
        var count = reader.ReadInt32();
        if (count < 0 || count > stream.Length - stream.Position)
            throw new FormatException("the language pack holds an impossible count");

        return count;
    }

    private static string InOrder(string? previous, string current, string what)
    {
        if (previous != null && string.CompareOrdinal(previous, current) >= 0)
            throw new FormatException($"the language pack's {what} are not in their canonical order");

        return current;
    }

    #endregion
}
