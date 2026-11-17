namespace OutWit.Controller.AI.Wasm;

/// <summary>The WASI preview 1 error numbers the host's own calls answer with.</summary>
internal static class WasmHostErrno
{
    #region Constants

    /// <summary>No error.</summary>
    public const int SUCCESS = 0;

    /// <summary>Permission denied.</summary>
    public const int ACCES = 2;

    /// <summary>Bad file descriptor.</summary>
    public const int BADF = 8;

    /// <summary>A pointer outside the guest's memory.</summary>
    public const int FAULT = 21;

    /// <summary>File too large.</summary>
    public const int FBIG = 22;

    /// <summary>Illegal byte sequence (a path that is not UTF-8).</summary>
    public const int ILSEQ = 25;

    /// <summary>Invalid argument.</summary>
    public const int INVAL = 28;

    /// <summary>No such file or directory.</summary>
    public const int NOENT = 44;

    /// <summary>No space left on device.</summary>
    public const int NOSPC = 51;

    /// <summary>Function not supported.</summary>
    public const int NOSYS = 52;

    /// <summary>Not a directory.</summary>
    public const int NOTDIR = 54;

    /// <summary>Operation not permitted.</summary>
    public const int PERM = 63;

    /// <summary>Read-only file system.</summary>
    public const int ROFS = 69;

    /// <summary>Invalid seek (a stream has no position).</summary>
    public const int SPIPE = 70;

    #endregion
}
