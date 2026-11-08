using System.Text;

namespace OutWit.Controller.AI.Corpus.Signatures;

/// <summary>
/// MinHash signatures for near-duplicate detection: a document becomes the set of its
/// word shingles (normalized tokens, <see cref="SHINGLE_WORDS"/> in a row), each shingle is
/// hashed once (64-bit FNV-1a), and <see cref="HASHES"/> universal hash functions
/// (multiply-add-shift with seeds derived from one constant) each keep their minimum. Two
/// signatures agree on about the Jaccard share of their positions. Everything is integer
/// arithmetic over bytes of UTF-8 — the same signature on every node.
/// </summary>
public static class MinHashSigner
{
    #region Constants

    /// <summary>Hash functions per signature.</summary>
    public const int HASHES = 128;

    /// <summary>Words per shingle.</summary>
    public const int SHINGLE_WORDS = 5;

    /// <summary>The signature of a text with fewer words than a shingle: all slots at the maximum.</summary>
    public const ulong EMPTY_SLOT = ulong.MaxValue;

    private const ulong SEED = 0x5DEECE66DC0FFEE1UL;
    private const ulong FNV_OFFSET = 14695981039346656037UL;
    private const ulong FNV_PRIME = 1099511628211UL;

    #endregion

    #region Fields

    private static readonly (ulong A, ulong B)[] s_coefficients = Coefficients();

    #endregion

    #region Functions

    /// <summary>
    /// Computes the signature of a text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The signature: <see cref="HASHES"/> minima.</returns>
    public static ulong[] Sign(string text)
    {
        var signature = new ulong[HASHES];
        Array.Fill(signature, EMPTY_SLOT);

        // The shingle hash is FNV-1a over the UTF-8 bytes of the words joined by a space, so it
        // runs over each word's bytes with a 0x20 between them: no shingle string is built. A
        // repeated shingle yields the same hash and cannot move a minimum, so no set is kept.
        var words = Utf8Words(text);
        for (var start = 0; start + SHINGLE_WORDS <= words.Count; start++)
        {
            var h = FNV_OFFSET;
            for (var w = start; w < start + SHINGLE_WORDS; w++)
            {
                if (w > start)
                {
                    h ^= (byte)' ';
                    h *= FNV_PRIME;
                }

                foreach (var b in words[w])
                {
                    h ^= b;
                    h *= FNV_PRIME;
                }
            }

            for (var i = 0; i < HASHES; i++)
            {
                var (a, b) = s_coefficients[i];
                var value = unchecked(a * h + b);
                if (value < signature[i])
                    signature[i] = value;
            }
        }

        return signature;
    }

    /// <summary>
    /// The estimated Jaccard similarity of two documents from their signatures.
    /// </summary>
    /// <param name="first">A signature.</param>
    /// <param name="second">Another signature.</param>
    /// <returns>The share of agreeing positions, 0 to 1.</returns>
    public static double Similarity(ReadOnlySpan<ulong> first, ReadOnlySpan<ulong> second)
    {
        if (first.Length != second.Length || first.Length == 0)
            throw new ArgumentException("signatures must have the same, non-zero length");

        var agree = 0;
        for (var i = 0; i < first.Length; i++)
        {
            if (first[i] == second[i])
                agree++;
        }

        return (double)agree / first.Length;
    }

    /// <summary>
    /// Serializes a signature (little-endian, fixed width).
    /// </summary>
    /// <param name="signature">The signature.</param>
    /// <returns>The bytes.</returns>
    public static byte[] ToBytes(ReadOnlySpan<ulong> signature)
    {
        var bytes = new byte[signature.Length * sizeof(ulong)];
        for (var i = 0; i < signature.Length; i++)
            BitConverter.TryWriteBytes(bytes.AsSpan(i * sizeof(ulong)), signature[i]);
        return bytes;
    }

    /// <summary>
    /// The LSH band keys of a signature: <paramref name="bands"/> hashes over consecutive rows,
    /// so two documents sharing a band key are candidates for the same cluster.
    /// </summary>
    /// <param name="signature">The signature.</param>
    /// <param name="bands">Band count; must divide the signature length.</param>
    /// <returns>One 64-bit key per band.</returns>
    public static ulong[] BandKeys(ReadOnlySpan<ulong> signature, int bands)
    {
        if (bands <= 0 || signature.Length % bands != 0)
            throw new ArgumentException("bands must divide the signature length");

        var rows = signature.Length / bands;
        var keys = new ulong[bands];
        for (var band = 0; band < bands; band++)
        {
            var h = FNV_OFFSET ^ (ulong)band;
            for (var row = 0; row < rows; row++)
            {
                h ^= signature[band * rows + row];
                h *= FNV_PRIME;
            }

            keys[band] = h;
        }

        return keys;
    }

    /// <summary>
    /// The word shingles of a text: tokens lower-cased by the invariant culture with punctuation
    /// stripped, <see cref="SHINGLE_WORDS"/> consecutive ones joined by a space; duplicates removed.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The distinct shingles, in first-occurrence order.</returns>
    public static IReadOnlyList<string> Shingles(string text)
    {
        var words = Words(text);

        var shingles = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i + SHINGLE_WORDS <= words.Count; i++)
        {
            var shingle = string.Join(' ', words.GetRange(i, SHINGLE_WORDS));
            if (seen.Add(shingle))
                shingles.Add(shingle);
        }

        return shingles;
    }

    /// <summary>The tokens of a text: runs of letters and digits, lower-cased by the invariant culture.</summary>
    private static List<string> Words(string text)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        Span<char> utf16 = stackalloc char[2];
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                var written = Rune.ToLowerInvariant(rune).EncodeToUtf16(utf16);
                word.Append(utf16[..written]);
                continue;
            }

            if (word.Length > 0)
            {
                words.Add(word.ToString());
                word.Clear();
            }
        }

        if (word.Length > 0)
            words.Add(word.ToString());

        return words;
    }

    /// <summary>The tokens as UTF-8, one array per distinct token.</summary>
    private static List<byte[]> Utf8Words(string text)
    {
        var distinct = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var words = new List<byte[]>();
        foreach (var word in Words(text))
        {
            if (!distinct.TryGetValue(word, out var bytes))
                distinct[word] = bytes = Encoding.UTF8.GetBytes(word);
            words.Add(bytes);
        }

        return words;
    }

    private static (ulong, ulong)[] Coefficients()
    {
        // splitmix64 over one constant seed: the hash family is part of the pipeline contract.
        var state = SEED;
        var coefficients = new (ulong, ulong)[HASHES];
        for (var i = 0; i < HASHES; i++)
        {
            var a = Next(ref state) | 1UL; // odd, so the multiply is a bijection
            var b = Next(ref state);
            coefficients[i] = (a, b);
        }

        return coefficients;
    }

    private static ulong Next(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        var z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    #endregion
}
