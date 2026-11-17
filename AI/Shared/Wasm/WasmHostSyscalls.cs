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
/// <item>when the run asks for it, a call costs fuel a byte it moves, so the host's time is
/// inside the fuel budget;</item>
/// <item>a pointer outside the guest's memory answers "bad address", never an error of the engine.</item>
/// </list>
/// What the host lets through reaches WASI's own definition through the shim
/// (<see cref="WasmHostShim"/>), at no cost in fuel to the guest.
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

    // The fuel the shim's own instructions may take in one call; given before and taken back
    // after, so that a call through the shim costs the guest nothing.
    private const ulong SHIM_FUEL = 4096;

    #endregion

    #region Fields

    private readonly WasmEngine m_engine;
    private readonly WasmHostShim m_shimModule;
    private readonly WasmHostRunRequest m_request;
    private readonly WasmHostFiles m_files;
    private readonly WasmHostOutput m_stdout;
    private readonly WasmHostOutput m_stderr;
    private readonly WasmHostRandom m_random;

    private Linker? m_wasi;
    private Instance? m_shim;
    private long m_written;

    private Func<int, int, int, int, int>? m_fdWrite;
    private Func<int, int, int, long, int, int>? m_fdPwrite;
    private Func<int, int, int, int, int>? m_fdRead;
    private Func<int, int, int, long, int, int>? m_fdPread;
    private Func<int, int>? m_fdClose;
    private Func<int, long, int, int, int>? m_fdSeek;
    private Func<int, int, int>? m_fdFilestatGet;
    private Func<int, long, int>? m_fdFilestatSetSize;
    private Func<int, long, long, int>? m_fdAllocate;
    private Func<int, int, int, int, int, long, long, int, int, int>? m_pathOpen;
    private Func<int, int, int, int, int, int>? m_pathFilestatGet;

    #endregion

    #region Constructors

    /// <summary>
    /// The calls of one run.
    /// </summary>
    /// <param name="engine">The host's engine.</param>
    /// <param name="shim">The engine's shim.</param>
    /// <param name="request">The run.</param>
    /// <exception cref="ArgumentException">Some of the run's folders have a table of names and some have none.</exception>
    /// <exception cref="IOException">The file the run's stdout goes to cannot be made.</exception>
    public WasmHostSyscalls(WasmEngine engine, WasmHostShim shim, WasmHostRunRequest request)
    {
        m_engine = engine;
        m_shimModule = shim;
        m_request = request;
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
        linker.DefineFunction(WASI_MODULE, "random_get", (Caller caller, int buffer, int length) =>
        {
            try { return RandomGet(caller, buffer, length); }
            catch (Exception exception) when (Note(exception)) { throw; }
        });
        linker.DefineFunction(WASI_MODULE, "clock_res_get", (Caller caller, int clock, int result) => ClockResolution(caller, result));

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
        linker.DefineFunction(WASI_MODULE, "path_create_directory", (Caller caller, int folder, int path, int pathLength) => WasmHostErrno.PERM);
        linker.DefineFunction(WASI_MODULE, "path_remove_directory", (Caller caller, int folder, int path, int pathLength) => WasmHostErrno.PERM);
        linker.DefineFunction(WASI_MODULE, "path_unlink_file", (Caller caller, int folder, int path, int pathLength) => WasmHostErrno.PERM);
        linker.DefineFunction(WASI_MODULE, "path_rename",
            (Caller caller, int folder, int path, int pathLength, int newFolder, int newPath, int newPathLength) => WasmHostErrno.PERM);
        linker.DefineFunction(WASI_MODULE, "path_symlink",
            (Caller caller, int target, int targetLength, int folder, int path, int pathLength) => WasmHostErrno.PERM);
        linker.DefineFunction(WASI_MODULE, "path_link",
            (Caller caller, int folder, int lookup, int path, int pathLength, int newFolder, int newPath, int newPathLength) => WasmHostErrno.PERM);
        linker.DefineFunction(WASI_MODULE, "path_readlink", (Caller caller, int folder, int path, int pathLength, int buffer, int bufferLength, int used) =>
        {
            try { return ReadLink(caller, folder, path, pathLength); }
            catch (Exception exception) when (Note(exception)) { throw; }
        });
    }

    #endregion

    #region Functions

    private int RandomGet(Caller caller, int buffer, int length)
    {
        var memory = caller.GetMemory(WasmHostShim.MEMORY);
        long count = (uint)length;
        if (memory == null || !WasmHostMemory.InRange(memory, buffer, count))
            return WasmHostErrno.FAULT;

        Charge(caller, count);

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

    private static int ClockResolution(Caller caller, int result)
    {
        var memory = caller.GetMemory(WasmHostShim.MEMORY);
        if (memory == null || !WasmHostMemory.InRange(memory, result, sizeof(long)))
            return WasmHostErrno.FAULT;

        memory.WriteInt64((uint)result, CLOCK_RESOLUTION_NS);
        return WasmHostErrno.SUCCESS;
    }

    private int Write(Caller caller, int descriptor, int buffers, int count, long? offset, int written)
    {
        var standard = descriptor is STDOUT or STDERR;
        var counted = m_request.WritableLimitBytes > 0 && m_files.IsWritable(descriptor);

        if (!standard && !counted && !m_request.ChargeHostCalls)
            return offset is { } at
                ? Forward(caller, () => m_fdPwrite!(descriptor, buffers, count, at, written))
                : Forward(caller, () => m_fdWrite!(descriptor, buffers, count, written));

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

        Charge(caller, length);

        if (standard)
        {
            var output = descriptor == STDOUT ? m_stdout : m_stderr;
            if (length > 0)
                output.Write(memory.GetSpan(pointer, length));

            memory.WriteInt32((uint)written, length);

            if (descriptor == STDOUT && output.Cut && m_request.StopAtStdoutLimit)
                throw Ended(WasmHostVerdict.OutputLimit);

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
            ? Forward(caller, () => m_fdPwrite!(descriptor, buffers, count, place, written))
            : Forward(caller, () => m_fdWrite!(descriptor, buffers, count, written));
    }

    private int Read(Caller caller, int descriptor, int buffers, int count, long? offset, int read)
    {
        // What a read costs is what it gave: WASI fills the first buffer that has room, and no
        // more than the file holds.
        if ((uint)count > WasmHostMemory.MAX_BUFFERS)
            return WasmHostErrno.INVAL;

        var result = offset is { } at
            ? Forward(caller, () => m_fdPread!(descriptor, buffers, count, at, read))
            : Forward(caller, () => m_fdRead!(descriptor, buffers, count, read));

        var memory = caller.GetMemory(WasmHostShim.MEMORY);
        if (result == WasmHostErrno.SUCCESS && memory != null && WasmHostMemory.InRange(memory, read, sizeof(int)))
            Charge(caller, (uint)memory.ReadInt32((uint)read));

        return result;
    }

    private int Close(Caller caller, int descriptor)
    {
        var result = Forward(caller, () => m_fdClose!(descriptor));
        if (result == WasmHostErrno.SUCCESS)
            m_files.Closed(descriptor);

        return result;
    }

    private int Seek(Caller caller, int descriptor, long offset, int whence, int result)
    {
        var errno = Forward(caller, () => m_fdSeek!(descriptor, offset, whence, result));
        if (errno != WasmHostErrno.SUCCESS || !m_files.IsWritable(descriptor))
            return errno;

        // A position far past the end would make the next write a file of that size.
        var memory = caller.GetMemory(WasmHostShim.MEMORY);
        if (memory == null || !WasmHostMemory.InRange(memory, result, sizeof(long)) || memory.ReadInt64((uint)result) <= m_request.WritableLimitBytes)
            return errno;

        Forward(caller, () => m_fdSeek!(descriptor, 0, WHENCE_SET, result));
        return WasmHostErrno.INVAL;
    }

    private int SetSize(Caller caller, int descriptor, long size)
    {
        if (m_files.IsWritable(descriptor) && (size < 0 || !Spend(size)))
            return WasmHostErrno.NOSPC;

        return Forward(caller, () => m_fdFilestatSetSize!(descriptor, size));
    }

    private int Allocate(Caller caller, int descriptor, long offset, long length)
    {
        if (m_files.IsWritable(descriptor) && (offset < 0 || length < 0 || offset > long.MaxValue - length || !Spend(offset + length)))
            return WasmHostErrno.NOSPC;

        return Forward(caller, () => m_fdAllocate!(descriptor, offset, length));
    }

    private int Stat(Caller caller, int descriptor, int record)
    {
        var result = Forward(caller, () => m_fdFilestatGet!(descriptor, record));

        var memory = caller.GetMemory(WasmHostShim.MEMORY);
        if (result == WasmHostErrno.SUCCESS && memory != null && WasmHostMemory.InRange(memory, record, WasmHostFiles.FILESTAT_SIZE))
            m_files.Neutralize(memory.GetSpan((uint)record, WasmHostFiles.FILESTAT_SIZE), descriptor);

        return result;
    }

    private int Open(Caller caller, int folder, int lookup, int path, int pathLength, int open, long rights, long inheriting, int flags, int opened)
    {
        var errno = Resolve(caller, folder, path, pathLength, out var memory, out var target);
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

        var result = Forward(caller, () => m_pathOpen!(folder, lookup, path, pathLength, open, rights, inheriting, flags, opened));
        if (result == WasmHostErrno.SUCCESS && WasmHostMemory.InRange(memory, opened, sizeof(int)))
            m_files.Opened(memory.ReadInt32((uint)opened), target, WasmHostFiles.IsFolder(target));

        return result;
    }

    private int StatPath(Caller caller, int folder, int lookup, int path, int pathLength, int record)
    {
        var errno = Resolve(caller, folder, path, pathLength, out var memory, out var target);
        if (errno != WasmHostErrno.SUCCESS)
            return errno;

        if (target.Preopen.Names is { } names && !names.IsFile(target.Relative) && !names.IsFolder(target.Relative))
            return WasmHostErrno.NOENT;

        var result = Forward(caller, () => m_pathFilestatGet!(folder, lookup, path, pathLength, record));
        if (result == WasmHostErrno.SUCCESS && WasmHostMemory.InRange(memory, record, WasmHostFiles.FILESTAT_SIZE))
            WasmHostFiles.Neutralize(memory.GetSpan((uint)record, WasmHostFiles.FILESTAT_SIZE), target);

        return result;
    }

    private int ReadLink(Caller caller, int folder, int path, int pathLength)
    {
        var errno = Resolve(caller, folder, path, pathLength, out _, out var target);
        if (errno != WasmHostErrno.SUCCESS)
            return errno;

        // A table holds files and folders, never a link.
        var names = target.Preopen.Names!;
        return names.IsFile(target.Relative) || names.IsFolder(target.Relative) ? WasmHostErrno.INVAL : WasmHostErrno.NOENT;
    }

    #endregion

    #region Tools

    private int Resolve(Caller caller, int folder, int path, int pathLength, out Memory memory, out WasmHostFiles.Target target)
    {
        target = default;
        memory = caller.GetMemory(WasmHostShim.MEMORY)!;

        long length = (uint)pathLength;
        if (memory == null || !WasmHostMemory.InRange(memory, path, length) || length > int.MaxValue)
            return WasmHostErrno.FAULT;

        Charge(caller, length);
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

    /// <summary>Takes a call's cost from the run's fuel, a unit a byte, and ends the run when it does not hold it.</summary>
    private void Charge(Caller caller, long bytes)
    {
        if (!m_request.ChargeHostCalls || bytes <= 0)
            return;

        var fuel = caller.Fuel;
        if ((ulong)bytes > fuel)
        {
            caller.Fuel = 0;
            throw Ended(WasmHostVerdict.OutOfFuel);
        }

        caller.Fuel = fuel - (ulong)bytes;
    }

    private RunEnded Ended(WasmHostVerdict verdict)
    {
        End = verdict;
        return new RunEnded(verdict);
    }

    /// <summary>
    /// Calls WASI's own definition through the shim. The shim's instructions run on fuel lent for
    /// the call and taken back after it, so the guest pays nothing for the way through.
    /// </summary>
    private int Forward(Caller caller, Func<int> call)
    {
        var fuel = caller.Fuel;
        caller.Fuel = fuel > ulong.MaxValue - SHIM_FUEL ? ulong.MaxValue : fuel + SHIM_FUEL;
        try
        {
            return Shim(caller) ? call() : WasmHostErrno.FAULT;
        }
        finally
        {
            caller.Fuel = fuel;
        }
    }

    private bool Shim(Caller caller)
    {
        if (m_shim != null)
            return true;

        // WASI reads and writes the memory of the module that calls it: a guest without one has
        // nothing a file call could touch.
        var memory = caller.GetMemory(WasmHostShim.MEMORY);
        if (memory == null)
            return false;

        m_wasi = new Linker(m_engine);
        m_wasi.DefineWasi();

        var shim = m_shimModule.Instantiate(m_wasi, caller.Store, memory);
        m_fdWrite = shim.GetFunction<int, int, int, int, int>(WasmHostShim.FD_WRITE);
        m_fdPwrite = shim.GetFunction<int, int, int, long, int, int>(WasmHostShim.FD_PWRITE);
        m_fdRead = shim.GetFunction<int, int, int, int, int>(WasmHostShim.FD_READ);
        m_fdPread = shim.GetFunction<int, int, int, long, int, int>(WasmHostShim.FD_PREAD);
        m_fdClose = shim.GetFunction<int, int>(WasmHostShim.FD_CLOSE);
        m_fdSeek = shim.GetFunction<int, long, int, int, int>(WasmHostShim.FD_SEEK);
        m_fdFilestatGet = shim.GetFunction<int, int, int>(WasmHostShim.FD_FILESTAT_GET);
        m_fdFilestatSetSize = shim.GetFunction<int, long, int>(WasmHostShim.FD_FILESTAT_SET_SIZE);
        m_fdAllocate = shim.GetFunction<int, long, long, int>(WasmHostShim.FD_ALLOCATE);
        m_pathOpen = shim.GetFunction<int, int, int, int, int, long, long, int, int, int>(WasmHostShim.PATH_OPEN);
        m_pathFilestatGet = shim.GetFunction<int, int, int, int, int, int>(WasmHostShim.PATH_FILESTAT_GET);
        m_shim = shim;
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
        if (exception is not RunEnded and not WasmtimeException)
            Fault ??= exception;

        return false;
    }

    #endregion

    #region IDisposable

    /// <summary>Closes the run's streams and the linker the shim was made with.</summary>
    public void Dispose()
    {
        m_stdout.Dispose();
        m_stderr.Dispose();
        m_wasi?.Dispose();
    }

    #endregion

    #region Properties

    /// <summary>The guest's stdout as kept.</summary>
    public WasmHostOutput Stdout => m_stdout;

    /// <summary>The guest's stderr as kept.</summary>
    public WasmHostOutput Stderr => m_stderr;

    /// <summary>How one of the host's calls ended the run (out of fuel, the output limit), or null when none did.</summary>
    public WasmHostVerdict? End { get; private set; }

    /// <summary>The host's own failure inside one of its calls, or null.</summary>
    public Exception? Fault { get; private set; }

    #endregion

    #region Nested Types

    /// <summary>The way out of the guest when one of the host's calls ends the run; never seen outside a run.</summary>
    private sealed class RunEnded(WasmHostVerdict verdict) : Exception($"the host ended the run: {verdict}");

    #endregion
}
