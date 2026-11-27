using System.Text.Json;
using System.Text.Json.Serialization;

namespace NOVA.FIX.Core.Services;

/// <summary>User-editable application settings (all local, no telemetry).</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public string Language { get; set; } = "en";
    public int MonitoringIntervalSeconds { get; set; } = 2;
    public string ReportDirectory { get; set; } = string.Empty;
    public string AiModelProfile { get; set; } = "Balanced";
    public int AiMaxContextTokens { get; set; } = 2048;
    public bool AiEnabled { get; set; } = true;
    public bool AllowBackgroundAnalysis { get; set; } = true;
    public bool CollectDiagnosticHistory { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;
    public bool CreateRestorePointBeforeRepair { get; set; } = true;
    public int HistoryRetentionDays { get; set; } = 90;
    public string? LastOpenedModule { get; set; }
}

/// <summary>Loads/saves settings atomically from local disk.</summary>
public interface ISettingsService
{
    AppSettings Current { get; }
    event EventHandler? SettingsChanged;
    Task LoadAsync(CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
    void Update(Action<AppSettings> mutate);
}

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SettingsService(AppPaths paths)
    {
        _filePath = paths.SettingsFile;
        Current = new AppSettings();
    }

    public AppSettings Current { get; private set; }

    public event EventHandler? SettingsChanged;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath, ct).ConfigureAwait(false);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, Options);
                if (loaded is not null) Current = loaded;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Corrupt settings must not brick the app — fall back to defaults.
            Current = new AppSettings();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var tmp = _filePath + ".tmp";
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(Current, Options), ct)
                .ConfigureAwait(false);
            File.Move(tmp, _filePath, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Update(Action<AppSettings> mutate)
    {
        mutate(Current);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        _ = SaveAsync();
    }
}
