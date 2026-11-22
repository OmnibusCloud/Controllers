using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Settlement;

/// <summary>
/// What two executions of one task must agree on: everything an honest node cannot vary. The
/// verdict, the exit code, stdout and stderr with their cut flags, and every case's outcome and
/// output. Fuel, memory and time are costs and are not compared. The outputs are compared by
/// digest (the first 128 bits of a SHA-256), so that a run of a million tasks is settled without
/// its outputs in memory.
/// </summary>
internal static class VerifyComparable
{
    #region Constants

    /// <summary>The names of the fields two executions can differ in, as the integrity document lists them.</summary>
    public const string VERDICT = "verdict";
    public const string EXIT = "exit";
    public const string STDOUT = "stdout";
    public const string STDERR = "stderr";
    public const string CASES = "cases";

    private static readonly UTF8Encoding UTF8 = new(encoderShouldEmitUTF8Identifier: false);

    #endregion

    #region Functions

    /// <summary>Makes the record of an execution from a node's result.</summary>
    /// <param name="result">The result.</param>
    /// <param name="spoolOffset">Where the whole result was kept, or -1.</param>
    /// <param name="spoolLength">Its length there.</param>
    /// <returns>The record.</returns>
    public static VerifyExecution Record(VerifyResultData result, long spoolOffset = -1, int spoolLength = 0)
    {
        return new VerifyExecution
        {
            Index = result.TaskIndex,
            Verdict = result.Verdict,
            ExitCode = result.ExitCode,
            Fault = result.Fault ?? "",
            Stdout = Digest(result.Stdout, result.StdoutTruncated),
            Stderr = Digest(result.Stderr, result.StderrTruncated),
            Cases = Digest(result.CaseResults),
            Fuel = result.FuelConsumed,
            PeakMemory = result.PeakMemoryBytes,
            WallMs = result.WallMs,
            SpoolOffset = spoolOffset,
            SpoolLength = spoolLength
        };
    }

    /// <summary>Whether two executions agree on everything that is compared.</summary>
    /// <param name="left">One execution.</param>
    /// <param name="right">Another execution of the same task.</param>
    /// <returns>True when they agree.</returns>
    public static bool Agree(VerifyExecution left, VerifyExecution right)
    {
        return left.Verdict == right.Verdict
               && left.ExitCode == right.ExitCode
               && left.Stdout == right.Stdout
               && left.Stderr == right.Stderr
               && left.Cases == right.Cases;
    }

    /// <summary>The fields two executions differ in.</summary>
    /// <param name="left">One execution.</param>
    /// <param name="right">Another execution of the same task.</param>
    /// <returns>The names of the fields, in the order the integrity document lists them; empty when the two agree.</returns>
    public static List<string> Differences(VerifyExecution left, VerifyExecution right)
    {
        var fields = new List<string>();
        if (left.Verdict != right.Verdict)
            fields.Add(VERDICT);
        if (left.ExitCode != right.ExitCode)
            fields.Add(EXIT);
        if (left.Stdout != right.Stdout)
            fields.Add(STDOUT);
        if (left.Stderr != right.Stderr)
            fields.Add(STDERR);
        if (left.Cases != right.Cases)
            fields.Add(CASES);

        return fields;
    }

    /// <summary>
    /// An order of the executions of one task that follows from what they hold, never from the
    /// order they arrived in: executions that agree stand together.
    /// </summary>
    /// <param name="executions">The executions of one task.</param>
    /// <returns>The executions, ordered.</returns>
    public static List<VerifyExecution> Ordered(IEnumerable<VerifyExecution> executions)
    {
        return executions
            .OrderBy(execution => execution.Verdict)
            .ThenBy(execution => execution.ExitCode)
            .ThenBy(execution => execution.Stdout)
            .ThenBy(execution => execution.Stderr)
            .ThenBy(execution => execution.Cases)
            .ThenBy(execution => execution.Fuel)
            .ThenBy(execution => execution.WallMs)
            .ToList();
    }

    private static UInt128 Digest(string? text, bool cut)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData([cut ? (byte)1 : (byte)0]);
        Append(hash, text);
        return Finish(hash);
    }

    private static UInt128 Digest(List<VerifyCaseResultData> cases)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> number = stackalloc byte[sizeof(int)];

        foreach (var item in cases)
        {
            BinaryPrimitives.WriteInt32LittleEndian(number, item.CaseIndex);
            hash.AppendData(number);
            hash.AppendData([item.Passed ? (byte)1 : (byte)0]);
            BinaryPrimitives.WriteInt32LittleEndian(number, (int)item.Verdict);
            hash.AppendData(number);
            BinaryPrimitives.WriteInt32LittleEndian(number, item.ExitCode);
            hash.AppendData(number);
            Append(hash, item.ActualStdout);
        }

        return Finish(hash);
    }

    /// <summary>A text into a hash, with its length before it, so that two texts never read as one.</summary>
    private static void Append(IncrementalHash hash, string? text)
    {
        var bytes = UTF8.GetBytes(text ?? "");
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static UInt128 Finish(IncrementalHash hash)
    {
        Span<byte> digest = stackalloc byte[32];
        hash.GetHashAndReset(digest);
        return BinaryPrimitives.ReadUInt128LittleEndian(digest);
    }

    #endregion
}
