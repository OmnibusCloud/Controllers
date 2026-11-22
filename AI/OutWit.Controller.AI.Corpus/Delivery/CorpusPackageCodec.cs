using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using MemoryPack;
using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Delivery;

/// <summary>
/// The format of a package blob: an eight-byte header (magic, kind, version) followed by a Brotli
/// stream of frames, each a kind byte, a length and a MemoryPack image. Tasks travel to a node and
/// results travel back in this form, as blobs, so the messages that assign work and report it
/// carry references only and nothing in them grows with the manifest. The format is the
/// controller's own: both ends run the same module version.
/// <para>
/// A task package holds one frame of what its tasks share (the pipeline, the origins, the byte
/// cap, the controller version) and one frame per shard (index, source, ticket, declared size).
/// A result package holds one frame per result. Nothing is read without a bound: every frame is
/// held to the most its kind may be before a byte of it is read, a package to the most frames its
/// reader expects, and a buffer grows with the bytes that arrive, never by a length a frame's
/// head states.
/// </para>
/// </summary>
public static class CorpusPackageCodec
{
    #region Constants

    /// <summary>File name of a task package blob.</summary>
    public const string TASKS_FILE_NAME = "corpus-tasks.wcp";

    /// <summary>File name of a result package blob.</summary>
    public const string RESULTS_FILE_NAME = "corpus-results.wcp";

    /// <summary>The length of a package's header.</summary>
    public const int HEADER_LENGTH = 8;

    /// <summary>The kind byte of a task package.</summary>
    public const byte KIND_TASKS = 1;

    /// <summary>The kind byte of a result package.</summary>
    public const byte KIND_RESULTS = 2;

    /// <summary>The version of both package formats.</summary>
    public const byte VERSION = 1;

    /// <summary>The frame of what a task package's tasks share.</summary>
    public const byte FRAME_JOB = 1;

    /// <summary>The frame of one task.</summary>
    public const byte FRAME_TASK = 2;

    /// <summary>The frame of one result.</summary>
    public const byte FRAME_RESULT = 3;

    /// <summary>The length of a frame's head: its kind and the length of its image.</summary>
    public const int FRAME_HEAD_LENGTH = 5;

    /// <summary>The most bytes the shared frame of a task package may hold: a pipeline with its target languages and thresholds, and the origins.</summary>
    public const int MAX_JOB_FRAME_BYTES = 256 * 1024;

    /// <summary>The most bytes a task's frame may hold: a URL of 4,096 characters at three bytes each, a ticket, a hash.</summary>
    public const int MAX_TASK_FRAME_BYTES = 16 * 1024;

    /// <summary>
    /// The most bytes a result's frame may hold: the statistics of a shard with every drop rule,
    /// every language a pack may carry and every key of the unsupported breakdown (about 40 KiB),
    /// with room to spare.
    /// </summary>
    public const int MAX_RESULT_FRAME_BYTES = 128 * 1024;

    /// <summary>Brotli quality 4: URLs that share their prefix and statistics that share their names shrink several times at hundreds of megabytes per second.</summary>
    public const int COMPRESSION_QUALITY = 4;

    private const int FIRST_FRAME_BUFFER_BYTES = 4 * 1024;

    private static readonly byte[] MAGIC = "WCPK"u8.ToArray();

    #endregion

    #region Tasks

    /// <summary>Writes a task package.</summary>
    /// <param name="stream">The stream to write to, at the package's start.</param>
    /// <param name="shared">What every task of the package shares: the pipeline, the allowed origins, the byte cap and the required controller version are read from it.</param>
    /// <param name="tasks">The tasks, in ascending shard order; only what is a task's own is written of each (index, source, ticket, declared size).</param>
    /// <returns>The tasks written.</returns>
    public static int WriteTasks(Stream stream, CorpusTaskData shared, IEnumerable<CorpusTaskData> tasks)
    {
        WriteHeader(stream, KIND_TASKS);

        var count = 0;
        using (var brotli = new BrotliStream(stream, new BrotliCompressionOptions { Quality = COMPRESSION_QUALITY }, leaveOpen: true))
        {
            WriteFrame(brotli, FRAME_JOB, MemoryPackSerializer.Serialize(new CorpusTaskData
            {
                TaskIndex = -1,
                Pipeline = shared.Pipeline,
                AllowedOrigins = shared.AllowedOrigins,
                MaxShardBytes = shared.MaxShardBytes,
                RequiredControllerVersion = shared.RequiredControllerVersion
            }), MAX_JOB_FRAME_BYTES);

            foreach (var task in tasks)
            {
                WriteFrame(brotli, FRAME_TASK, MemoryPackSerializer.Serialize(new CorpusTaskData
                {
                    TaskIndex = task.TaskIndex,
                    Source = task.Source,
                    Ticket = task.Ticket,
                    DeclaredBytes = task.DeclaredBytes
                }), MAX_TASK_FRAME_BYTES);
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Reads a task package: every task complete, with the pipeline, the origins, the cap and the
    /// version its package shares (the pipeline and the origins are one object for all of them:
    /// they are read, never changed).
    /// </summary>
    /// <param name="path">The blob's local file.</param>
    /// <param name="maxTasks">The most tasks the package may hold.</param>
    /// <returns>The tasks, in the order written.</returns>
    /// <exception cref="InvalidDataException">The file is not a task package this module reads, is damaged, or holds more than a task package may.</exception>
    public static List<CorpusTaskData> ReadTasks(string path, int maxTasks)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        ReadHeader(file, KIND_TASKS);

        using var brotli = new BrotliStream(file, CompressionMode.Decompress);
        var head = new byte[FRAME_HEAD_LENGTH];

        if (!ReadHead(brotli, head, out var kind, out var length) || kind != FRAME_JOB || length > MAX_JOB_FRAME_BYTES)
            throw new InvalidDataException("The task package does not start with what its tasks share.");

        var shared = Deserialize<CorpusTaskData>(ReadFrame(brotli, length))
                     ?? throw new InvalidDataException("The task package's shared frame cannot be read.");
        shared.AllowedOrigins ??= [];

        var tasks = new List<CorpusTaskData>();
        while (ReadHead(brotli, head, out kind, out length))
        {
            if (kind != FRAME_TASK || length > MAX_TASK_FRAME_BYTES)
                throw new InvalidDataException("The task package holds a frame that is no task.");

            if (tasks.Count >= maxTasks)
                throw new InvalidDataException("The task package holds more tasks than a package may.");

            var task = Deserialize<CorpusTaskData>(ReadFrame(brotli, length))
                       ?? throw new InvalidDataException("A task of the task package cannot be read.");

            task.Pipeline = shared.Pipeline;
            task.AllowedOrigins = shared.AllowedOrigins;
            task.MaxShardBytes = shared.MaxShardBytes;
            task.RequiredControllerVersion = shared.RequiredControllerVersion ?? "";
            tasks.Add(task);
        }

        return tasks;
    }

    #endregion

    #region Results

    /// <summary>Writes a result package.</summary>
    /// <param name="stream">The stream to write to, at the package's start.</param>
    /// <param name="results">The results, one per shard of the task package.</param>
    public static void WriteResults(Stream stream, IEnumerable<CorpusResultData> results)
    {
        WriteHeader(stream, KIND_RESULTS);

        using var brotli = new BrotliStream(stream, new BrotliCompressionOptions { Quality = COMPRESSION_QUALITY }, leaveOpen: true);
        foreach (var result in results)
            WriteFrame(brotli, FRAME_RESULT, MemoryPackSerializer.Serialize(result), MAX_RESULT_FRAME_BYTES);
    }

    /// <summary>
    /// Reads a result package on the server, one result at a time. A node wrote the package, so
    /// nothing in it is believed: a frame larger than a result may be, a frame of another kind, or
    /// more frames than the task package had shards ends the reading
    /// (<see cref="InvalidDataException"/>); what was read before it stands. A frame whose image
    /// is no result is given as null and the reading goes on. An image is walked before it is
    /// decoded (<see cref="CorpusResultImage"/>): the decoder is handed only one in which every
    /// length and every count is inside the image and inside the bound of its kind, and whatever
    /// it then throws is caught here.
    /// </summary>
    /// <param name="path">The blob's local file.</param>
    /// <param name="maxResults">The most results the package may hold: the shards of the task package it answers.</param>
    /// <returns>The results in the order written, each read when asked for; null for a frame that is no result.</returns>
    /// <exception cref="InvalidDataException">The file is not a result package this module reads, is damaged, or holds more than a result package may. Thrown while the results are read.</exception>
    public static IEnumerable<CorpusResultData?> ReadResults(string path, int maxResults)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        ReadHeader(file, KIND_RESULTS);

        using var brotli = new BrotliStream(file, CompressionMode.Decompress);
        var head = new byte[FRAME_HEAD_LENGTH];
        var results = 0;

        while (ReadHead(brotli, head, out var kind, out var length))
        {
            if (kind != FRAME_RESULT || length > MAX_RESULT_FRAME_BYTES)
                throw new InvalidDataException("The result package holds a frame that is no result.");

            if (++results > maxResults)
                throw new InvalidDataException("The result package holds more results than its task package had shards.");

            yield return Result(ReadFrame(brotli, length));
        }
    }

    /// <summary>The most bytes the blob of a result package may be: a frame of the largest size for every shard. Brotli does not make a stream longer than this by more than a few bytes in a thousand, so a larger blob is no answer of an honest node.</summary>
    /// <param name="shards">The shards of the task package it answers.</param>
    /// <returns>The bytes.</returns>
    public static long MaxResultsBlobBytes(int shards)
    {
        return HEADER_LENGTH + (long)Math.Max(1, shards) * (FRAME_HEAD_LENGTH + MAX_RESULT_FRAME_BYTES) + 1024;
    }

    #endregion

    #region Tools

    /// <summary>The identity of a package blob in a file: SHA-256 of its bytes, lower-case hex.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The hash.</returns>
    public static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static void WriteHeader(Stream stream, byte kind)
    {
        stream.Write(MAGIC);
        stream.WriteByte(kind);
        stream.WriteByte(VERSION);
        stream.WriteByte(0);
        stream.WriteByte(0);
    }

    private static void ReadHeader(Stream stream, byte kind)
    {
        Span<byte> header = stackalloc byte[HEADER_LENGTH];
        if (stream.ReadAtLeast(header, HEADER_LENGTH, throwOnEndOfStream: false) < HEADER_LENGTH || !header[..MAGIC.Length].SequenceEqual(MAGIC))
            throw new InvalidDataException("The blob is not a Corpus package.");

        if (header[MAGIC.Length] != kind)
            throw new InvalidDataException("The package is of another kind.");

        if (header[MAGIC.Length + 1] != VERSION)
            throw new InvalidDataException("The package is of a version this module does not read.");
    }

    private static void WriteFrame(Stream stream, byte kind, byte[] image, int maxBytes)
    {
        if (image.Length > maxBytes)
            throw new InvalidDataException("A frame is larger than a frame of its kind may be.");

        Span<byte> head = stackalloc byte[FRAME_HEAD_LENGTH];
        head[0] = kind;
        BinaryPrimitives.WriteInt32LittleEndian(head[1..], image.Length);
        stream.Write(head);
        stream.Write(image);
    }

    /// <summary>Reads a frame's head; false at the end of the stream.</summary>
    private static bool ReadHead(Stream stream, byte[] head, out byte kind, out int length)
    {
        kind = 0;
        length = 0;

        var got = ReadSome(stream, head);
        if (got == 0)
            return false;

        if (got < head.Length)
            throw new InvalidDataException("The package ends inside a frame.");

        kind = head[0];
        length = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(1));
        if (length < 0)
            throw new InvalidDataException("A frame of the package states no length.");

        return true;
    }

    /// <summary>
    /// Reads a frame of a stated length. The buffer grows with the bytes that arrive, not by the
    /// length the frame's head states: a head that claims a large frame the stream does not hold
    /// costs nothing.
    /// </summary>
    private static byte[] ReadFrame(Stream stream, int length)
    {
        var buffer = new byte[Math.Min(length, FIRST_FRAME_BUFFER_BYTES)];
        var filled = 0;

        while (filled < length)
        {
            if (filled == buffer.Length)
                Array.Resize(ref buffer, (int)Math.Min(length, buffer.Length * 2L));

            var read = ReadSome(stream, buffer.AsSpan(filled, buffer.Length - filled), atLeast: 1);
            if (read == 0)
                throw new InvalidDataException("The package ends inside a frame.");

            filled += read;
        }

        return buffer;
    }

    private static int ReadSome(Stream stream, byte[] buffer)
    {
        return ReadSome(stream, buffer, buffer.Length);
    }

    private static int ReadSome(Stream stream, Span<byte> buffer, int atLeast)
    {
        try
        {
            return stream.ReadAtLeast(buffer, atLeast, throwOnEndOfStream: false);
        }
        catch (InvalidOperationException)
        {
            // Brotli's word for a stream that is not one.
            throw new InvalidDataException("The package's stream is damaged.");
        }
        catch (IOException exception) when (exception is not FileNotFoundException and not DirectoryNotFoundException)
        {
            throw new InvalidDataException("The package's stream is damaged.");
        }
    }

    /// <summary>
    /// The result an image holds, or null. A node chose the bytes, so every length the decoder
    /// would believe is checked first: an image the walk refuses never reaches the decoder.
    /// </summary>
    private static CorpusResultData? Result(byte[] image)
    {
        return CorpusResultImage.IsSound(image) ? Deserialize<CorpusResultData>(image) : null;
    }

    private static T? Deserialize<T>(byte[] image) where T : class
    {
        try
        {
            return MemoryPackSerializer.Deserialize<T>(image);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Whatever the decoder says of bytes a node chose: they are no image of the type.
            return null;
        }
    }

    #endregion
}
