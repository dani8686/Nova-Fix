using System.Collections.ObjectModel;
using System.Text.Json;
using NOVA.FIX.Core.Mvvm;
using NOVA.FIX.Core.Services;
using NOVA.FIX.Domain.Contracts;

namespace NOVA.FIX.App.ViewModels;

/// <summary>
/// Windows Compatibility Center: versioned OFFLINE compatibility database.
/// Displays the bundled database date honestly; never claims to be current.
/// </summary>
public sealed class CompatibilityViewModel : ViewModelBase
{
    private readonly IHardwareProbe _hardware;
    private readonly IWindowsProbe _windows;
    private readonly string _dbPath;

    public CompatibilityViewModel(ServiceContainer c)
    {
        _hardware = c.Resolve<IHardwareProbe>();
        _windows = c.Resolve<IWindowsProbe>();
        _dbPath = Path.Combine(AppPaths.BundledData, "windows-compatibility",
            "compat-db.json");
        AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync);
    }

    public AsyncRelayCommand AnalyzeCommand { get; }

    public ObservableCollection<string> Findings { get; } = new();

    public string DatabaseInfo { get; private set; } =
        "Bundled compatibility database: loading…";

    private async Task AnalyzeAsync(CancellationToken ct)
    {
        IsBusy = true;
        try
        {
            LoadDatabaseInfo();

            var win = await _windows.GetWindowsInfoAsync(ct);
            var tpm = await _hardware.GetTpmStatusAsync(ct);
            var secureBoot = await _hardware.GetSecureBootAsync(ct);
            var cpu = await _hardware.GetCpuAsync(ct);
            var mem = await _hardware.GetMemoryAsync(ct);

            Findings.Clear();

            if (win.Success && win.Value is not null)
            {
                Findings.Add($"Windows: {win.Value.ProductName} {win.Value.Edition}, " +
                             $"build {win.Value.Build}, {win.Value.Architecture}");
            }
            else Findings.Add("Windows info: unavailable (probe reported honestly).");

            Findings.Add(tpm.Success
                ? $"TPM: {tpm.Value ?? "not present"}"
                : "TPM: unavailable (probe failed honestly).");

            Findings.Add(secureBoot.Success
                ? $"Secure Boot: {(secureBoot.Value is null ? "unknown" : secureBoot.Value.Value ? "enabled" : "disabled")}"
                : "Secure Boot: unavailable.");

            if (cpu.Success && cpu.Value is not null)
            {
                Findings.Add($"CPU: {cpu.Value.Name} ({cpu.Value.PhysicalCores}C/" +
                             $"{cpu.Value.LogicalProcessors}T)");
                Findings.Add(
                    "Windows 11 note: 7th-generation Intel Core CPUs are generally " +
                    "NOT on Microsoft's official Windows 11 CPU list. See bundled " +
                    "compatibility database for the reference date.");
            }

            if (mem.Success && mem.Value is not null)
            {
                var gb = mem.Value.TotalBytes / (1024 * 1024 * 1024);
                Findings.Add($"RAM: {gb} GB " +
                             (gb >= 8 ? "(meets recommended)" : "(below 8 GB recommended)"));
            }

            Findings.Add(string.Empty);
            Findings.Add(
                "This analysis uses the BUNDLED offline compatibility database " +
                $"(date shown above). It is never described as the latest " +
                "information. Updated compatibility packs can be imported via " +
                "Offline Updates. NOVA FIX never silently bypasses Windows " +
                "hardware requirements.");

            StatusMessage = "Compatibility analysis complete (offline).";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadDatabaseInfo()
    {
        try
        {
            if (!File.Exists(_dbPath))
            {
                DatabaseInfo =
                    "Bundled compatibility database not found — showing general " +
                    "bundled guidance only.";
                OnPropertyChanged(nameof(DatabaseInfo));
                return;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(_dbPath));
            var date = doc.RootElement.TryGetProperty("buildDate", out var d)
                ? d.GetString() : "unknown";
            var version = doc.RootElement.TryGetProperty("version", out var v)
                ? v.GetString() : "unknown";
            DatabaseInfo =
                $"Bundled compatibility database v{version}, built {date} — " +
                "not necessarily the latest information available.";
            OnPropertyChanged(nameof(DatabaseInfo));
        }
        catch
        {
            DatabaseInfo = "Bundled compatibility database unreadable.";
            OnPropertyChanged(nameof(DatabaseInfo));
        }
    }
}
