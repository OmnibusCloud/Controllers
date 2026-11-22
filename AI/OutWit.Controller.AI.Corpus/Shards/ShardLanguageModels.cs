using System.Security.Cryptography;
using OutWit.Controller.AI.Corpus.Language;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// Language models by content hash: a node loads a pinned model once and keeps it for the
/// shards of the jobs that pin the same bytes — the last few models it used
/// (<see cref="LanguageModelLimits.MAX_CACHED"/>), not every model it ever saw: a model is a
/// lab's input, and a node that kept each one for the life of its process would hold the memory
/// of every job it ever served. The hash is checked before the model is trusted — a blob that
/// does not match its pin is a different pipeline — and the file's size before it is read.
/// </summary>
public static class ShardLanguageModels
{
    #region Fields

    private static readonly Dictionary<string, (LanguageModel Model, long Used)> m_models = new(StringComparer.Ordinal);
    private static readonly Lock m_lock = new();
    private static long m_clock;

    #endregion

    #region Functions

    /// <summary>Lower-case hex SHA-256 of a file's bytes.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The hash.</returns>
    public static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>Loads a model file, checking it against its pin, or returns the cached instance.</summary>
    /// <param name="path">The model file.</param>
    /// <param name="expectedSha256">The pin (lower-case hex), or "" to accept any.</param>
    /// <returns>The model.</returns>
    /// <exception cref="InvalidDataException">The file does not match the pin.</exception>
    /// <exception cref="FormatException">The file is no model this controller reads, or a model past its limits.</exception>
    public static LanguageModel Load(string path, string expectedSha256)
    {
        if (expectedSha256.Length > 0 && TryGet(expectedSha256, out var cached))
            return cached;

        // The size first: a file past the limit is neither hashed nor read.
        if (new FileInfo(path).Length > LanguageModelLimits.MAX_BYTES)
            throw new FormatException($"the language model is larger than {LanguageModelLimits.MAX_BYTES} bytes");

        var actual = Hash(path);
        if (expectedSha256.Length > 0 && !string.Equals(actual, expectedSha256, StringComparison.Ordinal))
            throw new InvalidDataException("The language model's hash does not match its pin.");

        var model = LanguageModel.Load(path);
        Keep(actual, model);
        return model;
    }

    /// <summary>Forgets every cached model (tests).</summary>
    public static void Clear()
    {
        lock (m_lock)
            m_models.Clear();
    }

    private static bool TryGet(string sha256, out LanguageModel model)
    {
        lock (m_lock)
        {
            if (m_models.TryGetValue(sha256, out var entry))
            {
                m_models[sha256] = (entry.Model, ++m_clock);
                model = entry.Model;
                return true;
            }
        }

        model = null!;
        return false;
    }

    private static void Keep(string sha256, LanguageModel model)
    {
        lock (m_lock)
        {
            m_models[sha256] = (model, ++m_clock);
            while (m_models.Count > LanguageModelLimits.MAX_CACHED)
                m_models.Remove(m_models.MinBy(pair => pair.Value.Used).Key);
        }
    }

    #endregion

    #region Properties

    /// <summary>Models the process holds now.</summary>
    public static int Cached
    {
        get
        {
            lock (m_lock)
                return m_models.Count;
        }
    }

    #endregion
}
