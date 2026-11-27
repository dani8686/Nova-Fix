using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using NOVA.FIX.Core.Mvvm;
using NOVA.FIX.Core.Services;
using NOVA.FIX.Storage;

namespace NOVA.FIX.App.ViewModels;

/// <summary>Storage Guardian view-model: disks, volumes, cleanup candidates.</summary>
public sealed class StorageViewModel : ViewModelBase
{
    private readonly StorageGuardian _guardian;
    private CleanupCandidate? _selected;
    private long _selectedTotalBytes;

    public StorageViewModel(ServiceContainer c)
    {
        _guardian = c.Resolve<StorageGuardian>();
        ScanCommand = new AsyncRelayCommand(ScanAsync);
        CleanupCommand = new AsyncRelayCommand(CleanupAsync,
            () => Candidates.Any(c => c.Selected) && !IsBusy);
    }

    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand CleanupCommand { get; }

    public ObservableCollection<VolumeRow> Volumes { get; } = new();
    public ObservableCollection<DiskRow> Disks { get; } = new();
    public ObservableCollection<CandidateRow> Candidates { get; } = new();

    public string SenseText { get; private set; } = string.Empty;

    private async Task ScanAsync(CancellationToken ct)
    {
        IsBusy = true;
        try
        {
            var volumes = await Task.Run(() => _guardian.Volumes(), ct);
            var disks = await Task.Run(() => _guardian.Disks(), ct);
            var smart = await Task.Run(() => _guardian.SmartByDisk(), ct);
            var temp = await Task.Run(
                () => _guardian.TempCandidates(TimeSpan.FromHours(24), ct), ct);

            Volumes.Clear();
            foreach (var v in volumes)
            {
                Volumes.Add(new VolumeRow(
                    v.Mount, v.Label, v.FileSystem,
                    $"{v.SizeBytes / 1e9:0.0} GB",
                    $"{v.FreeBytes / 1e9:0.0} GB",
                    v.SizeBytes > 0 && (double)v.FreeBytes / v.SizeBytes < 0.1
                        ? "Low space" : "OK"));
            }

            Disks.Clear();
            foreach (var d in disks)
            {
                Disks.Add(new DiskRow(d.Model,
                    d.IsSsd ? "SSD" : "HDD",
                    d.Interface,
                    $"{d.SizeBytes / 1e9:0.0} GB",
                    smart.Count > 0 ? "SMART present" : "SMART unavailable (honest)"));
            }

            Candidates.Clear();
            long total = 0;
            foreach (var t in temp.Take(300))
            {
                total += t.SizeBytes;
                Candidates.Add(new CandidateRow(t));
            }
            _selectedTotalBytes = total;

            SenseText = _guardian.StorageSenseStatus();
            StatusMessage =
                $"{Candidates.Count} cleanup candidates listed with exact paths " +
                $"({total / 1e6:0.0} MB). Nothing is deleted without confirmation.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        var chosen = Candidates.Where(c => c.Selected).ToList();
        if (chosen.Count == 0)
        {
            StatusMessage = "Select candidates first.";
            return;
        }

        var preview = string.Join("\n",
            chosen.Take(15).Select(c => $"{c.FullPath}  ({c.SizeBytes / 1e6:0.0} MB)"));
        var more = chosen.Count > 15 ? $"\n…and {chosen.Count - 15} more" : string.Empty;

        var confirm = MessageBox.Show(
            $"Permanently delete {chosen.Count} item(s) " +
            $"({chosen.Sum(c => c.SizeBytes) / 1e6:0.0} MB)?\n\n" +
            $"Only approved temp locations are allowed.\n\n{preview}{more}",
            "NOVA FIX — Confirm cleanup",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            StatusMessage = "Cleanup cancelled — nothing was deleted.";
            return;
        }

        IsBusy = true;
        try
        {
            long freed = 0;
            int done = 0;
            foreach (var row in chosen)
            {
                ct.ThrowIfCancellationRequested();
                var (ok, err) = ActionVerbsTargetValidation(row.FullPath);
                if (!ok)
                {
                    StatusMessage = err!;
                    continue;
                }
                try
                {
                    if (File.Exists(row.FullPath))
                    {
                        freed += new FileInfo(row.FullPath).Length;
                        File.Delete(row.FullPath);
                        done++;
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"{row.FullPath}: {ex.Message}";
                }
            }
            StatusMessage = $"Removed {done} item(s), freed {freed / 1e6:0.0} MB.";
            await ScanAsync(ct);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static (bool Ok, string? Error) ActionVerbsTargetValidation(string path)
    {
        // Mirrors Repair's safety gate: only temp/cache locations.
        var full = Path.GetFullPath(path);
        var tempRoots = new[]
        {
            Path.GetTempPath(),
            Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "Temp"),
            Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.Windows), "Temp"),
        };
        var allowed = tempRoots.Any(t =>
        {
            var te = Path.GetFullPath(t).TrimEnd('\\', '/');
            return full.StartsWith(te + Path.DirectorySeparatorChar,
                       StringComparison.OrdinalIgnoreCase)
                   || string.Equals(full, te, StringComparison.OrdinalIgnoreCase);
        });
        return allowed
            ? (true, null)
            : (false, $"Refusing '{full}' — outside approved temp locations.");
    }

    public sealed record VolumeRow(
        string Mount, string Label, string FileSystem, string Size, string Free, string Status);

    public sealed record DiskRow(
        string Model, string Kind, string Interface, string Size, string Smart);

    public sealed class CandidateRow
    {
        public CandidateRow(CleanupCandidate c)
        {
            FullPath = c.Path;
            SizeBytes = c.SizeBytes;
            Category = c.Category;
        }

        public string FullPath { get; }
        public long SizeBytes { get; }
        public string Category { get; }
        public bool Selected { get; set; }
        public string SizeDisplay => $"{SizeBytes / 1e6:0.0} MB";
    }
}
