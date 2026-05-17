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
        var weight = new double[pixelCount];

        foreach (var context in contexts)
        {
            var image = await runner.DecodeImageToRgbaAsync(context.LocalPath, cancellationToken);
            BlendTile(context.Result, image, outputWidth, outputHeight, red, green, blue, alpha, weight);
        }

        return new RenderRawImage
        {
            Width = outputWidth,
            Height = outputHeight,
            PixelBytes = BuildOutputPixels(pixelCount, red, green, blue, alpha, weight)
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
        double[] weight)
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
                var sourceAlpha = image.PixelBytes[sourceIndex + 3] / 255d;
                var blendedWeight = feather * sourceAlpha;
                if (blendedWeight <= 0)
                    continue;

                var canvasIndex = canvasY * outputWidth + canvasX;
                red[canvasIndex] += image.PixelBytes[sourceIndex] * blendedWeight;
                green[canvasIndex] += image.PixelBytes[sourceIndex + 1] * blendedWeight;
                blue[canvasIndex] += image.PixelBytes[sourceIndex + 2] * blendedWeight;
                alpha[canvasIndex] += 255d * blendedWeight;
                weight[canvasIndex] += blendedWeight;
            }
        }
    }

    private static byte[] BuildOutputPixels(int pixelCount, double[] red, double[] green, double[] blue, double[] alpha, double[] weight)
    {
        var pixels = new byte[pixelCount * 4];
        for (var index = 0; index < pixelCount; index++)
        {
            var outputIndex = index * 4;
            if (weight[index] <= 0)
            {
                pixels[outputIndex + 3] = 0;
                continue;
            }

            pixels[outputIndex] = ClampToByte(red[index] / weight[index]);
            pixels[outputIndex + 1] = ClampToByte(green[index] / weight[index]);
            pixels[outputIndex + 2] = ClampToByte(blue[index] / weight[index]);
            pixels[outputIndex + 3] = ClampToByte(alpha[index] / weight[index]);
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

    private static int GetRenderedOffsetX(RenderResultData result, int width)
    {
        return (int)Math.Round(result.EffectiveRenderMinX * width, MidpointRounding.AwayFromZero);
    }

    private static int GetRenderedOffsetY(RenderResultData result, int height)
    {
        var renderMaxY = (int)Math.Round(result.EffectiveRenderMaxY * height, MidpointRounding.AwayFromZero);
        return height - renderMaxY;
    }

    private static int GetTileOffsetX(RenderResultData result, int width)
    {
        return (int)Math.Round(result.TileMinX * width, MidpointRounding.AwayFromZero);
    }

    private static int GetTileOffsetY(RenderResultData result, int height)
    {
        var tileMaxY = (int)Math.Round(result.TileMaxY * height, MidpointRounding.AwayFromZero);
        return height - tileMaxY;
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
        return (int)Math.Round((result.TileMaxX - result.TileMinX) * width, MidpointRounding.AwayFromZero);
    }

    private static int GetTileCropHeight(RenderResultData result, int height)
    {
        return (int)Math.Round((result.TileMaxY - result.TileMinY) * height, MidpointRounding.AwayFromZero);
    }

    #endregion
}
