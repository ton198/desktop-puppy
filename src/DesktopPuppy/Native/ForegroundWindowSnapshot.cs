namespace DesktopPuppy.Native;

public sealed record ForegroundWindowSnapshot(
    nint WindowHandle,
    uint ProcessId,
    string ProcessName,
    string WindowTitle);
