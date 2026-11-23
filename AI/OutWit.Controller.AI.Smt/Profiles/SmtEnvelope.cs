namespace OutWit.Controller.AI.Smt.Profiles;

/// <summary>
/// What one session runs in: the numbers a run's options resolve to under the profile
/// (<see cref="SmtProfile.Envelope"/>). Every one but the backstop is a count the outcome
/// depends on, the same on every machine.
/// </summary>
/// <param name="ResourceLimit">The solver's own resource limit for one check.</param>
/// <param name="Fuel">The fuel of the whole session, counted from the solver's first instruction.</param>
/// <param name="MemoryMegabytes">The solver's own memory ceiling, megabytes.</param>
/// <param name="HostMemoryBytes">The host's cap of the solver's linear memory, bytes.</param>
/// <param name="OutputLimitBytes">The most the session may write to its stdout; the write that passes it ends the session.</param>
/// <param name="BackstopMs">The wall time after which the machine, not the problem, ends the session.</param>
/// <param name="Seed">The seed of the solver's random choices.</param>
/// <param name="MaxModels">The most models of an enumeration whose problem names no number.</param>
internal sealed record SmtEnvelope(
    long ResourceLimit,
    long Fuel,
    int MemoryMegabytes,
    long HostMemoryBytes,
    int OutputLimitBytes,
    int BackstopMs,
    int Seed,
    int MaxModels);
