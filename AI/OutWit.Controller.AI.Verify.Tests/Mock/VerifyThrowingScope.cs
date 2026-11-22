namespace OutWit.Controller.AI.Verify.Tests.Mock;

/// <summary>A logger's scope that throws when it ends.</summary>
internal sealed class VerifyThrowingScope : IDisposable
{
    #region IDisposable

    /// <inheritdoc />
    public void Dispose()
    {
        throw new ObjectDisposedException("scope");
    }

    #endregion
}
