namespace OutWit.Controller.AI.Verify.Tests.Mock;

/// <summary>A host with no service to give: an adapter built on it falls back to what it does without one.</summary>
internal sealed class VerifyNoServices : IServiceProvider
{
    #region IServiceProvider

    /// <inheritdoc />
    public object? GetService(Type serviceType)
    {
        return null;
    }

    #endregion
}
