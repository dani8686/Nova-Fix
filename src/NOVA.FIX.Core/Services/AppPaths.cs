namespace NOVA.FIX.Core.Services;

/// <summary>
/// Central resolver for all local application paths. Everything is under
/// %LOCALAPPDATA%\NOVA FIX so the product never needs network locations.
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string? rootOverride = null)
    {
        Root = rootOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NOVA FIX");
        Data = Path.Combine(Root, "data");
        Database = Path.Combine(Root, "database");
        Knowledge = Path.Combine(Root, "knowledge");
        Models = Path.Combine(Root, "models");
        Logs = Path.Combine(Root, "logs");
        Reports = Path.Combine(Root, "reports");
        Snapshots = Path.Combine(Root, "snapshots");
        Updates = Path.Combine(Root, "updates");
        Backups = Path.Combine(Root, "backups");
        SettingsFile = Path.Combine(Root, "settings.json");

        foreach (var dir in new[]
                 {
                     Root, Data, Database, Knowledge, Models, Logs, Reports,
                     Snapshots, Updates, Backups,
                 })
        {
            Directory.CreateDirectory(dir);
        }
    }

    public string Root { get; }
    public string Data { get; }
    public string Database { get; }
    public string Knowledge { get; }
    public string Models { get; }
    public string Logs { get; }
    public string Reports { get; }
    public string Snapshots { get; }
    public string Updates { get; }
    public string Backups { get; }
    public string SettingsFile { get; }

    /// <summary>Bundled read-only data directory shipped next to the executable.</summary>
    public static string BundledData =>
        Path.Combine(AppContext.BaseDirectory, "data");
}
