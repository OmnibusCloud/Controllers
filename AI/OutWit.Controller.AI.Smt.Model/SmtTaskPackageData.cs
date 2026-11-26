using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Smt.Model;

/// <summary>
/// What one Smt.SolvePackage activity is given: a reference to a blob that holds the package's
/// problems (<see cref="SmtProblemData"/>, in the controller's package format), never the
/// problems themselves, and the run's options. The message that assigns work to a node stays a
/// list of references however large the problem set; the node downloads the blob of each
/// package of its share over the blob channel, in chunks, resumed after a break.
/// <para>
/// A node refuses the whole package before it reads a problem when the package was made for
/// another controller version, names no blob, carries no ticket or more problems than a node
/// takes, and after the download when the blob is not the one named here (its size, its
/// SHA-256) or does not hold exactly the problems named here (their count, their indices).
/// </para>
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order. A published layout
// changes only behind a version fence, so that no reader of the old layout is ever handed the
// new one: MemoryPack throws on a payload of more members than its reader knows, and a member a
// sender may not have arrives as its type's default (null for a text or a list), never as its
// initializer; such a member starts at that default, and a reference one is declared nullable.
public sealed partial class SmtTaskPackageData : ModelBase
{
    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SmtTaskPackageData other)
            return false;

        return ProblemsBlobId.Is(other.ProblemsBlobId)
               && Sha256.Is(other.Sha256)
               && SizeBytes.Is(other.SizeBytes)
               && ProblemCount.Is(other.ProblemCount)
               && Ticket.Is(other.Ticket)
               && RequiredControllerVersion.Is(other.RequiredControllerVersion)
               && IndexDigest.Is(other.IndexDigest)
               && Options.Check(other.Options);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override SmtTaskPackageData Clone()
    {
        return new SmtTaskPackageData
        {
            ProblemsBlobId = ProblemsBlobId,
            Sha256 = Sha256,
            SizeBytes = SizeBytes,
            ProblemCount = ProblemCount,
            Ticket = Ticket,
            RequiredControllerVersion = RequiredControllerVersion,
            IndexDigest = IndexDigest,
            Options = Options?.Clone()
        };
    }

    #endregion

    #region Properties

    /// <summary>The blob with the package's problems (the controller's package format).</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public Guid ProblemsBlobId { get; set; }

    /// <summary>SHA-256 of the blob's bytes, lower-case hex; the node checks it before it reads a problem.</summary>
    [MemoryPackOrder(1)]
    public string Sha256 { get; set; } = "";

    /// <summary>Size of the blob in bytes: what the node downloads.</summary>
    [ToString]
    [MemoryPackOrder(2)]
    public long SizeBytes { get; set; }

    /// <summary>Problems in the package: the work estimate, known without opening the blob.</summary>
    [ToString]
    [MemoryPackOrder(3)]
    public int ProblemCount { get; set; }

    /// <summary>
    /// The package's ticket: 16 random bytes as 32 lower-case hex characters, made when the
    /// package is. It travels to the node with the package and comes back in every row of the
    /// result and in the result package; the host accepts a result only under a ticket it gave
    /// out. A node knows the tickets of its own packages only. Never written to a document.
    /// </summary>
    [MemoryPackOrder(4)]
    public string Ticket { get; set; } = "";

    /// <summary>
    /// The version of the controller the job runs on. A node on another version refuses the
    /// package before it reads a problem, and the work goes to another node.
    /// </summary>
    [ToString]
    [MemoryPackOrder(5)]
    public string RequiredControllerVersion { get; set; } = "";

    /// <summary>
    /// SHA-256 over the indices of the package's problems in ascending order (each as four bytes,
    /// low byte first), lower-case hex: what the blob must hold and a result must answer, no
    /// problem more and none less.
    /// </summary>
    [MemoryPackOrder(6)]
    public string IndexDigest { get; set; } = "";

    /// <summary>
    /// The run's options, resolved by the host: every problem of the package is solved under
    /// them. Null only in a package that is not one; a node refuses it.
    /// </summary>
    [MemoryPackAllowSerialize]
    [MemoryPackOrder(7)]
    public SmtOptionsData? Options { get; set; }

    #endregion
}
