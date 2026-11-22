using System.Buffers.Binary;
using System.IO.Compression;
using MemoryPack;
using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Delivery;

/// <summary>
/// Writes a result package on a node as its batches finish: straight into a file of the
/// client's own folder, so that the results of a package are never all in memory at once. The
/// file is the package blob: the header, then a Brotli stream of frames, each a kind byte, a
/// length and a MemoryPack image. A batch is one head frame (its provenance and the ticket of
/// the task package it answers) followed by one frame a result.
/// </summary>
public sealed class VerifyResultPackageWriter : IDisposable
{
    #region Constants

    /// <summary>The frame that opens a batch: its diagnostics and ticket, no results.</summary>
    public const byte FRAME_BATCH = 1;

    /// <summary>The frame of one task's result.</summary>
    public const byte FRAME_RESULT = 2;

    /// <summary>The length of a frame's own head: the kind byte and the length of what follows.</summary>
    public const int FRAME_HEAD_LENGTH = 1 + sizeof(int);

    #endregion

    #region Fields

    private readonly FileStream m_file;
    private readonly BrotliStream m_brotli;
    private bool m_completed;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Starts a package in a file (made new).
    /// </summary>
    /// <param name="path">The file.</param>
    /// <exception cref="IOException">The file cannot be made.</exception>
    public VerifyResultPackageWriter(string path)
    {
        Path = path;
        m_file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        VerifyPackageCodec.WriteHeader(m_file, VerifyPackageCodec.KIND_RESULTS, VerifyPackageCodec.RESULTS_VERSION);
        m_brotli = new BrotliStream(m_file, new BrotliCompressionOptions { Quality = VerifyPackageCodec.COMPRESSION_QUALITY }, leaveOpen: true);
    }

    #endregion

    #region Functions

    /// <summary>Appends the results of one executed batch.</summary>
    /// <param name="batch">The batch: its results, its diagnostics and the ticket it answers.</param>
    public void Write(VerifyResultBatchData batch)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);

        var head = new VerifyResultBatchData { Diagnostics = batch.Diagnostics, Ticket = batch.Ticket };
        Frame(FRAME_BATCH, MemoryPackSerializer.Serialize(head));

        foreach (var result in batch.Results)
            Frame(FRAME_RESULT, MemoryPackSerializer.Serialize(result));

        Batches++;
        Results += batch.Results.Count;
    }

    /// <summary>Ends the stream and closes the file: the package is whole.</summary>
    public void Complete()
    {
        if (m_completed)
            return;

        m_completed = true;
        m_brotli.Dispose();
        m_file.Dispose();
    }

    private void Frame(byte kind, byte[] image)
    {
        Span<byte> head = stackalloc byte[FRAME_HEAD_LENGTH];
        head[0] = kind;
        BinaryPrimitives.WriteInt32LittleEndian(head[1..], image.Length);

        m_brotli.Write(head);
        m_brotli.Write(image);
    }

    #endregion

    #region IDisposable

    /// <summary>Closes the file; a package that was not completed is not one.</summary>
    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;
        Complete();
    }

    #endregion

    #region Properties

    /// <summary>The file the package is written to.</summary>
    public string Path { get; }

    /// <summary>The batches written.</summary>
    public int Batches { get; private set; }

    /// <summary>The results written.</summary>
    public int Results { get; private set; }

    #endregion
}
