using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Storage;

/// <summary>
/// The deployment's storage limit as the host process can tell it, from two places:
/// <list type="bullet">
/// <item>the server's blob service, which a host-side activity receives as its
/// <see cref="IWitBlobService"/>: a public <c>Task&lt;long&gt; GetJobStorageHeadroomBytesAsync()</c>
/// and a public <c>int JobBlobRetentionDays</c> on it are read by reflection, on purpose (the
/// precedent is the activity progress sink): the engine's interfaces carry no such member, a
/// server that has them states its headroom, an older one simply does not, and a node never
/// asks;</item>
/// <item>the environment variable <see cref="HEADROOM_VARIABLE"/> of the host process, in
/// bytes: the operator's own word, for a server that does not state one yet and for
/// tests.</item>
/// </list>
/// When both state a headroom the smaller holds. A value that is absent, unreadable or not
/// above zero is not stated.
/// </summary>
public sealed class CorpusStorageHeadroomHost : ICorpusStorageHeadroom
{
    #region Constants

    /// <summary>The environment variable in which a server's operator states the headroom of one job, in bytes.</summary>
    public const string HEADROOM_VARIABLE = "OMNIBUSCLOUD_CORPUS_STORAGE_HEADROOM_BYTES";

    /// <summary>The method of the server's blob service that answers the headroom of one job in bytes (0 = not stated).</summary>
    public const string HEADROOM_METHOD = "GetJobStorageHeadroomBytesAsync";

    /// <summary>The property of the server's blob service that answers the retention window in days.</summary>
    public const string RETENTION_PROPERTY = "JobBlobRetentionDays";

    /// <summary>The server setting behind <see cref="HEADROOM_METHOD"/>, for the message.</summary>
    public const string SERVER_SETTING = "BlobStorage:JobStorageHeadroomBytes";

    private static readonly ConcurrentDictionary<Type, (MethodInfo? Headroom, PropertyInfo? Retention)> MEMBERS = new();

    #endregion

    #region Fields

    private readonly IWitBlobService? m_blobService;
    private readonly Func<string, string?> m_environment;

    #endregion

    #region Constructors

    /// <summary>Creates the host's source.</summary>
    /// <param name="blobService">The blob service the activity received (null = none).</param>
    /// <param name="environment">Reads an environment variable (null = this process's environment).</param>
    public CorpusStorageHeadroomHost(IWitBlobService? blobService, Func<string, string?>? environment = null)
    {
        m_blobService = blobService;
        m_environment = environment ?? Environment.GetEnvironmentVariable;
    }

    #endregion

    #region Functions

    /// <summary>Reads a byte count as the environment variable carries it: a whole number, no unit.</summary>
    /// <param name="text">The variable's value.</param>
    /// <returns>The bytes, or 0 when the text is not a positive whole number.</returns>
    public static long ParseBytes(string? text)
    {
        return long.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var bytes) && bytes > 0 ? bytes : 0;
    }

    private async Task<(long Headroom, int Retention)> AskServerAsync()
    {
        if (m_blobService == null)
            return (0, 0);

        var members = MEMBERS.GetOrAdd(m_blobService.GetType(), static type => (
            type.GetMethod(HEADROOM_METHOD, BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes) is { ReturnType: var returned } method && returned == typeof(Task<long>) ? method : null,
            type.GetProperty(RETENTION_PROPERTY, BindingFlags.Public | BindingFlags.Instance) is { PropertyType: var kind } property && kind == typeof(int) ? property : null));

        // A server that cannot answer one of the two has stated nothing there; the preflight says so.
        long headroom = 0;
        try
        {
            if (members.Headroom?.Invoke(m_blobService, null) is Task<long> answer)
                headroom = Math.Max(0, await answer);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            headroom = 0;
        }

        var retention = 0;
        try
        {
            if (members.Retention?.GetValue(m_blobService) is int days)
                retention = Math.Max(0, days);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            retention = 0;
        }

        return (headroom, retention);
    }

    #endregion

    #region ICorpusStorageHeadroom

    /// <summary>Asks the server's blob service and the environment.</summary>
    /// <param name="cancellationToken">Cancels the question.</param>
    /// <returns>The limit; <see cref="CorpusStorageLimit.NONE"/> when nothing is stated.</returns>
    public async Task<CorpusStorageLimit> GetAsync(CancellationToken cancellationToken = default)
    {
        var (serverHeadroom, retention) = await AskServerAsync();
        var operatorHeadroom = ParseBytes(m_environment(HEADROOM_VARIABLE));

        var stated = new List<(long Bytes, string Source)>();
        if (serverHeadroom > 0)
            stated.Add((serverHeadroom, $"the server ({SERVER_SETTING})"));
        if (operatorHeadroom > 0)
            stated.Add((operatorHeadroom, $"the server's operator ({HEADROOM_VARIABLE})"));

        if (stated.Count == 0)
            return new CorpusStorageLimit { RetentionDays = retention };

        var smallest = stated.OrderBy(entry => entry.Bytes).First();
        return new CorpusStorageLimit { HeadroomBytes = smallest.Bytes, RetentionDays = retention, Source = smallest.Source };
    }

    #endregion
}
