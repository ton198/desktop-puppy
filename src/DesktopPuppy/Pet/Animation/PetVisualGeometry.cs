using System.Resources;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopPuppy.Pet.Animation;

public static class PetVisualGeometry
{
    public const double ImageSize = 132;
    private static readonly Lazy<IReadOnlyDictionary<string, BitmapSource>> Frames = new(LoadFrames);
    private static readonly Lazy<Rect> Bounds = new(() =>
    {
        var bounds = Rect.Empty;
        foreach (var frame in NormalFrames.Values) bounds.Union(GetInkBounds(frame));
        return bounds;
    });

    public static IReadOnlyDictionary<string, BitmapSource> NormalFrames => Frames.Value;
    public static Rect VisibleBounds => Bounds.Value;

    public static Rect GetInkBounds(BitmapSource source, byte alphaThreshold = 0)
    {
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
        bgra.CopyPixels(pixels, bgra.PixelWidth * 4, 0);
        var left = bgra.PixelWidth; var top = bgra.PixelHeight; var right = 0; var bottom = 0;
        for (var y = 0; y < bgra.PixelHeight; y++)
        for (var x = 0; x < bgra.PixelWidth; x++)
        {
            if (pixels[(y * bgra.PixelWidth + x) * 4 + 3] <= alphaThreshold) continue;
            left = Math.Min(left, x); right = Math.Max(right, x + 1);
            top = Math.Min(top, y); bottom = Math.Max(bottom, y + 1);
        }
        if (right <= left || bottom <= top) return Rect.Empty;
        var scale = Math.Min(ImageSize / bgra.PixelWidth, ImageSize / bgra.PixelHeight);
        return new Rect((ImageSize - bgra.PixelWidth * scale) / 2 + left * scale,
            (ImageSize - bgra.PixelHeight * scale) / 2 + top * scale,
            (right - left) * scale, (bottom - top) * scale);
    }

    private static IReadOnlyDictionary<string, BitmapSource> LoadFrames()
    {
        var resources = new ResourceManager("DesktopPuppy.g", typeof(PetVisualGeometry).Assembly);
        var frames = new Dictionary<string, BitmapSource>();
        foreach (var name in PetAnimationPlayer.Sequences.Values.SelectMany(f => f)
                     .Select(f => f.Name).Append("idle").Distinct())
        {
            using var stream = resources.GetStream($"pet/animation/assets/{name}.png")
                ?? throw new InvalidOperationException($"Missing animation frame: {name}");
            var bitmap = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            var cropped = new CroppedBitmap(bitmap, new Int32Rect(250, 80, 800, 1120));
            cropped.Freeze();
            frames.Add(name, cropped);
        }
        return frames;
    }
}
