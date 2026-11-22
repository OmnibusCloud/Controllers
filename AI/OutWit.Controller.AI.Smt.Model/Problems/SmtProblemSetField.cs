namespace OutWit.Controller.AI.Smt.Model.Problems;

/// <summary>
/// The fields of a row of a problem set, as flags: which of them a row has given.
/// </summary>
[Flags]
internal enum SmtProblemSetField
{
    /// <summary>No field, or a name that is none of a row's.</summary>
    None = 0,

    /// <summary><c>id</c>.</summary>
    Id = 1,

    /// <summary><c>smt2</c>.</summary>
    Smt2 = 2,

    /// <summary><c>query</c>.</summary>
    Query = 4,

    /// <summary><c>values</c>.</summary>
    Values = 8,

    /// <summary><c>project</c>.</summary>
    Project = 16,

    /// <summary><c>max_models</c>.</summary>
    MaxModels = 32,

    /// <summary><c>goal</c>.</summary>
    Goal = 64,

    /// <summary><c>expected</c>.</summary>
    Expected = 128,

    /// <summary><c>tags</c>.</summary>
    Tags = 256
}
