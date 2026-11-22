using System.Diagnostics;
using System.Globalization;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// The memory of the test process at one moment, as the system reports it: what is the
/// process's own, what it really holds, and how much address space it has.
/// </summary>
internal sealed class SmtMemoryReading
{
    #region Constants

    private const double MEGABYTE = 1024 * 1024;

    #endregion

    #region Constructors

    private SmtMemoryReading(long privateBytes, long workingSet, long virtualBytes)
    {
        Private = privateBytes;
        WorkingSet = workingSet;
        Virtual = virtualBytes;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Reads the memory of this process now.
    /// </summary>
    /// <returns>The reading.</returns>
    public static SmtMemoryReading Now()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return new SmtMemoryReading(process.PrivateMemorySize64, process.WorkingSet64, process.VirtualMemorySize64);
    }

    /// <summary>
    /// The change since an earlier reading.
    /// </summary>
    /// <param name="earlier">The earlier reading.</param>
    /// <returns>The differences, in megabytes.</returns>
    public string Since(SmtMemoryReading earlier)
    {
        return $"private {Signed(Private - earlier.Private)}, working set {Signed(WorkingSet - earlier.WorkingSet)}, virtual {Signed(Virtual - earlier.Virtual)}";
    }

    /// <summary>
    /// The reading in megabytes.
    /// </summary>
    /// <returns>The three numbers.</returns>
    public override string ToString()
    {
        return $"private {Megabytes(Private)} MB, working set {Megabytes(WorkingSet)} MB, virtual {Megabytes(Virtual)} MB";
    }

    /// <summary>
    /// Bytes as megabytes with one decimal.
    /// </summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The number, without a unit.</returns>
    public static string Megabytes(long bytes)
    {
        return (bytes / MEGABYTE).ToString("F1", CultureInfo.InvariantCulture);
    }

    private static string Signed(long bytes)
    {
        return (bytes >= 0 ? "+" : "-") + Megabytes(Math.Abs(bytes)) + " MB";
    }

    #endregion

    #region Properties

    /// <summary>Bytes that are the process's own: committed, on Windows.</summary>
    public long Private { get; }

    /// <summary>Bytes of memory the process really holds.</summary>
    public long WorkingSet { get; }

    /// <summary>Bytes of address space, reserved or used.</summary>
    public long Virtual { get; }

    /// <summary>
    /// What the system charges the process for: its private bytes where the system keeps that
    /// count (Windows), the pages it really holds elsewhere.
    /// </summary>
    public long Taken => OperatingSystem.IsWindows() ? Private : WorkingSet;

    #endregion
}
