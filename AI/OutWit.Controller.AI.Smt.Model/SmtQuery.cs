using System.Text.Json.Serialization;

namespace OutWit.Controller.AI.Smt.Model;

/// <summary>
/// What is asked of a problem. The values are the names a problem set and a run's options use,
/// in lower case: <c>check</c>, <c>enumerate</c>, <c>prove</c>, and the same words name them in a
/// document of the options (<c>smt.options@1</c>).
/// </summary>
public enum SmtQuery
{
    /// <summary>Is the problem satisfiable, and when it is, by which model or which values.</summary>
    [JsonStringEnumMemberName("check")]
    Check = 0,

    /// <summary>The models of the problem, told apart by the values of the constants it is projected on, up to a number.</summary>
    [JsonStringEnumMemberName("enumerate")]
    Enumerate = 1,

    /// <summary>Does a goal follow from the problem.</summary>
    [JsonStringEnumMemberName("prove")]
    Prove = 2
}
