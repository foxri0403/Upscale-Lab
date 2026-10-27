using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UpscaleLab.Infrastructure.Database;

// Explicit operator tool. Never invoked by API startup or CI/CD.
const string expectedMigration = "20261027034626_AddCompanionInventory";
var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings__DefaultConnection is required.");
await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options);
var pending = (await db.Database.GetPendingMigrationsAsync()).ToArray();
await db.Database.OpenConnectionAsync();
await using (var identity = db.Database.GetDbConnection().CreateCommand())
{
    identity.CommandText = "SELECT current_database(), current_user";
    await using var reader = await identity.ExecuteReaderAsync(); await reader.ReadAsync();
    Console.WriteLine($"Database: {reader.GetString(0)}; owner: {reader.GetString(1)}");
}
Console.WriteLine("Pending migrations: " + string.Join(", ", pending));
Console.WriteLine("PostgreSQL server version: " + db.Database.GetDbConnection().ServerVersion);
if (args.Length == 1 && args[0] == "status") return;
if (args.Length != 2 || args[0] != "apply-companion") throw new ArgumentException("Use status, or apply-companion /absolute/backup.dump");
if (pending.Length == 0) { Console.WriteLine("Already applied."); return; }
if (pending.Length != 1 || pending[0] != expectedMigration) throw new InvalidOperationException("Unexpected pending migrations; review them before applying.");
var backup = Path.GetFullPath(args[1]);
if (!Path.IsPathFullyQualified(args[1]) || File.Exists(backup)) throw new ArgumentException("A new absolute backup path is required.");
Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
var settings = new NpgsqlConnectionStringBuilder(connection);
var start = new ProcessStartInfo("pg_dump") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
start.ArgumentList.Add("--format=custom"); start.ArgumentList.Add("--no-password"); start.ArgumentList.Add("--file=" + backup);
start.Environment["PGHOST"] = settings.Host; start.Environment["PGPORT"] = settings.Port.ToString();
start.Environment["PGUSER"] = settings.Username; start.Environment["PGPASSWORD"] = settings.Password;
start.Environment["PGDATABASE"] = settings.Database; start.Environment["PGCONNECT_TIMEOUT"] = "15";
start.Environment["PGSSLMODE"] = settings.SslMode switch { SslMode.VerifyFull => "verify-full", SslMode.VerifyCA => "verify-ca", _ => settings.SslMode.ToString().ToLowerInvariant() };
if (!string.IsNullOrEmpty(settings.RootCertificate)) start.Environment["PGSSLROOTCERT"] = settings.RootCertificate;
using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start pg_dump.");
var errorTask = process.StandardError.ReadToEndAsync(); var outputTask = process.StandardOutput.ReadToEndAsync();
await process.WaitForExitAsync(); var backupError = await errorTask; await outputTask;
if (process.ExitCode != 0 || !File.Exists(backup) || new FileInfo(backup).Length == 0)
{
    if (!string.IsNullOrEmpty(settings.Password)) backupError = backupError.Replace(settings.Password, "[redacted]", StringComparison.Ordinal);
    Console.Error.WriteLine(backupError);
    throw new InvalidOperationException("Database backup failed. No migration was applied.");
}
if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(backup, UnixFileMode.UserRead | UnixFileMode.UserWrite);
Console.WriteLine($"Backup completed: {backup} ({new FileInfo(backup).Length} bytes)");
await db.Database.MigrateAsync();
Console.WriteLine("Companion inventory migration applied.");
