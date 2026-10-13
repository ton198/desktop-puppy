using System.IO;
using Microsoft.Data.Sqlite;
namespace DesktopPuppy.Settings;
public sealed record SavedPetPlacement(string MonitorId,double LeftDip,double TopDip,string DockSide);
public sealed class PetPlacementStore(string databasePath)
{
    private SqliteConnection Open()
    {
        SQLitePCL.Batteries_V2.Init(); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=databasePath,Pooling=false,DefaultTimeout=5}.ToString());c.Open();
        try
        {
            using var transaction=c.BeginTransaction();using var q=c.CreateCommand();q.Transaction=transaction;
            q.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='placement_schema_version'";
            if(Convert.ToInt64(q.ExecuteScalar(),System.Globalization.CultureInfo.InvariantCulture)>0)
            {
                q.CommandText="SELECT version FROM placement_schema_version WHERE id=1";
                var version=q.ExecuteScalar();
                if(version is not null && version is not DBNull && Convert.ToInt64(version,System.Globalization.CultureInfo.InvariantCulture)>1)
                    throw new InvalidOperationException("Placement database version is unsupported; the original database was preserved.");
            }
            q.CommandText="CREATE TABLE IF NOT EXISTS pet_placement(id INTEGER PRIMARY KEY CHECK(id=1),monitor_id TEXT NOT NULL,left_dip REAL NOT NULL,top_dip REAL NOT NULL,dock_side TEXT NOT NULL); CREATE TABLE IF NOT EXISTS placement_schema_version(id INTEGER PRIMARY KEY CHECK(id=1),version INTEGER NOT NULL); INSERT INTO placement_schema_version VALUES(1,1) ON CONFLICT(id) DO NOTHING;";
            q.ExecuteNonQuery();transaction.Commit();return c;
        }
        catch {c.Dispose();throw;}
    }
    public Task<SavedPetPlacement?> LoadAsync()=>Task.Run(()=>
    {
        using var c=Open();using var q=c.CreateCommand();q.CommandText="SELECT monitor_id,left_dip,top_dip,dock_side FROM pet_placement WHERE id=1";using var r=q.ExecuteReader();
        if(!r.Read())return null;var placement=new SavedPetPlacement(r.GetString(0),r.GetDouble(1),r.GetDouble(2),r.GetString(3));return Valid(placement)?placement:null;
    });
    public Task SaveAsync(SavedPetPlacement placement)
    {
        if(!Valid(placement))throw new ArgumentOutOfRangeException(nameof(placement));
        return Task.Run(()=>{using var c=Open();using var q=c.CreateCommand();q.CommandText="INSERT INTO pet_placement VALUES(1,$m,$l,$t,$d) ON CONFLICT(id) DO UPDATE SET monitor_id=$m,left_dip=$l,top_dip=$t,dock_side=$d";q.Parameters.AddWithValue("$m",placement.MonitorId);q.Parameters.AddWithValue("$l",placement.LeftDip);q.Parameters.AddWithValue("$t",placement.TopDip);q.Parameters.AddWithValue("$d",placement.DockSide);q.ExecuteNonQuery();});
    }
    public Task ResetAsync()=>Task.Run(()=>{using var c=Open();using var q=c.CreateCommand();q.CommandText="DELETE FROM pet_placement";q.ExecuteNonQuery();});
    private static bool Valid(SavedPetPlacement p)=>!string.IsNullOrWhiteSpace(p.MonitorId)&&double.IsFinite(p.LeftDip)&&double.IsFinite(p.TopDip)&&p.DockSide is "None" or "Left" or "Right";
}
