using System.Collections.Concurrent;
using System.Security.Cryptography;
using OutWit.Controller.AI.Corpus.Language;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// Language models by content hash: a node loads a pinned model once and keeps it for every
/// shard of every job that pins the same bytes. The hash is checked before the model is
/// trusted — a blob that does not match its pin is a different pipeline.
/// </summary>
public static class ShardLanguageModels
{
    #region Fields

    private static readonly ConcurrentDictionary<string, LanguageModel> m_models = new(StringComparer.Ordinal);

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
    public static LanguageModel Load(string path, string expectedSha256)
    {
        if (expectedSha256.Length > 0 && m_models.TryGetValue(expectedSha256, out var cached))
            return cached;

        var actual = Hash(path);
        if (expectedSha256.Length > 0 && !string.Equals(actual, expectedSha256, StringComparison.Ordinal))
            throw new InvalidDataException($"The language model's hash {actual} does not match the pinned {expectedSha256}.");

        var model = LanguageModel.Load(path);
        m_models[actual] = model;
        return model;
    }

    /// <summary>Forgets every cached model (tests).</summary>
    public static void Clear()
    {
        m_models.Clear();
    }

    #endregion
}
