namespace OutWit.Controller.Visualization.ParaView.State;

/// <summary>
/// The state file is not an admissible ParaView state: malformed XML, a prohibited XML construct
/// (DTD, entities), or a structural limit exceeded. Always a permanent input failure.
/// </summary>
public sealed class ParaViewStateFormatException : Exception
{
    #region Constructors

    public ParaViewStateFormatException(string message)
        : base(message)
    {
    }

    public ParaViewStateFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    #endregion
}
