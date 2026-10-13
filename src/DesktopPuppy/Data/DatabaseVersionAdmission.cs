using System.IO;
using Microsoft.Data.Sqlite;

namespace DesktopPuppy.Data;

/// <summary>Admits the entire database before any module initializes or migrates it.</summary>
public static class DatabaseVersionAdmission
{
    private static readonly (string Table,int MaximumVersion)[] Modules =
    [
        ("settings_schema",1), ("ai_schema_version",2), ("activity_schema_version",1),
        ("report_schema_version",1), ("placement_schema_version",1)
    ];

    public static void AssertCompatible(string databasePath)
    {
        if(!File.Exists(databasePath)) return;
        SQLitePCL.Batteries_V2.Init();
        using var connection=new SqliteConnection(new SqliteConnectionStringBuilder
        {DataSource=databasePath,Mode=SqliteOpenMode.ReadOnly,Pooling=false,DefaultTimeout=5}.ToString());
        connection.Open();
        foreach(var (table,maximumVersion) in Modules)
        {
            using var query=connection.CreateCommand();
            query.CommandText="SELECT type FROM sqlite_master WHERE name=$name";
            query.Parameters.AddWithValue("$name",table);
            var markerType=query.ExecuteScalar();
            if(markerType is null) continue;
            if(markerType is not string type || type!="table")
                throw new InvalidOperationException("Database module version is unsupported or malformed; the original database was preserved.");
            try
            {
                query.CommandText=$"SELECT id,version FROM {table}";
                using var rows=query.ExecuteReader();
                if(!rows.Read() || rows.GetValue(0) is not long id || id!=1 || rows.GetValue(1) is not long version || version<1 || version>maximumVersion || rows.Read())
                    throw new InvalidOperationException("Database module version is unsupported or malformed; the original database was preserved.");
            }
            catch(SqliteException)
            {
                throw new InvalidOperationException("Database module version is unsupported or malformed; the original database was preserved.");
            }
        }
    }
}
