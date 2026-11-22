namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// The caller's side of a conversation with a guest over its stdin and stdout
/// (<see cref="WasmHostRunRequest.Conversation"/>): the guest reads a command, answers it on
/// stdout and reads the next, and the caller decides each command having seen the answers so far.
/// There is no thread and no pipe in it: the caller is asked inside the guest's own read, on the
/// thread the guest runs on, so a run stays one sequence of steps, and the same conversation
/// gives the same run on every node, fuel included.
/// <para>
/// The host shows what the guest has written, not what the guest still holds in a buffer of its
/// own: a program that answers before it reads the next command has written its answer by then
/// (a C++ input stream tied to its output flushes first), and one that does not is seen a turn
/// late.
/// </para>
/// </summary>
internal interface IWasmHostConversation
{
    /// <summary>
    /// Gives the guest's next input. Asked when the guest reads descriptor 0 and has consumed
    /// everything given so far: the first time at its first read. The guest waits for the answer,
    /// and the time the answer takes is inside the run's wall time.
    /// </summary>
    /// <param name="output">
    /// What the guest wrote to stdout since the previous call, as the host kept it: nothing past
    /// the cap of the run's stdout. The span is valid during the call only.
    /// </param>
    /// <returns>
    /// The next bytes of the guest's stdin: the host hands them out over as many reads as the
    /// guest takes them in, and asks again only when the last one is taken (the memory must stay
    /// as it is until then). Empty for the end of input: that read and every later one of
    /// descriptor 0 give zero bytes, and the conversation is not asked again.
    /// </returns>
    /// <exception cref="Exception">
    /// Whatever the caller throws is a fault of the host's side: the run ends and
    /// <see cref="WasmHost.Run"/> throws for it, as for a failure of one of the host's own calls.
    /// It is never a verdict about the guest.
    /// </exception>
    ReadOnlyMemory<byte> Next(ReadOnlySpan<byte> output);
}
