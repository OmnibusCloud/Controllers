using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Activities;

internal interface IRenderFrameActivity
{
    IWitParameter? Task { get; init; }
}
