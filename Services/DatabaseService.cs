using System.Globalization;


using System.IO;


using System.Text;


using Microsoft.Data.Sqlite;
using TempControl.Models;





namespace TempControl.Services;





/// <summary>


/// SQLite service for persisting temperature data and exporting CSV reports.


/// </summary>


public sealed class DatabaseService : IDisposable


{


    private readonly string _dbPath;


    private SqliteConnection? _connection;





    public DatabaseService(string dbPath = "ovendata.db")


    {


        _dbPath = dbPath;


    }





    public void Initialize()


    {


        _connection = new SqliteConnection($"Data Source={_dbPath}");


        _connection.Open();





        using var cmd = _connection.CreateCommand();


        cmd.CommandText = """


            CREATE TABLE IF NOT EXISTS TemperatureLog (


                Id         INTEGER PRIMARY KEY AUTOINCREMENT,


                Timestamp  TEXT    NOT NULL,


                Zone1Temp  REAL    NOT NULL,


                Zone2Temp  REAL    NOT NULL,


                Zone1SetPv REAL    NOT NULL DEFAULT 0,


                Zone2SetPv REAL    NOT NULL DEFAULT 0,


                Zone1Sv    REAL    NOT NULL DEFAULT 0,


                Zone2Sv    REAL    NOT NULL DEFAULT 0,


                Zone1JobPv REAL    NOT NULL DEFAULT 0,


                Zone2JobPv REAL    NOT NULL DEFAULT 0


            );


            CREATE INDEX IF NOT EXISTS IX_TemperatureLog_Timestamp ON TemperatureLog(Timestamp);


            """;


        cmd.ExecuteNonQuery();

        using var recipesCmd = _connection.CreateCommand();
        recipesCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Recipes (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RecipeName TEXT NOT NULL,
                RecipeMode INTEGER NOT NULL UNIQUE,
                Step1Zone1Temp REAL NOT NULL, Step1Zone2Temp REAL NOT NULL, Step1SoakTime REAL NOT NULL, Step1RampRate REAL NOT NULL,
                Step2Zone1Temp REAL NOT NULL, Step2Zone2Temp REAL NOT NULL, Step2SoakTime REAL NOT NULL, Step2RampRate REAL NOT NULL,
                Step3Zone1Temp REAL NOT NULL, Step3Zone2Temp REAL NOT NULL, Step3SoakTime REAL NOT NULL, Step3RampRate REAL NOT NULL,
                Step4Zone1Temp REAL NOT NULL, Step4Zone2Temp REAL NOT NULL, Step4SoakTime REAL NOT NULL, Step4RampRate REAL NOT NULL,
                Step5Zone1Temp REAL NOT NULL, Step5Zone2Temp REAL NOT NULL, Step5SoakTime REAL NOT NULL, Step5RampRate REAL NOT NULL,
                ProcessZone1Safety REAL NOT NULL, ProcessZone2Safety REAL NOT NULL,
                ProcessBlower1 REAL NOT NULL, ProcessBlower2 REAL NOT NULL
            );
            """;
        recipesCmd.ExecuteNonQuery();





        RenameColumnIfExists("TemperatureLog", "Zone1JobPv", "Zone1SetPv");
        RenameColumnIfExists("TemperatureLog", "Zone2JobPv", "Zone2SetPv");
        RenameColumnIfExists("TemperatureLog", "Zone1SafetyTemp", "Zone1JobPv");
        RenameColumnIfExists("TemperatureLog", "Zone2SafetyTemp", "Zone2JobPv");

        EnsureColumnExists("TemperatureLog", "Zone1SetPv", "REAL NOT NULL DEFAULT 0");


        EnsureColumnExists("TemperatureLog", "Zone2SetPv", "REAL NOT NULL DEFAULT 0");


        EnsureColumnExists("TemperatureLog", "Zone1Sv", "REAL NOT NULL DEFAULT 0");


        EnsureColumnExists("TemperatureLog", "Zone2Sv", "REAL NOT NULL DEFAULT 0");

        EnsureColumnExists("TemperatureLog", "Zone2JobPv", "REAL NOT NULL DEFAULT 0");

        SeedRecipesIfEmpty();

        EnsureColumnExists("TemperatureLog", "Zone2JobPv", "REAL NOT NULL DEFAULT 0");





    }

    public List<Recipe> GetRecipes()
    {
        var recipes = new List<Recipe>();
        if (_connection is null) return recipes;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Recipes ORDER BY RecipeMode";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) recipes.Add(ReadRecipe(reader));
        return recipes;
    }

    public void UpdateRecipe(Recipe recipe)
    {
        if (_connection is null) throw new InvalidOperationException("Database has not been initialized.");
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE Recipes SET RecipeName = $name, RecipeMode = $mode,
                Step1Zone1Temp = $s1z1, Step1Zone2Temp = $s1z2, Step1SoakTime = $s1soak, Step1RampRate = $s1ramp,
                Step2Zone1Temp = $s2z1, Step2Zone2Temp = $s2z2, Step2SoakTime = $s2soak, Step2RampRate = $s2ramp,
                Step3Zone1Temp = $s3z1, Step3Zone2Temp = $s3z2, Step3SoakTime = $s3soak, Step3RampRate = $s3ramp,
                Step4Zone1Temp = $s4z1, Step4Zone2Temp = $s4z2, Step4SoakTime = $s4soak, Step4RampRate = $s4ramp,
                Step5Zone1Temp = $s5z1, Step5Zone2Temp = $s5z2, Step5SoakTime = $s5soak, Step5RampRate = $s5ramp,
                ProcessZone1Safety = $pz1, ProcessZone2Safety = $pz2, ProcessBlower1 = $blower1, ProcessBlower2 = $blower2
            WHERE Id = $id
            """;
        AddRecipeParameters(cmd, recipe);
        cmd.Parameters.AddWithValue("$id", recipe.Id);
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("The selected recipe no longer exists.");
    }

    public void InsertRecord(DateTime timestamp, double zone1Temp, double zone2Temp)
    => InsertRecord(timestamp, zone1Temp, zone2Temp, 0, 0, 0, 0, 0, 0);

    public void InsertRecord(DateTime timestamp, double zone1Temp, double zone2Temp, double zone1SetPv, double zone2SetPv)
        => InsertRecord(timestamp, zone1Temp, zone2Temp, zone1SetPv, zone2SetPv, 0, 0, 0, 0);

    public void InsertRecord(DateTime timestamp, double zone1Temp, double zone2Temp, double zone1SetPv, double zone2SetPv, double zone1Sv, double zone2Sv)
        => InsertRecord(timestamp, zone1Temp, zone2Temp, zone1SetPv, zone2SetPv, zone1Sv, zone2Sv, 0, 0);

    public void InsertRecord(DateTime timestamp, double zone1Temp, double zone2Temp, double zone1SetPv, double zone2SetPv, double zone1Sv, double zone2Sv, double zone1JobPv, double zone2JobPv)
    {
        if (_connection is null) return;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT INTO TemperatureLog (Timestamp, Zone1Temp, Zone2Temp, Zone1SetPv, Zone2SetPv, Zone1Sv, Zone2Sv, Zone1JobPv, Zone2JobPv) VALUES ($ts, $z1, $z2, $z1Set, $z2Set, $z1Sv, $z2Sv, $z1Job, $z2Job)";
        cmd.Parameters.AddWithValue("$ts", timestamp.ToString("o"));
        cmd.Parameters.AddWithValue("$z1", zone1Temp);
        cmd.Parameters.AddWithValue("$z2", zone2Temp);
        cmd.Parameters.AddWithValue("$z1Set", zone1SetPv);
        cmd.Parameters.AddWithValue("$z2Set", zone2SetPv);
        cmd.Parameters.AddWithValue("$z1Sv", zone1Sv);
        cmd.Parameters.AddWithValue("$z2Sv", zone2Sv);
        cmd.Parameters.AddWithValue("$z1Job", zone1JobPv);
        cmd.Parameters.AddWithValue("$z2Job", zone2JobPv);
        cmd.ExecuteNonQuery();
    }





    public List<(DateTime Timestamp, double Zone1Pv, double Zone1Sv, double Zone2Pv, double Zone2Sv)> QueryRangeWithSv(DateTime from, DateTime to)


    {


        var results = new List<(DateTime, double, double, double, double)>();


        if (_connection is null) return results;





        using var cmd = _connection.CreateCommand();


        cmd.CommandText = """


            SELECT Timestamp, Zone1Temp, Zone1Sv, Zone2Temp, Zone2Sv


            FROM TemperatureLog


            WHERE Timestamp >= $from AND Timestamp <= $to


            ORDER BY Timestamp


            """;


        cmd.Parameters.AddWithValue("$from", from.ToString("o"));


        cmd.Parameters.AddWithValue("$to", to.ToString("o"));





        using var reader = cmd.ExecuteReader();


        while (reader.Read())


        {


            var ts = DateTime.Parse(reader.GetString(0), null, DateTimeStyles.RoundtripKind);


            var z1Pv = reader.GetDouble(1);


            var z1JobPv = reader.GetDouble(2);


            var z2Pv = reader.GetDouble(3);


            var z2JobPv = reader.GetDouble(4);


            results.Add((ts, z1Pv, z1JobPv, z2Pv, z2JobPv));


        }





        return results;


    }





    public List<(DateTime Timestamp, double Zone1, double Zone2)> QueryRange(DateTime from, DateTime to)


    {


        var results = new List<(DateTime, double, double)>();


        if (_connection is null) return results;





        using var cmd = _connection.CreateCommand();


        cmd.CommandText = """


            SELECT Timestamp, Zone1Temp, Zone2Temp


            FROM TemperatureLog


            WHERE Timestamp >= $from AND Timestamp <= $to


            ORDER BY Timestamp


            """;


        cmd.Parameters.AddWithValue("$from", from.ToString("o"));


        cmd.Parameters.AddWithValue("$to", to.ToString("o"));





        using var reader = cmd.ExecuteReader();


        while (reader.Read())


        {


            var ts = DateTime.Parse(reader.GetString(0), null, DateTimeStyles.RoundtripKind);


            var z1 = reader.GetDouble(1);


            var z2 = reader.GetDouble(2);


            results.Add((ts, z1, z2));


        }


        return results;


    }





    public string ExportToCsv(string filePath, DateTime from, DateTime to)


        => ExportToCsv(filePath, from, to, null);





    public string ExportToCsv(string filePath, DateTime from, DateTime to, string? selectedZone)

    {

        var data = QueryRangeFull(from, to, selectedZone);

        if (data.Count == 0)

            return "No data found for the selected date range.";



        var sb = new StringBuilder();
        sb.AppendLine("Sr.No,Timestamp,Zone1Temp (C),Zone2Temp (C),setPv (C),Zone1JobPv (C),Zone2JobPv (C)");
        foreach (var (srNo, ts, z1, z2, setPv, z1JobPv, z2JobPv) in data)
            sb.AppendLine($"{srNo},{ts:yyyy-MM-dd HH:mm:ss},{z1:F1},{z2:F1},{setPv:F1},{z1JobPv:F1},{z2JobPv:F1}");



        var dir = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))

            Directory.CreateDirectory(dir);



        File.WriteAllText(filePath, sb.ToString());

        return $"Exported {data.Count} records to {filePath}";

    }





    public List<(int SrNo, DateTime Timestamp, double Zone1Temp, double Zone2Temp, double SetPv, double Zone1JobPv, double Zone2JobPv)> QueryRangeFull(DateTime from, DateTime to, string? selectedZone = null)
    {
        var results = new List<(int, DateTime, double, double, double, double, double)>();
        if (_connection is null) return results;

        var setPvColumn = string.Equals(selectedZone, "Zone2", StringComparison.OrdinalIgnoreCase)
            ? "Zone2SetPv"
            : "Zone1SetPv";

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT Timestamp, Zone1Temp, Zone2Temp, {setPvColumn}, Zone1JobPv, Zone2JobPv
            FROM TemperatureLog
            WHERE Timestamp >= $from AND Timestamp <= $to
            ORDER BY Timestamp
            """;
        cmd.Parameters.AddWithValue("$from", from.ToString("o"));
        cmd.Parameters.AddWithValue("$to", to.ToString("o"));

        int srNo = 0;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var ts = DateTime.Parse(reader.GetString(0), null, System.Globalization.DateTimeStyles.RoundtripKind);
            var z1 = reader.GetDouble(1);
            var z2 = reader.GetDouble(2);
            var setPv = reader.GetDouble(3);
            var z1JobPv = reader.GetDouble(4);
            var z2JobPv = reader.GetDouble(5);
            results.Add((++srNo, ts, z1, z2, setPv, z1JobPv, z2JobPv));
        }
        return results;
    }



    private void SeedRecipesIfEmpty()
    {
        if (_connection is null) return;

        using var countCmd = _connection.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM Recipes";
        if (Convert.ToInt64(countCmd.ExecuteScalar()) != 0) return;

        foreach (var recipe in new[]
        {
            CreateSeedRecipe(1, "Standard Cure", 150, 150, 30, 5),
            CreateSeedRecipe(2, "High Temperature Cure", 180, 180, 45, 7),
            CreateSeedRecipe(3, "Preheat Cycle", 90, 90, 20, 3)
        })
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Recipes (RecipeName, RecipeMode,
                    Step1Zone1Temp, Step1Zone2Temp, Step1SoakTime, Step1RampRate,
                    Step2Zone1Temp, Step2Zone2Temp, Step2SoakTime, Step2RampRate,
                    Step3Zone1Temp, Step3Zone2Temp, Step3SoakTime, Step3RampRate,
                    Step4Zone1Temp, Step4Zone2Temp, Step4SoakTime, Step4RampRate,
                    Step5Zone1Temp, Step5Zone2Temp, Step5SoakTime, Step5RampRate,
                    ProcessZone1Safety, ProcessZone2Safety, ProcessBlower1, ProcessBlower2)
                VALUES ($name, $mode, $s1z1, $s1z2, $s1soak, $s1ramp, $s2z1, $s2z2, $s2soak, $s2ramp,
                    $s3z1, $s3z2, $s3soak, $s3ramp, $s4z1, $s4z2, $s4soak, $s4ramp,
                    $s5z1, $s5z2, $s5soak, $s5ramp, $pz1, $pz2, $blower1, $blower2)
                """;
            AddRecipeParameters(cmd, recipe);
            cmd.ExecuteNonQuery();
        }
    }

    private static Recipe CreateSeedRecipe(int mode, string name, double temperature, double soak, double ramp, double safety)
        => new()
        {
            RecipeName = name,
            RecipeMode = mode,
            Step1Zone1Temp = temperature, Step1Zone2Temp = temperature, Step1SoakTime = soak, Step1RampRate = ramp,
            Step2Zone1Temp = temperature, Step2Zone2Temp = temperature, Step2SoakTime = soak, Step2RampRate = ramp,
            Step3Zone1Temp = temperature, Step3Zone2Temp = temperature, Step3SoakTime = soak, Step3RampRate = ramp,
            Step4Zone1Temp = temperature, Step4Zone2Temp = temperature, Step4SoakTime = soak, Step4RampRate = ramp,
            Step5Zone1Temp = temperature, Step5Zone2Temp = temperature, Step5SoakTime = soak, Step5RampRate = ramp,
            ProcessZone1Safety = safety, ProcessZone2Safety = safety, ProcessBlower1 = 10, ProcessBlower2 = 10
        };

    private static Recipe ReadRecipe(SqliteDataReader reader)
        => new()
        {
            Id = reader.GetInt64(0), RecipeName = reader.GetString(1), RecipeMode = reader.GetInt32(2),
            Step1Zone1Temp = reader.GetDouble(3), Step1Zone2Temp = reader.GetDouble(4), Step1SoakTime = reader.GetDouble(5), Step1RampRate = reader.GetDouble(6),
            Step2Zone1Temp = reader.GetDouble(7), Step2Zone2Temp = reader.GetDouble(8), Step2SoakTime = reader.GetDouble(9), Step2RampRate = reader.GetDouble(10),
            Step3Zone1Temp = reader.GetDouble(11), Step3Zone2Temp = reader.GetDouble(12), Step3SoakTime = reader.GetDouble(13), Step3RampRate = reader.GetDouble(14),
            Step4Zone1Temp = reader.GetDouble(15), Step4Zone2Temp = reader.GetDouble(16), Step4SoakTime = reader.GetDouble(17), Step4RampRate = reader.GetDouble(18),
            Step5Zone1Temp = reader.GetDouble(19), Step5Zone2Temp = reader.GetDouble(20), Step5SoakTime = reader.GetDouble(21), Step5RampRate = reader.GetDouble(22),
            ProcessZone1Safety = reader.GetDouble(23), ProcessZone2Safety = reader.GetDouble(24), ProcessBlower1 = reader.GetDouble(25), ProcessBlower2 = reader.GetDouble(26)
        };

    private static void AddRecipeParameters(SqliteCommand cmd, Recipe recipe)
    {
        cmd.Parameters.AddWithValue("$name", recipe.RecipeName);
        cmd.Parameters.AddWithValue("$mode", recipe.RecipeMode);
        cmd.Parameters.AddWithValue("$s1z1", recipe.Step1Zone1Temp); cmd.Parameters.AddWithValue("$s1z2", recipe.Step1Zone2Temp); cmd.Parameters.AddWithValue("$s1soak", recipe.Step1SoakTime); cmd.Parameters.AddWithValue("$s1ramp", recipe.Step1RampRate);
        cmd.Parameters.AddWithValue("$s2z1", recipe.Step2Zone1Temp); cmd.Parameters.AddWithValue("$s2z2", recipe.Step2Zone2Temp); cmd.Parameters.AddWithValue("$s2soak", recipe.Step2SoakTime); cmd.Parameters.AddWithValue("$s2ramp", recipe.Step2RampRate);
        cmd.Parameters.AddWithValue("$s3z1", recipe.Step3Zone1Temp); cmd.Parameters.AddWithValue("$s3z2", recipe.Step3Zone2Temp); cmd.Parameters.AddWithValue("$s3soak", recipe.Step3SoakTime); cmd.Parameters.AddWithValue("$s3ramp", recipe.Step3RampRate);
        cmd.Parameters.AddWithValue("$s4z1", recipe.Step4Zone1Temp); cmd.Parameters.AddWithValue("$s4z2", recipe.Step4Zone2Temp); cmd.Parameters.AddWithValue("$s4soak", recipe.Step4SoakTime); cmd.Parameters.AddWithValue("$s4ramp", recipe.Step4RampRate);
        cmd.Parameters.AddWithValue("$s5z1", recipe.Step5Zone1Temp); cmd.Parameters.AddWithValue("$s5z2", recipe.Step5Zone2Temp); cmd.Parameters.AddWithValue("$s5soak", recipe.Step5SoakTime); cmd.Parameters.AddWithValue("$s5ramp", recipe.Step5RampRate);
        cmd.Parameters.AddWithValue("$pz1", recipe.ProcessZone1Safety); cmd.Parameters.AddWithValue("$pz2", recipe.ProcessZone2Safety);
        cmd.Parameters.AddWithValue("$blower1", recipe.ProcessBlower1); cmd.Parameters.AddWithValue("$blower2", recipe.ProcessBlower2);
    }

    public void Dispose()


    {


        _connection?.Dispose();


    }





    private void EnsureColumnExists(string tableName, string columnName, string columnDefinition)


    {


        if (_connection is null) return;





        using var pragma = _connection.CreateCommand();


        pragma.CommandText = $"PRAGMA table_info({tableName})";





        using var reader = pragma.ExecuteReader();


        while (reader.Read())


        {


            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))


                return;


        }





        using var alter = _connection.CreateCommand();


        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition}";


        alter.ExecuteNonQuery();


    }



    private void RenameColumnIfExists(string tableName, string oldColumnName, string newColumnName)


    {


        if (_connection is null || !ColumnExists(tableName, oldColumnName) || ColumnExists(tableName, newColumnName))


            return;





        using var alter = _connection.CreateCommand();


        alter.CommandText = $"ALTER TABLE {tableName} RENAME COLUMN {oldColumnName} TO {newColumnName}";


        alter.ExecuteNonQuery();


    }





    private bool ColumnExists(string tableName, string columnName)


    {


        if (_connection is null) return false;





        using var pragma = _connection.CreateCommand();


        pragma.CommandText = $"PRAGMA table_info({tableName})";





        using var reader = pragma.ExecuteReader();


        while (reader.Read())


        {


            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))


                return true;


        }





        return false;


    }


}


