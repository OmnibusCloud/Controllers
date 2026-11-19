using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Sandbox;
using OutWit.Controller.AI.Verify.Tasksets;

namespace OutWit.Controller.AI.Verify.Delivery;

/// <summary>
/// The two frames of a result package as a node of this controller writes them, walked before
/// they are decoded (<see cref="VerifyFrameCursor"/>): the head of a batch
/// (<see cref="VerifyResultBatchData"/> without its results) and one task's result
/// (<see cref="VerifyResultData"/>). A frame passes when every object in it has the members this
/// version writes, every text and list lies inside the frame and inside its bound, and the frame
/// ends where its last member does. Whether what it says is something a node may say is asked
/// afterwards, of the decoded result.
/// <para>
/// The member counts and their order are the wire layout of the model, which is append-only: a
/// member added to one of the four types is added here in the same change, and a test holds the
/// walk to what the serializer writes.
/// </para>
/// </summary>
internal static class VerifyResultFrames
{
    #region Constants

    /// <summary>The members of <see cref="VerifyResultBatchData"/> on the wire.</summary>
    public const int BATCH_MEMBERS = 3;

    /// <summary>The members of <see cref="VerifyBatchDiagnosticsData"/> on the wire.</summary>
    public const int DIAGNOSTICS_MEMBERS = 11;

    /// <summary>The members of <see cref="VerifyResultData"/> on the wire.</summary>
    public const int RESULT_MEMBERS = 12;

    /// <summary>The members of <see cref="VerifyCaseResultData"/> on the wire.</summary>
    public const int CASE_MEMBERS = 5;

    /// <summary>
    /// The most characters of a text that is a name or a code: a ticket, a fault, what a node
    /// says of its platform, engine, controller and runtime. The longest an honest one has is
    /// a SHA-256 in hex.
    /// </summary>
    public const int MAX_NAME_LENGTH = 256;

    /// <summary>The most case records a result may hold: the cases a suite may have.</summary>
    public const int MAX_CASES = VerifyTasksetLimits.MAX_SUITE_CASES;

    private const int GUID_BYTES = 16;

    #endregion

    #region Functions

    /// <summary>Walks the frame that opens a batch.</summary>
    /// <param name="frame">The frame's bytes.</param>
    /// <exception cref="InvalidDataException">The frame is not the head of a batch as this controller writes one.</exception>
    public static void CheckBatch(ReadOnlySpan<byte> frame)
    {
        var cursor = new VerifyFrameCursor(frame);
        if (!cursor.Object(BATCH_MEMBERS))
            throw VerifyFrameCursor.Unreadable();

        // The results of a batch travel as frames of their own: the head carries none.
        cursor.Count(0);

        if (cursor.Object(DIAGNOSTICS_MEMBERS))
        {
            cursor.Skip(GUID_BYTES);
            cursor.Text(MAX_NAME_LENGTH);
            cursor.Text(MAX_NAME_LENGTH);
            cursor.Text(MAX_NAME_LENGTH);
            cursor.Text(MAX_NAME_LENGTH);
            cursor.Text(MAX_NAME_LENGTH);
            cursor.Text(MAX_NAME_LENGTH);
            cursor.Int32();
            cursor.Int32();
            cursor.Int32();
            cursor.Text(MAX_NAME_LENGTH);
        }

        cursor.Text(MAX_NAME_LENGTH);
        cursor.End();
    }

    /// <summary>Walks the frame of one task's result.</summary>
    /// <param name="frame">The frame's bytes.</param>
    /// <exception cref="InvalidDataException">The frame is not a result as this controller writes one, or holds more than a result may.</exception>
    public static void CheckResult(ReadOnlySpan<byte> frame)
    {
        var cursor = new VerifyFrameCursor(frame);
        if (!cursor.Object(RESULT_MEMBERS))
            throw VerifyFrameCursor.Unreadable();

        cursor.Int32();
        cursor.Int32();
        cursor.Int32();
        cursor.Text(VerifyLimitCeilings.MAX_STDOUT_LIMIT_BYTES);
        cursor.Flag();
        cursor.Text(VerifyLimitCeilings.MAX_STDERR_LIMIT_BYTES);
        cursor.Flag();

        var cases = cursor.Count(MAX_CASES);
        for (var index = 0; index < cases; index++)
        {
            if (!cursor.Object(CASE_MEMBERS))
                continue;

            cursor.Int32();
            cursor.Flag();
            cursor.Int32();
            cursor.Int32();
            cursor.Text(VerifyLimitCeilings.MAX_STDOUT_LIMIT_BYTES);
        }

        cursor.Int64();
        cursor.Int64();
        cursor.Int32();
        cursor.Text(MAX_NAME_LENGTH);
        cursor.End();
    }

    #endregion
}
