using System.Text;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// A task's sources as files, for a runtime whose guest file system is the task itself (Lua, C
/// and C++): written into a fresh folder, preopened read-only as the guest's "/", removed after
/// the task. The names are the portable set (<see cref="VerifySourceNames"/>), and the guest
/// reaches the folder through a table of exactly those names (<see cref="WasmHostNames"/>): a
/// lookup under another spelling (<c>Util.h</c> for <c>util.h</c>) finds nothing on any node,
/// whatever the node's own file system would have matched.
/// </summary>
internal static class VerifyTaskSources
{
    #region Constants

    private static readonly UTF8Encoding UTF8 = new(encoderShouldEmitUTF8Identifier: false);

    #endregion

    #region Functions

    /// <summary>
    /// What is wrong with the task's sources as files, or null when they can be written.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <returns>The problem in words, or null.</returns>
    public static string? Problem(VerifyTaskData task)
    {
        return task.Sources.Count == 0 ? "the task has no sources" : VerifySourceNames.Problem(task.Sources);
    }

    /// <summary>
    /// Writes the task's sources into <paramref name="folder"/> (created), as UTF-8 without a
    /// byte order mark. Call <see cref="Problem"/> first.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <param name="folder">The folder to write into; it must not exist yet.</param>
    /// <returns>The table of the names written: what the guest may reach in the folder.</returns>
    /// <exception cref="IOException">The node cannot write the files (a fault of the node).</exception>
    public static WasmHostNames Write(VerifyTaskData task, string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var source in task.Sources)
        {
            var path = Path.Combine(folder, source.Name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, source.Content ?? "", UTF8);
        }

        return new WasmHostNames(task.Sources.Select(source => source.Name));
    }

    #endregion
}
