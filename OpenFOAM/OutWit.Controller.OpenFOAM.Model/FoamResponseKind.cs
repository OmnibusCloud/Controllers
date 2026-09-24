namespace OutWit.Controller.OpenFOAM.Model;

/// <summary>
/// The kind of quantity a response request asks for; each maps to one
/// OpenFOAM function object the node writes into the case.
/// </summary>
public enum FoamResponseKind
{
    /// <summary>Drag, lift and moment coefficients on patches (<c>forceCoeffs</c>).</summary>
    ForceCoeffs = 0,

    /// <summary>Forces and moments on patches (<c>forces</c>).</summary>
    Forces = 1,

    /// <summary>A field statistic over patches (<c>surfaceFieldValue</c>: average, integral, min, max).</summary>
    PatchValue = 2,

    /// <summary>A field statistic over the volume or a cell zone (<c>volFieldValue</c>).</summary>
    VolumeValue = 3,

    /// <summary>The domain-wide minimum and maximum of a field (<c>fieldMinMax</c>).</summary>
    FieldMinMax = 4,

    /// <summary>A field sampled at points (<c>probes</c>).</summary>
    Probe = 5
}
