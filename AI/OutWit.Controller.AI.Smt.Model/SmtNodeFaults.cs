namespace OutWit.Controller.AI.Smt.Model;

/// <summary>
/// Why a node could not work on a package at all (<see cref="SmtResultPackageData.FaultCode"/>):
/// a short, fixed list. A code names what kept the node from the work; it never carries the
/// node's own words, a path or an exception text. The package's problems have no answer from
/// that node, and the work goes elsewhere. A problem that fails on its own is never one of these:
/// it is a row with its status.
/// </summary>
public static class SmtNodeFaults
{
    #region Constants

    /// <summary>The package was made for another controller version than the node runs.</summary>
    public const string VERSION_MISMATCH = "version_mismatch";

    /// <summary>The package's reference is not one: no blob, no options, a number of problems out of the range a node takes.</summary>
    public const string PACKAGE_INVALID = "package_invalid";

    /// <summary>The package's ticket is not in the form of one.</summary>
    public const string TICKET_INVALID = "ticket_invalid";

    /// <summary>The package's blob is larger than a node takes, by its reference or by its bytes.</summary>
    public const string PACKAGE_TOO_LARGE = "package_too_large";

    /// <summary>The blob's bytes are not the ones its reference names: another size or another SHA-256.</summary>
    public const string PACKAGE_HASH_MISMATCH = "package_hash_mismatch";

    /// <summary>The blob is not a package of problems this controller reads: another format, a damaged stream, a frame past its bound or one that cannot be decoded.</summary>
    public const string PACKAGE_UNREADABLE = "package_unreadable";

    /// <summary>The blob reads, but does not hold the problems its reference names: another number of them, or other indices.</summary>
    public const string PACKAGE_MISMATCH = "package_mismatch";

    /// <summary>This process has no sandbox for the solver: no solver module beside the controller, or an engine library or binding that is not the pinned one.</summary>
    public const string SANDBOX_UNAVAILABLE = "sandbox_unavailable";

    /// <summary>The machine's available memory does not hold one session under the package's options.</summary>
    public const string MEMORY_INSUFFICIENT = "memory_insufficient";

    /// <summary>The package's blob could not be downloaded.</summary>
    public const string DOWNLOAD_FAILED = "download_failed";

    /// <summary>The rows were written but could not be uploaded.</summary>
    public const string UPLOAD_FAILED = "upload_failed";

    /// <summary>Every code, in the order the documents list them.</summary>
    public static readonly IReadOnlyList<string> ALL =
    [
        VERSION_MISMATCH, PACKAGE_INVALID, TICKET_INVALID, PACKAGE_TOO_LARGE, PACKAGE_HASH_MISMATCH, PACKAGE_UNREADABLE, PACKAGE_MISMATCH,
        SANDBOX_UNAVAILABLE, MEMORY_INSUFFICIENT, DOWNLOAD_FAILED, UPLOAD_FAILED
    ];

    #endregion
}
