using System.Buffers.Binary;
using MemoryPack;
using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// Working files the server keeps a taskset's tasks in between reading the taskset and storing
/// its packages: one file a runtime, each task appended as it is read and read back alone when
/// its batch is cut. A plan groups the tasks by runtime, and a taskset names its runtimes in any
/// order, so the grouping needs the tasks twice; here they wait on the disk, not in memory. The
/// files are about as large as the taskset and are deleted when the spool is disposed.
/// </summary>
public sealed class VerifyTaskSpool : IDisposable
{
    #region Constants

    private const string EXTENSION = ".tasks";
    private const int WRITE_BUFFER_BYTES = 128 * 1024;

    #endregion

    #region Fields

    private readonly string m_folder;
    private readonly Dictionary<string, FileStream> m_files = new(StringComparer.Ordinal);
    private readonly List<string> m_runtimes = [];
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>A spool in a folder of its own.</summary>
    /// <param name="folder">The folder; made here and removed when the spool is disposed.</param>
    public VerifyTaskSpool(string folder)
    {
        m_folder = folder;
        Directory.CreateDirectory(folder);
    }

    #endregion

    #region Functions

    /// <summary>Appends a task to its runtime's file.</summary>
    /// <param name="task">The task.</param>
    public void Add(VerifyTaskData task)
    {
        if (!m_files.TryGetValue(task.RuntimeId, out var file))
        {
            file = new FileStream(Path.Combine(m_folder, $"{m_files.Count}{EXTENSION}"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, WRITE_BUFFER_BYTES);
            m_files[task.RuntimeId] = file;
            m_runtimes.Add(task.RuntimeId);
        }

        var image = MemoryPackSerializer.Serialize(task);

        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, image.Length);
        file.Write(length);
        file.Write(image);
        Tasks++;
    }

    /// <summary>Reads the tasks of a runtime back, in the order they were added, one at a time.</summary>
    /// <param name="runtimeId">The runtime.</param>
    /// <returns>The tasks.</returns>
    /// <exception cref="InvalidDataException">The file does not hold what was written to it.</exception>
    public IEnumerable<VerifyTaskData> Read(string runtimeId)
    {
        if (!m_files.TryGetValue(runtimeId, out var file))
            yield break;

        file.Flush();
        file.Seek(0, SeekOrigin.Begin);

        var length = new byte[sizeof(int)];
        while (file.ReadAtLeast(length, length.Length, throwOnEndOfStream: false) == length.Length)
        {
            var image = new byte[BinaryPrimitives.ReadInt32LittleEndian(length)];
            file.ReadExactly(image);

            yield return MemoryPackSerializer.Deserialize<VerifyTaskData>(image)
                         ?? throw new InvalidDataException("The task spool holds no task where one was kept.");
        }
    }

    #endregion

    #region IDisposable

    /// <summary>Closes and deletes the files.</summary>
    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;
        foreach (var file in m_files.Values)
            file.Dispose();

        try
        {
            if (Directory.Exists(m_folder))
                Directory.Delete(m_folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // working files of the server's own; best-effort
        }
    }

    #endregion

    #region Properties

    /// <summary>The runtimes that have tasks, in the order the first task of each was added.</summary>
    public IReadOnlyList<string> Runtimes => m_runtimes;

    /// <summary>The tasks added.</summary>
    public long Tasks { get; private set; }

    #endregion
}
