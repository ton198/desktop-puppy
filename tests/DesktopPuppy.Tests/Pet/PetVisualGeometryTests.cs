using System.Windows.Media;
using DesktopPuppy.Pet.Animation;
using Xunit;

namespace DesktopPuppy.Tests.Pet;

public sealed class PetVisualGeometryTests
{
    [Fact]
    public void Every_animation_pixel_fits_the_fixed_viewport_at_the_original_scale()
    {
        var viewport = PetVisualGeometry.VisibleBounds;
        var names = PetAnimationPlayer.Sequences.Values.SelectMany(f => f).Select(f => f.Name).Append("idle").Distinct();
        foreach (var name in names)
        {
            var frame = PetVisualGeometry.NormalFrames[name];
            var bitmap = new System.Windows.Media.Imaging.FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            // Before this change the 800x1120 crop was uniformly displayed in a 132x132 image.
            const double scale = 132d / 1120;
            const double horizontalInset = (132 - 800 * scale) / 2;
            for (var y = 0; y < bitmap.PixelHeight; y++)
            for (var x = 0; x < bitmap.PixelWidth; x++)
            {
                if (pixels[(y * bitmap.PixelWidth + x) * 4 + 3] == 0) continue;
                var left = horizontalInset + x * scale;
                var top = y * scale;
                Assert.True(left >= viewport.Left - .000001 && top >= viewport.Top - .000001 &&
                    left + scale <= viewport.Right + .000001 && top + scale <= viewport.Bottom + .000001,
                    $"{name}: nontransparent pixel ({x}, {y}) is outside the fixed pet viewport.");
            }
        }
    }
    [Theory]
    [InlineData(1, .12)]
    [InlineData(1, .349)]
    [InlineData(2, .35)]
    [InlineData(2, .649)]
    [InlineData(3, .65)]
    [InlineData(3, .88)]
    public void Turn_ink_fits_the_fixed_window_at_both_ends_of_each_pose(int frame, double orientation)
    {
        var resources = new System.Resources.ResourceManager("DesktopPuppy.g", typeof(PetVisualGeometry).Assembly);
        using var stream = resources.GetStream($"pet/animation/assets/turn-{frame}.png")!;
        var bitmap = System.Windows.Media.Imaging.BitmapDecoder.Create(stream,
            System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
        var ink = PetVisualGeometry.GetInkBounds(bitmap);
        var bounds = PetVisualGeometry.VisibleBounds;
        var left = (bounds.Right - 132) * (1 - orientation) - bounds.Left * orientation;
        Assert.True(ink.Left + left >= 0 && ink.Right + left <= bounds.Width &&
            ink.Top >= bounds.Top && ink.Bottom <= bounds.Bottom,
            $"turn-{frame} at {orientation}: ink={ink}, left={left}, viewport={bounds}");
    }
}
