using System.Security.Cryptography;
using System.Text;

namespace OutWit.Controller.AI.Corpus.Language;

/// <summary>
/// A set of language profiles with one binary serialization: a small header, then per
/// language its code and ranked n-grams, UTF-8, in code order — so the same profiles give
/// the same bytes and one SHA-256 pins the model in the pipeline contract.
/// </summary>
public sealed class LanguageModel
{
    #region Constants

    private const string MAGIC = "WITLID1";

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a model from profiles (ordered by code for a stable serialization).
    /// </summary>
    /// <param name="profiles">The profiles, one per language code.</param>
    public LanguageModel(IEnumerable<LanguageProfile> profiles)
    {
        Profiles = profiles.OrderBy(profile => profile.Code, StringComparer.Ordinal).ToList();
    }

    #endregion

    #region Functions

    /// <summary>
    /// Serializes the model.
    /// </summary>
    /// <returns>The model bytes.</returns>
    public byte[] ToBytes()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(MAGIC);
            writer.Write(Profiles.Count);
            foreach (var profile in Profiles)
            {
                writer.Write(profile.Code);
                writer.Write(profile.Grams.Count);
                foreach (var gram in profile.Grams)
                    writer.Write(gram);
            }
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Reads a model.
    /// </summary>
    /// <param name="bytes">The model bytes.</param>
    /// <returns>The model.</returns>
    /// <exception cref="FormatException">The bytes are not a model.</exception>
    public static LanguageModel FromBytes(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream, Encoding.UTF8);

        if (reader.ReadString() != MAGIC)
            throw new FormatException("not a language model");

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

    /// <summary>
    /// Loads a model file.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The model.</returns>
    public static LanguageModel Load(string path)
    {
        return FromBytes(File.ReadAllBytes(path));
    }

    /// <summary>Lower-case hex SHA-256 of the serialized model: the pin the pipeline carries.</summary>
    /// <returns>The digest.</returns>
    public string Sha256()
    {
        return Convert.ToHexStringLower(SHA256.HashData(ToBytes()));
    }

    #endregion

    #region Properties

    /// <summary>The profiles in code order.</summary>
    public IReadOnlyList<LanguageProfile> Profiles { get; }

    #endregion
}
