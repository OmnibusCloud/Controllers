using OutWit.Controller.AI.Verify.Delivery;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// What a taskset may hold, beyond the limits of a single run: the bounds on what the server
/// reads from a lab. A taskset outside them is refused by line when it is split, and the
/// preflight says so beforehand. A node holds a package to the same bounds, whoever made it.
/// </summary>
public static class VerifyTasksetLimits
{
    #region Constants

    /// <summary>
    /// The most bytes one line of a taskset may have. A task's text is at most
    /// <see cref="VerifyPackagePlanner.MAX_PACKAGE_BYTES"/> characters, and a character is at
    /// most six bytes of JSON (a <c>\uXXXX</c> escape), so no task that fits is refused for
    /// its line; a longer line is refused without being read into memory.
    /// </summary>
    public const int MAX_LINE_BYTES = 64 * 1024 * 1024;

    /// <summary>
    /// The most tasks one taskset may have: what the package caps hold in the number of
    /// packages a large run is cut into (<see cref="VerifyPackagePlanner.TARGET_PACKAGES"/> of
    /// <see cref="VerifyPackagePlanner.MAX_PACKAGE_TASKS"/>). It bounds what the server keeps a
    /// task (a record, never its text) and the list of package references a job carries.
    /// </summary>
    public const int MAX_TASKS = VerifyPackagePlanner.TARGET_PACKAGES * VerifyPackagePlanner.MAX_PACKAGE_TASKS;

    /// <summary>The most source files one task may have.</summary>
    public const int MAX_SOURCES = 1024;

    /// <summary>The most arguments a task may have, and a case of a suite besides them.</summary>
    public const int MAX_ARGS = 1024;

    /// <summary>The most cases a suite may have: a task runs once a case, each run under the task's limits.</summary>
    public const int MAX_SUITE_CASES = 1024;

    /// <summary>The most messages of one kind a preflight returns; past it one line says how many more there are.</summary>
    public const int MAX_MESSAGES = 32;

    /// <summary>The most characters one message has.</summary>
    public const int MAX_MESSAGE_LENGTH = 240;

    /// <summary>The most runtime ids a preflight lists.</summary>
    public const int MAX_LISTED_RUNTIMES = 32;

    #endregion
}
