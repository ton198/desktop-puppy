using System.Text.Json;
namespace DesktopPuppy.Pet.Animation;

public sealed record AnimationFrame(string Name, int DurationMs);

public sealed class PetAnimationPlayer
{
    public static IReadOnlyDictionary<string, AnimationFrame[]> Sequences { get; } = LoadSequences();
    private AnimationFrame[]? _frames;
    private TimeSpan _started;
    private bool _hold;
    public void Play(string animation, TimeSpan now)
    {
        _frames = Sequences[animation];
        _started = now;
        _hold = animation == "drag";
    }
    public string FrameAt(TimeSpan now)
    {
        if (_frames is null) return "idle";
        if (_hold) return _frames[0].Name;
        var elapsed = Math.Max(0, (now - _started).TotalMilliseconds);
        foreach (var frame in _frames)
        {
            if (elapsed < frame.DurationMs) return frame.Name;
            elapsed -= frame.DurationMs;
        }
        _frames = null;
        return "idle";
    }
    private static IReadOnlyDictionary<string, AnimationFrame[]> LoadSequences()
    {
        using var stream = typeof(PetAnimationPlayer).Assembly.GetManifestResourceStream("DesktopPuppy.Animation.sequences.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, AnimationFrame[]>>(stream)!;
    }
}
