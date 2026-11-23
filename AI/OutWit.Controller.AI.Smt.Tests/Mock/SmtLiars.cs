namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>Which of the rig's nodes lie.</summary>
internal enum SmtLiars
{
    /// <summary>The first node: copies on different machines meet honest ones.</summary>
    First = 0,

    /// <summary>Every node, the same way: the machines agree on what they say.</summary>
    All = 1
}
