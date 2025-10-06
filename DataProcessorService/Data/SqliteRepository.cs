using Microsoft.Data.Sqlite;

namespace DataProcessorService.Data
{
    public class SqliteRepository
    {
        private readonly string _dbPath;
        private readonly ILogger<SqliteRepository> _log;
        private readonly string _connectionString;

        public SqliteRepository(IConfiguration cfg, ILogger<SqliteRepository> log)
        {
            _log = log;
            _dbPath = cfg.GetValue<string>("SQLite:Path", "instrument.db");
            _connectionString = new SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString();
            EnsureDatabase();
        }

        private void EnsureDatabase()
        {
            try
            {
                var dir = Path.GetDirectoryName(_dbPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Modules(
                    ModuleCategoryID TEXT PRIMARY KEY,
                    ModuleState TEXT NOT NULL,
                    LastUpdatedUtc TEXT NOT NULL
                );";
                cmd.ExecuteNonQuery();
                _log.LogInformation("SQLite DB initialized at {path}", _dbPath);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to initialize SQLite DB at {path}", _dbPath);
                throw;
            }
        }

        public async Task UpsertModuleAsync(string moduleCategoryId, string moduleState)
        {
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();
                using var tran = conn.BeginTransaction();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                INSERT INTO Modules(ModuleCategoryID, ModuleState, LastUpdatedUtc)
                VALUES($id, $state, $now)
                ON CONFLICT(ModuleCategoryID) DO UPDATE SET
                    ModuleState = excluded.ModuleState,
                    LastUpdatedUtc = excluded.LastUpdatedUtc;
            ";
                cmd.Parameters.AddWithValue("$id", moduleCategoryId);
                cmd.Parameters.AddWithValue("$state", moduleState);
                cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));

                await cmd.ExecuteNonQueryAsync();
                await tran.CommitAsync();
                _log.LogInformation("Saved/Updated module {id} => {state}", moduleCategoryId, moduleState);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to upsert {id}/{state}", moduleCategoryId, moduleState);
                throw;
            }
        }
    }
}
