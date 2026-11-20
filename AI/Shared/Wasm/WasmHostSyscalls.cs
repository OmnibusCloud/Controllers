using Wasmtime;
using WasmEngine = Wasmtime.Engine;

namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// The calls of one run that the host answers itself or looks at before WASI does, so that a
/// bound sits where the resource is spent:
/// <list type="bullet">
/// <item>stdout and stderr are the host's own streams, capped at the write
/// (<see cref="WasmHostOutput"/>): nothing a guest prints past its cap is stored anywhere;</item>
/// <item>a path is resolved by the host first (<see cref="WasmHostFiles"/>): in a folder with a
/// table of names only the names of the table exist, and nothing can be made or changed there;</item>
/// <item>a file's record says nothing of the host: no times, no device, a file number that
/// follows from the guest's path;</item>
/// <item>what a guest writes into its writable folders is counted against one budget;</item>
/// <item>when the run asks for it, a call costs fuel: a fixed amount, and a unit a byte it moves
/// (<see cref="WasmHostMeter"/>), so the host's time is inside the fuel budget;</item>
/// <item>a pointer outside the guest's memory answers "bad address", never an error of the engine.</item>
/// </list>
/// What the host lets through reaches WASI's own definition through the shim
/// (<see cref="WasmHostWasi"/>); the way through costs the guest nothing.
/// </summary>
internal sealed class WasmHostSyscalls : IDisposable
{
    #region Constants

    private const string WASI_MODULE = "wasi_snapshot_preview1";

    private const int STDIN = 0;
    private const int STDOUT = 1;
    private const int STDERR = 2;

    private const int OFLAG_CREATE = 1;
    private const int OFLAG_TRUNCATE = 8;

    private const int WHENCE_SET = 0;

    /// <summary>What making a file or a folder counts for against the writable budget, bytes.</summary>
    public const long ENTRY_COST_BYTES = 4096;

    /// <summary>The resolution every clock of a run reports, nanoseconds.</summary>
    public const long CLOCK_RESOLUTION_NS = 1;

    private const string RANDOM_GET = "random_get";
    private const string CLOCK_RES_GET = "clock_res_get";
    private const string PATH_CREATE_DIRECTORY = "path_create_directory";
    private const string PATH_REMOVE_DIRECTORY = "path_remove_directory";
    private const string PATH_UNLINK_FILE = "path_unlink_file";
    private const string PATH_RENAME = "path_rename";
    private const string PATH_SYMLINK = "path_symlink";
    private const string PATH_LINK = "path_link";
    private const string PATH_READLINK = "path_readlink";

    #endregion

    #region Fields

    private readonly WasmHostRunRequest m_request;
    private readonly WasmHostMeter m_meter;
    private readonly WasmHostWasi m_wasi;
    private readonly WasmHostFiles m_files;
    private readonly WasmHostOutput m_stdout;
    private readonly WasmHostOutput m_stderr;
    private readonly WasmHostRandom m_random;

    private long m_written;

    #endregion

    #region Constructors

    /// <summary>
    /// The calls of one run.
    /// </summary>
    /// <param name="engine">The host's engine.</param>
    /// <param name="shim">The engine's shim.</param>
    /// <param name="request">The run.</param>
    /// <param name="meter">The run's meter.</param>
    /// <exception cref="ArgumentException">Some of the run's folders have a table of names and some have none.</exception>
    /// <exception cref="IOException">The file the run's stdout goes to cannot be made.</exception>
    public WasmHostSyscalls(WasmEngine engine, WasmHostShim shim, WasmHostRunRequest request, WasmHostMeter meter)
    {
        m_request = request;
        m_meter = meter;
        m_wasi = new WasmHostWasi(engine, shim, meter);
        m_files = new WasmHostFiles(request.Preopens);
        m_random = new WasmHostRandom(request.RandomSeed);
        m_stderr = new WasmHostOutput(request.StderrLimitBytes);
        m_stdout = request.StdoutFile == null
            ? new WasmHostOutput(request.StdoutLimitBytes)
            : new WasmHostOutput(request.StdoutFile, request.StdoutLimitBytes);
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Puts the run's calls over WASI's in a linker that has WASI and allows shadowing.
    /// </summary>
    /// <param name="linker">The run's linker.</param>
    public void Define(Linker linker)
    {
        // What WASI still answers by itself costs fuel as well: defined first, so that the host's
        // own definitions below stand over them.
        if (m_meter.Charges)
            WasmHostPlainCalls.Define(linker, m_meter, m_wasi, m_request);

        linker.DefineFunction(WASI_MODULE, RANDOM_GET, (Caller caller, int buffer, int length) =>
        {
            try { return RandomGet(caller, buffer, length); }
            catch (Exception exception) when (Note(exception)) { throw; }
        });
        linker.DefineFunction(WASI_MODULE, CLOCK_RES_GET, (Caller caller, int clock, int result) => ClockResolution(caller, result));

        linker.DefineFunction(WASI_MODULE, WasmHostShim.FD_WRITE, (Caller caller, int descriptor, int buffers, int count, int written) =>
        {
            try { return Write(caller, descriptor, buffers, count, null, written); }
            catch (Exception exception) when (Note(exception)) { throw; }
        });
        linker.DefineFunction(WASI_MODULE, WasmHostShim.FD_PWRITE, (Caller caller, int descriptor, int buffers, int count, long offset, int written) =>
        {
            try { return Write(caller, descriptor, buffers, count, offset, written); }
            catch (Exception exception) when (Note(exception)) { throw; }
        });
        linker.DefineFunction(WASI_MODULE, WasmHostShim.FD_CLOSE, (Caller caller, int descriptor) =>
        {
            try { return Close(caller, descriptor); }
            catch (Exception exception) when (Note(exception)) { throw; }
        });
        linker.DefineFunction(WASI_MODULE, WasmHostShim.FD_FILESTAT_GET, (Caller caller, int descriptor, int record) =>
        {
            try { return Stat(caller, descriptor, record); }
            catch (Exception exception) when (Note(exception)) { throw; }
        });
        linker.DefineFunction(WASI_MODULE, WasmHostShim.PATH_OPEN,
            (Caller caller, int folder, int lookup, int path, int pathLength, int open, long rights, long inheriting, int flags, int opened) =>
            {
                try { return Open(caller, folder, lookup, path, pathLength, open, rights, inheriting, flags, opened); }
                catch (Exception exception) when (Note(exception)) { throw; }
            });
        linker.DefineFunction(WASI_MODULE, WasmHostShim.PATH_FILESTAT_GET, (Caller caller, int folder, int lookup, int path, int pathLength, int record) =>
        {
            try { return StatPath(caller, folder, lookup, path, pathLength, record); }
            catch (Exception exception) when (Note(exception)) { throw; }
        });

        if (m_request.ChargeHostCalls)
        {
            linker.DefineFunction(WASI_MODULE, WasmHostShim.FD_READ, (Caller caller, int descriptor, int buffers, int count, int read) =>
            {
                try { return Read(caller, descriptor, buffers, count, null, read); }
                catch (Exception exception) when (Note(exception)) { throw; }
            });
            linker.DefineFunction(WASI_MODULE, WasmHostShim.FD_PREAD, (Caller caller, int descriptor, int buffers, int count, long offset, int read) =>
            {
                try { return Read(caller, descriptor, buffers, count, offset, read); }
                catch (Exception exception) when (Note(exception)) { throw; }
            });
        }

        if (m_request.WritableLimitBytes > 0)
        {
            linker.DefineFunction(WASI_MODULE, WasmHostShim.FD_SEEK, (Caller caller, int descriptor, long offset, int whence, int result) =>
            {
                try { return Seek(caller, descriptor, offset, whence, result); }
                catch (Exception exception) when (Note(exception)) { throw; }
            });
            linker.DefineFunction(WASI_MODULE, WasmHostShim.FD_FILESTAT_SET_SIZE, (Caller caller, int descriptor, long size) =>
            {
                try { return SetSize(caller, descriptor, size); }
                catch (Exception exception) when (Note(exception)) { throw; }
            });
            linker.DefineFunction(WASI_MODULE, WasmHostShim.FD_ALLOCATE, (Caller caller, int descriptor, long offset, long length) =>
            {
                try { return Allocate(caller, descriptor, offset, length); }
                catch (Exception exception) when (Note(exception)) { throw; }
            });
        }

        if (m_files.HasTables)
            DefineReadOnlyNames(linker);
    }

    /// <summary>
    /// In folders with tables of names nothing can be made, removed or renamed, and the answer
    /// does not depend on whether the host's file system would have found the name.
    /// </summary>
    private void DefineReadOnlyNames(Linker linker)
    {
        linker.DefineFunction(WASI_MODULE, PATH_CREATE_DIRECTORY, (Caller caller, int folder, int path, int pathLength) => Refused(caller, PATH_CREATE_DIRECTORY));
        linker.DefineFunction(WASI_MODULE, PATH_REMOVE_DIRECTORY, (Caller caller, int folder, int path, int pathLength) => Refused(caller, PATH_REMOVE_DIRECTORY));
        linker.DefineFunction(WASI_MODULE, PATH_UNLINK_FILE, (Caller caller, int folder, int path, int pathLength) => Refused(caller, PATH_UNLINK_FILE));
        linker.DefineFunction(WASI_MODULE, PATH_RENAME,
            (Caller caller, int folder, int path, int pathLength, int newFolder, int newPath, int newPathLength) => Refused(caller, PATH_RENAME));
        linker.DefineFunction(WASI_MODULE, PATH_SYMLINK,
            (Caller caller, int target, int targetLength, int folder, int path, int pathLength) => Refused(caller, PATH_SYMLINK));
        linker.DefineFunction(WASI_MODULE, PATH_LINK,
            (Caller caller, int folder, int lookup, int path, int pathLength, int newFolder, int newPath, int newPathLength) => Refused(caller, PATH_LINK));
        linker.DefineFunction(WASI_MODULE, PATH_READLINK, (Caller caller, int folder, int path, int pathLength, int buffer, int bufferLength, int used) =>
        {
            try { return ReadLink(caller, folder, path, pathLength); }
            catch (Exception exception) when (Note(exception)) { throw; }
        });
    }

    /// <summary>A call that changes names, in a folder where nothing can be changed: paid for like any call, and refused.</summary>
    private int Refused(Caller caller, string name)
    {
        m_meter.Call(caller, name);
        return WasmHostErrno.PERM;
    }

    #endregion

    #region Functions

    private int RandomGet(Caller caller, int buffer, int length)
    {
        m_meter.Call(caller, RANDOM_GET);

        var memory = caller.GetMemory(WasmHostShim.MEMORY);
        long count = (uint)length;
        if (memory == null || !WasmHostMemory.InRange(memory, buffer, count))
            return WasmHostErrno.FAULT;

        m_meter.Bytes(caller, RANDOM_GET, count);

        // A span holds two gigabytes less a byte; a memory holds no more than four.
        long start = (uint)buffer;
        while (count > 0)
        {
            var take = (int)Math.Min(count, int.MaxValue);
            m_random.Fill(memory.GetSpan(start, take));
            start += take;
            count -= take;
        }

        return WasmHostErrno.SUCCESS;
    }

    private int ClockResolution(Caller caller, int result)
    {
        m_meter.Call(caller, CLOCK_RES_GET);

        var memory = caller.GetMemory(WasmHostShim.MEMORY);
        if (memory == null || !WasmHostMemory.InRange(memory, result, sizeof(long)))
            return WasmHostErrno.FAULT;

        memory.WriteInt64((uint)result, CLOCK_RESOLUTION_NS);
        return WasmHostErrno.SUCCESS;
    }

    private int Write(Caller caller, int descriptor, int buffers, int count, long? offset, int written)
    {
        var name = offset == null ? WasmHostShim.FD_WRITE : WasmHostShim.FD_PWRITE;
        m_meter.Call(caller, name);

        var standard = descriptor is STDOUT or STDERR;
        var counted = m_request.WritableLimitBytes > 0 && m_files.IsWritable(descriptor);

        if (!standard && !counted && !m_meter.Charges)
            return offset is { } at
                ? m_wasi.Forward(caller, () => m_wasi.FdPwrite!(descriptor, buffers, count, at, written))
                : m_wasi.Forward(caller, () => m_wasi.FdWrite!(descriptor, buffers, count, written));

        var memory = caller.GetMemory(WasmHostShim.MEMORY);
        if (memory == null)
            return WasmHostErrno.FAULT;

        // As WASI's own write does, one call takes the first buffer that holds anything.
        var errno = WasmHostMemory.FirstBuffer(memory, buffers, count, out var pointer, out var length);
        if (errno != WasmHostErrno.SUCCESS)
            return errno;

        if (!WasmHostMemory.InRange(memory, written, sizeof(int)))
            return WasmHostErrno.FAULT;

        if (standard && offset != null)
            return WasmHostErrno.SPIPE;

        m_meter.Bytes(caller, name, length);

        if (standard)
        {
            var output = descriptor == STDOUT ? m_stdout : m_stderr;
            if (length > 0)
                output.Write(memory.GetSpan(pointer, length));

            memory.WriteInt32((uint)written, length);

            if (descriptor == STDOUT && output.Cut && m_request.StopAtStdoutLimit)
                throw m_meter.Ended(WasmHostVerdict.OutputLimit);

            return WasmHostErrno.SUCCESS;
        }

        if (counted)
        {
            if (offset is { } position && (position < 0 || position > m_request.WritableLimitBytes - length))
                return WasmHostErrno.FBIG;

            if (!Spend(length))
                return WasmHostErrno.NOSPC;
        }

        return offset is { } place
            ? m_wasi.Forward(caller, () => m_wasi.FdPwrite!(descriptor, buffers, count, place, written))
            : m_wasi.Forward(caller, () => m_wasi.FdWrite!(descriptor, buffers, count, written));
    }

    private int Read(Caller caller, int descriptor, int buffers, int count, long? offset, int read)
    {
        var name = offset == null ? WasmHostShim.FD_READ : WasmHostShim.FD_PREAD;
        m_meter.Call(caller, name);

        // What a read costs besides is what it gave: WASI fills the first buffer that has room,
        // and no more than the file holds.
        if ((uint)count > WasmHostMemory.MAX_BUFFERS)
            return WasmHostErrno.INVAL;

        var result = offset is { } at
            ? m_wasi.Forward(caller, () => m_wasi.FdPread!(descriptor, buffers, count, at, read))
            : m_wasi.Forward(caller, () => m_wasi.FdRead!(descriptor, buffers, count, read));

        var memory = caller.GetMemory(WasmHostShim.MEMORY);
        if (result == WasmHostErrno.SUCCESS && memory != null && WasmHostMemory.InRange(memory, read, sizeof(int)))
            m_meter.Bytes(caller, name, (uint)memory.ReadInt32((uint)read));

        return result;
    }

    private int Close(Caller caller, int descriptor)
    {
        m_meter.Call(caller, WasmHostShim.FD_CLOSE);

        var result = m_wasi.Forward(caller, () => m_wasi.FdClose!(descriptor));
        if (result == WasmHostErrno.SUCCESS)
            m_files.Closed(descriptor);

        return result;
    }

    private int Seek(Caller caller, int descriptor, long offset, int whence, int result)
    {
        m_meter.Call(caller, WasmHostShim.FD_SEEK);

        var errno = m_wasi.Forward(caller, () => m_wasi.FdSeek!(descriptor, offset, whence, result));
        if (errno != WasmHostErrno.SUCCESS || !m_files.IsWritable(descriptor))
            return errno;

        // A position far past the end would make the next write a file of that size.
        var memory = caller.GetMemory(WasmHostShim.MEMORY);
        if (memory == null || !WasmHostMemory.InRange(memory, result, sizeof(long)) || memory.ReadInt64((uint)result) <= m_request.WritableLimitBytes)
            return errno;

        m_wasi.Forward(caller, () => m_wasi.FdSeek!(descriptor, 0, WHENCE_SET, result));
        return WasmHostErrno.INVAL;
    }

    private int SetSize(Caller caller, int descriptor, long size)
    {
        m_meter.Call(caller, WasmHostShim.FD_FILESTAT_SET_SIZE);

        if (m_files.IsWritable(descriptor) && (size < 0 || !Spend(size)))
            return WasmHostErrno.NOSPC;

        return m_wasi.Forward(caller, () => m_wasi.FdFilestatSetSize!(descriptor, size));
    }

    private int Allocate(Caller caller, int descriptor, long offset, long length)
    {
        m_meter.Call(caller, WasmHostShim.FD_ALLOCATE);

        if (m_files.IsWritable(descriptor) && (offset < 0 || length < 0 || offset > long.MaxValue - length || !Spend(offset + length)))
            return WasmHostErrno.NOSPC;

        return m_wasi.Forward(caller, () => m_wasi.FdAllocate!(descriptor, offset, length));
    }

    private int Stat(Caller caller, int descriptor, int record)
    {
        m_meter.Call(caller, WasmHostShim.FD_FILESTAT_GET);

        var result = m_wasi.Forward(caller, () => m_wasi.FdFilestatGet!(descriptor, record));

        var memory = caller.GetMemory(WasmHostShim.MEMORY);
        if (result == WasmHostErrno.SUCCESS && memory != null && WasmHostMemory.InRange(memory, record, WasmHostFiles.FILESTAT_SIZE))
            m_files.Neutralize(memory.GetSpan((uint)record, WasmHostFiles.FILESTAT_SIZE), descriptor);

        return result;
    }

    private int Open(Caller caller, int folder, int lookup, int path, int pathLength, int open, long rights, long inheriting, int flags, int opened)
    {
        m_meter.Call(caller, WasmHostShim.PATH_OPEN);

        var errno = Resolve(caller, WasmHostShim.PATH_OPEN, folder, path, pathLength, out var memory, out var target);
        if (errno != WasmHostErrno.SUCCESS)
            return errno;

        var makes = (open & (OFLAG_CREATE | OFLAG_TRUNCATE)) != 0;
        if (target.Preopen.Names is { } names)
        {
            // Only the table's names exist, and none of them can be made anew or emptied.
            var known = names.IsFile(target.Relative) || names.IsFolder(target.Relative);
            if (makes)
                return WasmHostErrno.PERM;

            if (!known)
                return WasmHostErrno.NOENT;
        }
        else if (makes && (open & OFLAG_CREATE) != 0 && m_request.WritableLimitBytes > 0 && target.Preopen.Writable && !Spend(ENTRY_COST_BYTES))
        {
            return WasmHostErrno.NOSPC;
        }

        var result = m_wasi.Forward(caller, () => m_wasi.PathOpen!(folder, lookup, path, pathLength, open, rights, inheriting, flags, opened));
        if (result == WasmHostErrno.SUCCESS && WasmHostMemory.InRange(memory, opened, sizeof(int)))
            m_files.Opened(memory.ReadInt32((uint)opened), target, WasmHostFiles.IsFolder(target));

        return result;
    }

    private int StatPath(Caller caller, int folder, int lookup, int path, int pathLength, int record)
    {
        m_meter.Call(caller, WasmHostShim.PATH_FILESTAT_GET);

        var errno = Resolve(caller, WasmHostShim.PATH_FILESTAT_GET, folder, path, pathLength, out var memory, out var target);
        if (errno != WasmHostErrno.SUCCESS)
            return errno;

        if (target.Preopen.Names is { } names && !names.IsFile(target.Relative) && !names.IsFolder(target.Relative))
            return WasmHostErrno.NOENT;

        var result = m_wasi.Forward(caller, () => m_wasi.PathFilestatGet!(folder, lookup, path, pathLength, record));
        if (result == WasmHostErrno.SUCCESS && WasmHostMemory.InRange(memory, record, WasmHostFiles.FILESTAT_SIZE))
            WasmHostFiles.Neutralize(memory.GetSpan((uint)record, WasmHostFiles.FILESTAT_SIZE), target);

        return result;
    }

    private int ReadLink(Caller caller, int folder, int path, int pathLength)
    {
        m_meter.Call(caller, PATH_READLINK);

        var errno = Resolve(caller, PATH_READLINK, folder, path, pathLength, out _, out var target);
        if (errno != WasmHostErrno.SUCCESS)
            return errno;

        // A table holds files and folders, never a link.
        var names = target.Preopen.Names!;
        return names.IsFile(target.Relative) || names.IsFolder(target.Relative) ? WasmHostErrno.INVAL : WasmHostErrno.NOENT;
    }

    #endregion

    #region Tools

    private int Resolve(Caller caller, string name, int folder, int path, int pathLength, out Memory memory, out WasmHostFiles.Target target)
    {
        target = default;
        memory = caller.GetMemory(WasmHostShim.MEMORY)!;

        long length = (uint)pathLength;
        if (memory == null || !WasmHostMemory.InRange(memory, path, length) || length > int.MaxValue)
            return WasmHostErrno.FAULT;

        m_meter.Bytes(caller, name, length);
        return m_files.Resolve(folder, memory.GetSpan((uint)path, (int)length), out target);
    }

    /// <summary>Takes bytes from the writable budget; false when it does not hold them.</summary>
    private bool Spend(long bytes)
    {
        if (bytes > m_request.WritableLimitBytes - m_written)
            return false;

        m_written += bytes;
        return true;
    }

    /// <summary>
    /// Looks at an exception on its way out of one of the host's calls, and lets it go on. One
    /// that is neither the end of the run nor the engine's is a fault of the host (a disk that is
    /// full under the file stdout goes to): it is kept, so that the run is reported as the host's
    /// failure and not as the program's.
    /// </summary>
    /// <returns>False, always: the exception is not caught here.</returns>
    private bool Note(Exception exception)
    {
        if (exception is not WasmHostRunEnded and not WasmtimeException)
            Fault ??= exception;

        return false;
    }

    #endregion

    #region IDisposable

    /// <summary>Closes the run's streams and the way to WASI's own calls.</summary>
    public void Dispose()
    {
        m_stdout.Dispose();
        m_stderr.Dispose();
        m_wasi.Dispose();
    }

    #endregion

    #region Properties

    /// <summary>The guest's stdout as kept.</summary>
    public WasmHostOutput Stdout => m_stdout;

    /// <summary>The guest's stderr as kept.</summary>
    public WasmHostOutput Stderr => m_stderr;

    /// <summary>The host's own failure inside one of its calls, or null.</summary>
    public Exception? Fault { get; private set; }

    #endregion
}
