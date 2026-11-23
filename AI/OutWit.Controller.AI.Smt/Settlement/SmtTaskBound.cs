using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>What a row may say of one problem: as the host's package of problems gave it.</summary>
/// <param name="Id">The problem's id.</param>
/// <param name="Hash">The hash of its canonical text.</param>
/// <param name="Query">Its query.</param>
/// <param name="MaxModels">The most models an enumeration of it may find; 0 for the other queries.</param>
internal readonly record struct SmtTaskBound(string Id, string Hash, SmtQuery Query, int MaxModels);
