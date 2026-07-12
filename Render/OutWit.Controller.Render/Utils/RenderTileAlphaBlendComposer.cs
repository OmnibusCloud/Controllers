using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Utils;

internal static class RenderTileAlphaBlendComposer
{
    #region Functions

    public static async Task<RenderRawImage> ComposeAsync(
        IReadOnlyList<RenderTileValidationContext> contexts,
        int outputWidth,
        int outputHeight,
        FfmpegRunner runner,
        CancellationToken cancellationToken)
    {
        var pixelCount = outputWidth * outputHeight;
        var red = new double[pixelCount];
        var green = new double[pixelCount];
        var blue = new double[pixelCount];
        var alpha = new double[pixelCount];
        var colorWeight = new double[pixelCount];
        var featherWeight = new double[pixelCount];

        foreach (var context in contexts)
        {
            var image = await runner.DecodeImageToRgbaAsync(context.LocalPath, cancellationToken);
            BlendTile(context.Result, image, outputWidth, outputHeight, red, green, blue, alpha, colorWeight, featherWeight);
        }

        return new RenderRawImage
        {
            Width = outputWidth,
            Height = outputHeight,
            PixelBytes = BuildOutputPixels(pixelCount, red, green, blue, alpha, colorWeight, featherWeight)
        };
    }

    private static void BlendTile(
        RenderResultData result,
        RenderRawImage image,
        int outputWidth,
        int outputHeight,
        double[] red,
        double[] green,
        double[] blue,
        double[] alpha,
        double[] colorWeight,
        double[] featherWeight)
    {
        var renderedOffsetX = GetRenderedOffsetX(result, outputWidth);
        var renderedOffsetY = GetRenderedOffsetY(result, outputHeight);
        var cropX = GetTileCropX(result, outputWidth);
        var cropY = GetTileCropY(result, outputHeight);
        var cropWidth = GetTileCropWidth(result, outputWidth);
        var cropHeight = GetTileCropHeight(result, outputHeight);
        var rightOverlap = image.Width - cropX - cropWidth;
        var bottomOverlap = image.Height - cropY - cropHeight;

        for (var y = 0; y < image.Height; y++)
        {
            var canvasY = renderedOffsetY + y;
            if (canvasY < 0 || canvasY >= outputHeight)
                continue;

            var weightY = GetAxisWeight(y, image.Height, cropY, bottomOverlap);
            for (var x = 0; x < image.Width; x++)
            {
                var canvasX = renderedOffsetX + x;
                if (canvasX < 0 || canvasX >= outputWidth)
                    continue;

                var weightX = GetAxisWeight(x, image.Width, cropX, rightOverlap);
                var feather = weightX * weightY;
                if (feather <= 0)
                    continue;

                var sourceIndex = (y * image.Width + x) * 4;
                var sourceAlphaByte = image.PixelBytes[sourceIndex + 3];
                var canvasIndex = canvasY * outputWidth + canvasX;

                // Output alpha is the feather-weighted average of the SOURCE alpha (accumulated for
                // every covered pixel, including fully-transparent ones), so an anti-aliased or
                // semi-transparent edge keeps its partial alpha instead of collapsing to opaque. RGB
                // is averaged by feather×alpha (an "over" composite weighted by coverage), so fully
                // transparent contributors add no colour.
                alpha[canvasIndex] += sourceAlphaByte * feather;
                featherWeight[canvasIndex] += feather;

                var blendedWeight = feather * (sourceAlphaByte / 255d);
                if (blendedWeight <= 0)
                    continue;

                red[canvasIndex] += image.PixelBytes[sourceIndex] * blendedWeight;
                green[canvasIndex] += image.PixelBytes[sourceIndex + 1] * blendedWeight;
                blue[canvasIndex] += image.PixelBytes[sourceIndex + 2] * blendedWeight;
                colorWeight[canvasIndex] += blendedWeight;
            }
        }
    }

    private static byte[] BuildOutputPixels(int pixelCount, double[] red, double[] green, double[] blue, double[] alpha, double[] colorWeight, double[] featherWeight)
    {
        var pixels = new byte[pixelCount * 4];
        for (var index = 0; index < pixelCount; index++)
        {
            var outputIndex = index * 4;
            if (featherWeight[index] <= 0)
            {
                // No tile covered this pixel — fully transparent.
                pixels[outputIndex + 3] = 0;
                continue;
            }

            pixels[outputIndex + 3] = ClampToByte(alpha[index] / featherWeight[index]);

            if (colorWeight[index] <= 0)
                continue; // covered only by fully-transparent pixels — alpha ~0, colour stays black.

            pixels[outputIndex] = ClampToByte(red[index] / colorWeight[index]);
            pixels[outputIndex + 1] = ClampToByte(green[index] / colorWeight[index]);
            pixels[outputIndex + 2] = ClampToByte(blue[index] / colorWeight[index]);
        }

        return pixels;
    }

    private static byte ClampToByte(double value)
    {
        return (byte)Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
    }

    private static double GetAxisWeight(int position, int length, int leadingOverlap, int trailingOverlap)
    {
        double result = 1d;

        if (leadingOverlap > 0 && position < leadingOverlap)
            result = Math.Min(result, (position + 1d) / (leadingOverlap + 1d));

        if (trailingOverlap > 0 && position >= length - trailingOverlap)
        {
            var distanceToEdge = length - position;
            result = Math.Min(result, distanceToEdge / (trailingOverlap + 1d));
        }

        return result;
    }

    // Shared boundary-first geometry (RenderTileGeometry) — identical to the stitch placement, so
    // the feather weights line up with the exact same pixel grid the CenterPriorityCrop path uses.
    private static int GetRenderedOffsetX(RenderResultData result, int width)
    {
        return RenderTileGeometry.BoundaryPixel(result.EffectiveRenderMinX, width);
    }

    private static int GetRenderedOffsetY(RenderResultData result, int height)
    {
        return RenderTileGeometry.TopOffset(result.EffectiveRenderMaxY, height);
    }

    private static int GetTileOffsetX(RenderResultData result, int width)
    {
        return RenderTileGeometry.BoundaryPixel(result.TileMinX, width);
    }

    private static int GetTileOffsetY(RenderResultData result, int height)
    {
        return RenderTileGeometry.TopOffset(result.TileMaxY, height);
    }

    private static int GetTileCropX(RenderResultData result, int width)
    {
        return GetTileOffsetX(result, width) - GetRenderedOffsetX(result, width);
    }

    private static int GetTileCropY(RenderResultData result, int height)
    {
        return GetTileOffsetY(result, height) - GetRenderedOffsetY(result, height);
    }

    private static int GetTileCropWidth(RenderResultData result, int width)
    {
        return RenderTileGeometry.Span(result.TileMinX, result.TileMaxX, width);
    }

    private static int GetTileCropHeight(RenderResultData result, int height)
    {
        return RenderTileGeometry.Span(result.TileMinY, result.TileMaxY, height);
    }

    #endregion
}
