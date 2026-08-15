using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using RightClickConvert.Infrastructure;

namespace RightClickConvert.Engines;

/// <summary>
/// Handles raster-to-raster conversions in-process. No LibreOffice, no temp directory,
/// no serialisation — routing these here is why a JPEG stays a JPEG instead of being
/// re-rendered onto a Draw page.
/// </summary>
public static class ImageEngine
{
    private const int Quality = 90;

    public static async Task<string> ConvertAsync(string input, Target target, string outputDirectory)
    {
        using var image = await Image.LoadAsync<Rgba32>(input);

        string final = Paths.Unique(outputDirectory, Path.GetFileNameWithoutExtension(input), target.Ext);

        switch (target.Ext)
        {
            case "png":
                await image.SaveAsPngAsync(final);
                break;

            case "webp":
                await image.SaveAsWebpAsync(final, new WebpEncoder { Quality = Quality });
                break;

            case "jpg":
                // JPEG has no alpha channel; without this, transparent areas encode as black.
                image.Mutate(x => x.BackgroundColor(Color.White));
                await image.SaveAsJpegAsync(final, new JpegEncoder { Quality = Quality });
                break;

            default:
                throw new NotSupportedException($"No image encoder for .{target.Ext}");
        }

        return final;
    }
}
