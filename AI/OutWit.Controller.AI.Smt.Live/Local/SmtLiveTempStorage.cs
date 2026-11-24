using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Live.Local;

/// <summary>
/// The temp storage of a local run: scopes and files under one folder of the run's own, as a
/// node's client gives the controller a folder of the client's own. The solver's sandbox keeps
/// the stdio files of its sessions in a scope of it, so a local run writes nothing outside the
/// folders it is given.
/// </summary>
internal sealed class SmtLiveTempStorage : IWitTempStorage
{
    #region Constructors

    /// <summary>Makes the storage under a folder.</summary>
    /// <param name="rootPath">The folder; made when it is not there.</param>
    public SmtLiveTempStorage(string rootPath)
    {
        RootPath = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(RootPath);
    }

    #endregion

    #region IWitTempStorage

    /// <inheritdoc />
    public string CreateScope(string label)
    {
        var safe = string.Concat(label.Select(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
        var path = Path.Combine(RootPath, $"{safe}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <inheritdoc />
    public string CreateTempFile(string? extension = null)
    {
        var suffix = string.IsNullOrEmpty(extension) ? "" : extension.StartsWith('.') ? extension : "." + extension;
        return Path.Combine(RootPath, $"{Guid.NewGuid():N}{suffix}");
    }

    /// <inheritdoc />
    public void DeleteScope(string scopePath)
    {
        try
        {
            if (Directory.Exists(scopePath))
                Directory.Delete(scopePath, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // best-effort: the run's work folder goes as a whole at its end
        }
    }

    /// <inheritdoc />
    public void DeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // best-effort, as above
        }
    }

    #endregion

    #region Properties

    /// <inheritdoc />
    public string RootPath { get; }

    #endregion
}
