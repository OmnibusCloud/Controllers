using OutWit.Controller.Render.Dcc.Model;

namespace OutWit.Controller.Render.Dcc.Services;

/// <summary>
/// Public facade for validating neutral DCC scene payloads outside the host-only implementation details.
/// </summary>
public static class DccSceneValidationService
{
    #region Functions

    /// <summary>
    /// Validates the provided DCC scene payload against the currently supported contract.
    /// </summary>
    /// <param name="scene">The scene to validate.</param>
    public static void Validate(DccSceneData scene)
    {
        DccSceneContractValidator.Validate(scene);
    }

    #endregion
}
