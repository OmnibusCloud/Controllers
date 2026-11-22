using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Corpus.Storage;

/// <summary>
/// What the deployment says about the room one job has in its blob store: the most a job may
/// leave there, the days a job's products are kept, and who said so. Either number may be
/// unstated (0): a deployment that states no headroom refuses nothing on storage.
/// </summary>
public sealed class CorpusStorageLimit : ModelBase
{
    #region Constants

    /// <summary>Nothing stated.</summary>
    public static readonly CorpusStorageLimit NONE = new();

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not CorpusStorageLimit other)
            return false;

        return HeadroomBytes.Is(other.HeadroomBytes)
               && RetentionDays.Is(other.RetentionDays)
               && Source.Is(other.Source);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override CorpusStorageLimit Clone()
    {
        return new CorpusStorageLimit { HeadroomBytes = HeadroomBytes, RetentionDays = RetentionDays, Source = Source };
    }

    #endregion

    #region Properties

    /// <summary>The most one job may leave in the blob store; 0 = not stated.</summary>
    [ToString]
    public long HeadroomBytes { get; init; }

    /// <summary>Days a job's products are kept after they are made; 0 = not stated.</summary>
    [ToString]
    public int RetentionDays { get; init; }

    /// <summary>Who stated the headroom, for the message ("" when nobody did).</summary>
    [ToString]
    public string Source { get; init; } = "";

    /// <summary>Whether a headroom is stated.</summary>
    public bool IsStated => HeadroomBytes > 0;

    #endregion
}
