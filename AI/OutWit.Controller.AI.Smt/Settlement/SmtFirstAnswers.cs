namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// The answers a problem is first ruled on (<see cref="SmtRulings.First"/>): the first fan-out's,
/// and for a problem the repair round answered, the first fan-out's with the repair round's.
/// </summary>
/// <param name="Answers">The answers: the first fan-out's, then the repair round's; empty when neither gave one.</param>
/// <param name="Round">The latest round with an answer among them: the round a dispute sends the problem on from.</param>
/// <param name="TwoMachines">
/// Whether two of the answers are known to be two machines': two bound answers of one fan-out,
/// returned for two of its tasks, which Grid keeps on different machines. Unbound answers may be
/// one machine's however many they are, and so may a lone answer of the first fan-out and a lone
/// answer of the repair round: Grid guarantees nothing across fan-outs.
/// </param>
/// <param name="Surplus">Whether a round that gave answers among them took more results under the problem's ticket than it asked for.</param>
internal sealed record SmtFirstAnswers(List<SmtCopy> Answers, SmtRound Round, bool TwoMachines, bool Surplus);
