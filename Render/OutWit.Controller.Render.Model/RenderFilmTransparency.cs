namespace OutWit.Controller.Render.Model;

/// <summary>
/// Whether the film/world background is rendered transparent (Blender's <c>render.film_transparent</c>).
/// </summary>
public enum RenderFilmTransparency
{
    /// <summary>
    /// Leave the scene's own film-transparent setting untouched (legacy behaviour).
    /// </summary>
    Default,

    /// <summary>
    /// Force an opaque background.
    /// </summary>
    Opaque,

    /// <summary>
    /// Force a transparent background (combine with <see cref="RenderColorMode.RGBA"/> for transparent output).
    /// </summary>
    Transparent
}
