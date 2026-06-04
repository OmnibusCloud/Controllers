using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Utils;

/// <summary>
/// One rendered frame/tile produced by <see cref="BlenderRunner.RenderFrameBatchAsync"/>: the source
/// <see cref="RenderTaskData"/> paired with the local path of its rendered image.
/// </summary>
public sealed record RenderBatchFrameResult(RenderTaskData Task, string RenderedPath);
