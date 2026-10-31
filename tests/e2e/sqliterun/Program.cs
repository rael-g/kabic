using Sqlite;
SurfaceGate.Check(typeof(Sqlite.Sqlite).Assembly, "Sqlite");

var db = Sqlite.Sqlite.Open(":memory:");
Sqlite.Sqlite.Exec(db, "create table t(x); insert into t values (1), (2), (3);", null);
var rows = 0;
Sqlite.Sqlite.Exec(db, "select x from t", (columns, values, names) => { rows += columns; return 0; });
if (rows != 3) throw new InvalidOperationException($"rows: {rows}");
try
{
    Sqlite.Sqlite.Exec(db, "not sql", null);
    throw new InvalidOperationException("no failure");
}
catch (SqliteException)
{
    Console.WriteLine(Sqlite.Sqlite.Errmsg(db));
}
Sqlite.Sqlite.Close(db);
Console.WriteLine($"{Sqlite.Sqlite.Libversion()} rows {rows}");
