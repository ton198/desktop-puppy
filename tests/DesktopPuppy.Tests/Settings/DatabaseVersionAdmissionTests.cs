using DesktopPuppy.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DesktopPuppy.Tests.Settings;

public sealed class DatabaseVersionAdmissionTests
{
    [Theory]
    [InlineData("settings_schema")]
    [InlineData("ai_schema_version")]
    [InlineData("activity_schema_version")]
    [InlineData("report_schema_version")]
    [InlineData("placement_schema_version")]
    public void Future_module_rejects_without_any_database_changes(string table)
    {
        WithDatabase(path=>
        {
            Execute(path,$"CREATE TABLE {table}(id INTEGER PRIMARY KEY,version INTEGER NOT NULL); INSERT INTO {table} VALUES(1,99)");
            var bytes=File.ReadAllBytes(path);
            Assert.Throws<InvalidOperationException>(()=>DatabaseVersionAdmission.AssertCompatible(path));
            Assert.Equal(bytes,File.ReadAllBytes(path));
        });
    }

    [Theory]
    [InlineData("CREATE TABLE settings_schema(id INTEGER PRIMARY KEY,version INTEGER NOT NULL)")]
    [InlineData("CREATE TABLE settings_schema(id INTEGER PRIMARY KEY,version INTEGER NOT NULL); INSERT INTO settings_schema VALUES(2,1)")]
    [InlineData("CREATE TABLE settings_schema(id INTEGER,version TEXT); INSERT INTO settings_schema VALUES(1,'broken')")]
    [InlineData("CREATE TABLE settings_schema(id INTEGER,version INTEGER); INSERT INTO settings_schema VALUES(1,0)")]
    [InlineData("CREATE TABLE settings_schema(id INTEGER,version INTEGER); INSERT INTO settings_schema VALUES(1,1),(1,1)")]
    [InlineData("CREATE TABLE settings_schema(unexpected INTEGER)")]
    [InlineData("CREATE VIEW settings_schema AS SELECT 1 AS id,1 AS version")]
    public void Malformed_marker_rejects_without_database_changes(string sql)
    {
        WithDatabase(path=>
        {
            Execute(path,sql);var bytes=File.ReadAllBytes(path);
            Assert.Throws<InvalidOperationException>(()=>DatabaseVersionAdmission.AssertCompatible(path));
            Assert.Equal(bytes,File.ReadAllBytes(path));
        });
    }

    [Fact] public void Absent_database_is_not_created()
    {
        var path=Path.Combine(Path.GetTempPath(),"puppy-admission-"+Guid.NewGuid()+".db");
        DatabaseVersionAdmission.AssertCompatible(path);Assert.False(File.Exists(path));
    }

    [Fact] public void Supported_versions_and_unversioned_legacy_database_are_admitted()
    {
        WithDatabase(path=>
        {
            Execute(path,"CREATE TABLE legacy_data(value TEXT); INSERT INTO legacy_data VALUES('retained')");
            DatabaseVersionAdmission.AssertCompatible(path);
            foreach(var(table,version) in new[]{("settings_schema",1),("ai_schema_version",1),("activity_schema_version",1),("report_schema_version",1),("placement_schema_version",1)})
                Execute(path,$"CREATE TABLE {table}(id INTEGER PRIMARY KEY,version INTEGER NOT NULL); INSERT INTO {table} VALUES(1,{version})");
            DatabaseVersionAdmission.AssertCompatible(path);
            Execute(path,"UPDATE ai_schema_version SET version=2");DatabaseVersionAdmission.AssertCompatible(path);
        });
    }
    private static void Execute(string path,string sql)
    {
        using var c=new SqliteConnection($"Data Source={path};Pooling=False");c.Open();using var q=c.CreateCommand();q.CommandText=sql;q.ExecuteNonQuery();
    }
    private static void WithDatabase(Action<string> action)
    {
        SQLitePCL.Batteries_V2.Init();var path=Path.Combine(Path.GetTempPath(),"puppy-admission-"+Guid.NewGuid()+".db");
        try {action(path);}finally {File.Delete(path);}
    }
}
