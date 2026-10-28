namespace OutWit.Controller.OpenFOAM.Model.Rules;

/// <summary>
/// One function object a case runs, as <see cref="FoamCaseFunctionObjects"/>
/// reads it from <c>system/controlDict</c>: a block of the <c>functions</c>
/// dictionary, a block of a file the dictionary <c>#include</c>s, or a
/// <c>#includeFunc</c> line - the case's own <c>system/&lt;name&gt;</c> when
/// the case ships one (v2606 looks there first), the kit's
/// <c>etc/caseDicts/postProcessing</c> otherwise.
/// </summary>
public sealed class FoamCaseFunctionObject
{
    #region Constructors

    /// <summary>
    /// A function object.
    /// </summary>
    /// <param name="name">Its name: the block's keyword, or the <c>#includeFunc</c> name before its arguments.</param>
    /// <param name="type">Its <c>type</c>; null when the text read names none (a kit dictionary the Model does not know).</param>
    /// <param name="body">The text between its braces; empty for a kit dictionary.</param>
    /// <param name="relativePath">The case file the text was read from; the kit's path for a kit dictionary.</param>
    /// <param name="line">The 1-based line of the block or the <c>#includeFunc</c> in <paramref name="relativePath"/>.</param>
    /// <param name="source">Where the function object comes from.</param>
    /// <param name="arguments">The arguments of an <c>#includeFunc</c> (<c>p, U</c> of <c>probes(p, U)</c>); empty otherwise.</param>
    /// <param name="typedByKit">True when the type comes from a dictionary of the kit (<see cref="TypedByKit"/>).</param>
    public FoamCaseFunctionObject(string name, string? type, string body, string relativePath, int line, FoamCaseFunctionObjectSource source, string arguments = "", bool typedByKit = false)
    {
        Name = name;
        Type = type;
        Body = body;
        RelativePath = relativePath;
        Line = line;
        Source = source;
        Arguments = arguments;
        TypedByKit = typedByKit;
    }

    #endregion

    #region Functions

    /// <summary>
    /// The value text of an entry of the body, as written (<c>(0 0 0)</c>,
    /// <c>rhoInf</c>, <c>{ origin (0 0 0); }</c>); null when the body has no
    /// such top-level entry. A <c>$</c> reference stays a reference.
    /// </summary>
    /// <param name="keyword">The entry's keyword.</param>
    /// <returns>The value, trimmed, without its semicolon.</returns>
    public string? Entry(string keyword)
    {
        return FoamCaseFunctionObjects.Entry(Body, keyword);
    }

    public override string ToString()
    {
        return $"{Name}: {Type ?? "?"} ({Source}, {RelativePath}:{Line})";
    }

    #endregion

    #region Properties

    /// <summary>The function object's name.</summary>
    public string Name { get; }

    /// <summary>The function object's type; null when unknown.</summary>
    public string? Type { get; }

    /// <summary>The dictionary text between its braces; empty for a kit dictionary.</summary>
    public string Body { get; }

    /// <summary>The file the function object was read from.</summary>
    public string RelativePath { get; }

    /// <summary>The 1-based line in <see cref="RelativePath"/>.</summary>
    public int Line { get; }

    /// <summary>Where the function object comes from.</summary>
    public FoamCaseFunctionObjectSource Source { get; }

    /// <summary>The arguments of an <c>#includeFunc</c>, as written; empty otherwise.</summary>
    public string Arguments { get; }

    /// <summary>
    /// Whether the type is the kit's: a dictionary of the kit's
    /// <c>etc/caseDicts/postProcessing</c> pulled in by <c>#includeFunc</c>, or
    /// a body without a <c>type</c> of its own that <c>#includeEtc</c>s a kit
    /// cfg (pitzDaily's <c>system/streamlines</c>); the type is the kit's cfg's,
    /// <see cref="Type"/> when the Model knows it, null otherwise.
    /// </summary>
    public bool TypedByKit { get; }

    #endregion
}

/// <summary>
/// Where a function object of a case comes from.
/// </summary>
public enum FoamCaseFunctionObjectSource
{
    /// <summary>A block of the <c>functions</c> dictionary of <c>system/controlDict</c>.</summary>
    Block = 0,

    /// <summary>A block of a case file the <c>functions</c> dictionary <c>#include</c>s.</summary>
    IncludedFile = 1,

    /// <summary>An <c>#includeFunc</c> of a dictionary the case ships under <c>system/</c>.</summary>
    IncludedCaseDictionary = 2,

    /// <summary>An <c>#includeFunc</c> of a dictionary of the kit's <c>etc/caseDicts/postProcessing</c>.</summary>
    IncludedKitDictionary = 3
}
