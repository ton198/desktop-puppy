namespace DesktopPuppy.WindowsAcceptance.Core;

public sealed class AcceptanceOwnedDirectory
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DesktopPuppy.WindowsAcceptance"));
    private readonly string ownedPath;

    private AcceptanceOwnedDirectory(string ownedPath)
    {
        this.ownedPath = ownedPath;
        DatabasePath = Path.Combine(ownedPath, "usage.db");
    }

    public string DatabasePath { get; }

    public static AcceptanceOwnedDirectory Create()
    {
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new AcceptanceOwnedDirectory(path);
    }

    public void DeleteOwned()
    {
        // Never derive a deletion target from the database path or a caller-supplied run ID.
        if (Path.GetDirectoryName(ownedPath) != Root || !Directory.Exists(ownedPath)) return;
        if ((File.GetAttributes(ownedPath) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Owned directory was replaced by a link; refusing recursive cleanup.");
        Directory.Delete(ownedPath, recursive: true);
    }
}
