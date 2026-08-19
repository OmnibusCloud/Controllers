namespace OutWit.Controller.Visualization.ParaView.Validation;

/// <summary>
/// One registered item of a state proxy collection (for example the views collection: id → registration name).
/// </summary>
/// <param name="Id">State-local proxy id.</param>
/// <param name="Name">Registration name.</param>
public sealed record ParaViewStateCollectionItem(string Id, string Name);
