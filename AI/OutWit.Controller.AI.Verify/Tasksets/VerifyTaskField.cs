namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// The fields a taskset line, a case of its suite, its limits and a checker spec may have: what
/// the schemas name, and nothing else. A set of them is what an object has given so far.
/// </summary>
[Flags]
internal enum VerifyTaskField
{
    None = 0,
    Index = 1 << 0,
    Runtime = 1 << 1,
    Entry = 1 << 2,
    Sources = 1 << 3,
    Args = 1 << 4,
    Stdin = 1 << 5,
    Seed = 1 << 6,
    Suite = 1 << 7,
    Limits = 1 << 8,
    ExpectedStdout = 1 << 9,
    ExpectedExit = 1 << 10,
    Fuel = 1 << 11,
    Memory = 1 << 12,
    WallMs = 1 << 13,
    StdoutBytes = 1 << 14,
    StderrBytes = 1 << 15
}
