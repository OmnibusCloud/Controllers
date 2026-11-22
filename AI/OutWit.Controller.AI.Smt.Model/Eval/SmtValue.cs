namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// A value a closed term evaluates to: a truth value, an integer, a rational, a bit-vector, a
/// string, a datatype value, an element of a sort the model populates, or an array. Every value
/// is of one definite sort and never changes once made.
/// </summary>
internal abstract class SmtValue
{
    #region Properties

    /// <summary>What the value is.</summary>
    public abstract SmtValueKind Kind { get; }

    /// <summary>How deep values nest in the value: 1 for a value that holds no other.</summary>
    public virtual int Depth => 1;

    /// <summary>
    /// What comparing the value with another costs at most, in steps: about its size in machine
    /// words. A value that shares parts counts them each time they are reached.
    /// </summary>
    public virtual long Weight => 1;

    /// <summary>
    /// What comparing the value with any other index of an array costs at most, in steps
    /// (<see cref="SmtValueComparer"/>): its weight, but for a rational, which is compared by
    /// multiplying across.
    /// </summary>
    public virtual long KeyCost => Weight;

    /// <summary>Whether the value is an array or holds one: such a value is no index of an array.</summary>
    public virtual bool HoldsArray => false;

    #endregion
}
