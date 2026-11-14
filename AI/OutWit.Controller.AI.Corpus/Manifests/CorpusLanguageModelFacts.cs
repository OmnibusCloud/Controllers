using OutWit.Controller.AI.Corpus.Language;

namespace OutWit.Controller.AI.Corpus.Manifests;

/// <summary>
/// What the host learns by reading a job's language model before any task is sent: whether this
/// controller can use it at all — a model of a format it does not read, a truncated or
/// non-canonical pack, a pack setting a threshold this controller does not have are refused
/// here, by name, instead of failing on every node as <c>PipelineUnavailable</c> — and, when it
/// can, its format, name and the languages a document may be labelled with.
/// </summary>
/// <param name="Format">The model's format (<see cref="LanguageModelFormat.WITLID1"/> or <see cref="LanguageModelFormat.WITLID2"/>), or "" when it cannot be read.</param>
/// <param name="Name">The pack's name ("" for a WITLID1 model).</param>
/// <param name="Languages">The languages a document may be labelled with, in code order.</param>
/// <param name="KnownLanguages">How many more languages the pack knows and does not support.</param>
/// <param name="Problem">Why this controller cannot use the model, or null when it can.</param>
public sealed record CorpusLanguageModelFacts(string Format, string Name, IReadOnlyList<string> Languages, int KnownLanguages, string? Problem)
{
    #region Functions

    /// <summary>Reads a model's bytes.</summary>
    /// <param name="bytes">The model blob's bytes.</param>
    /// <returns>The facts; <see cref="Problem"/> says why the model cannot be used.</returns>
    public static CorpusLanguageModelFacts Read(byte[] bytes)
    {
        try
        {
            var model = LanguageModel.FromBytes(bytes);
            return new CorpusLanguageModelFacts(model.Format, model.Name, model.Languages, model.Profiles.Count - model.Languages.Count, null);
        }
        catch (FormatException exception)
        {
            return new CorpusLanguageModelFacts("", "", [], 0, exception.Message);
        }
    }

    /// <summary>Reads a model file.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The facts.</returns>
    public static CorpusLanguageModelFacts Load(string path)
    {
        return Read(File.ReadAllBytes(path));
    }

    #endregion
}
