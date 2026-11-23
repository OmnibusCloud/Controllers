using System.Reflection;
using System.Runtime.Loader;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// A load context that notes when one of a few assemblies is asked for, and gives each a copy of
/// its own from a folder: what is loaded here never reaches the default context, and what was
/// asked for is known. Every other assembly is the default context's.
/// </summary>
internal sealed class SmtWatchingLoadContext : AssemblyLoadContext
{
    #region Fields

    private readonly string m_folder;
    private readonly IReadOnlyCollection<string> m_watched;
    private readonly List<string> m_asked = [];

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the context.
    /// </summary>
    /// <param name="folder">The folder the watched assemblies lie in.</param>
    /// <param name="watched">The simple names of the assemblies to watch.</param>
    public SmtWatchingLoadContext(string folder, IReadOnlyCollection<string> watched)
        : base("smt-watching", isCollectible: true)
    {
        m_folder = folder;
        m_watched = watched;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Asked by the runtime when something loaded here names an assembly: a watched one is noted
    /// and loaded here, any other is left to the default context.
    /// </summary>
    /// <param name="assemblyName">The assembly that is asked for.</param>
    /// <returns>The watched assembly's copy in this context, or null.</returns>
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name == null || !m_watched.Contains(assemblyName.Name))
            return null;

        m_asked.Add(assemblyName.Name);
        return LoadFromAssemblyPath(Path.Combine(m_folder, $"{assemblyName.Name}.dll"));
    }

    #endregion

    #region Properties

    /// <summary>How many times a watched assembly was asked for so far.</summary>
    public int Asked => m_asked.Count;

    /// <summary>The watched assemblies that were asked for, in order.</summary>
    public IReadOnlyList<string> Names => m_asked;

    #endregion
}
