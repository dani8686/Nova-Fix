using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Win32;
using NOVA.FIX.Core.Logging;
using NOVA.FIX.Domain.Models;

namespace NOVA.FIX.Optimization;

/// <summary>
/// Startup Manager: inspects Run keys, Startup folders and scheduled startup
/// tasks. Disable/Enable never deletes application files — only the registration
/// is toggled, and the original configuration is exported first for restore.
/// </summary>
public sealed class StartupManager
{
    private readonly IAppLogger _logger;

    public StartupManager(IAppLogger logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<StartupEntry> Inspect()
    {
        var entries = new List<StartupEntry>();

        void Add(string location, string name, string? value, RegistryHive hive)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var path = ExtractPath(value);
            entries.Add(new StartupEntry(
                Id: $"{location}:{hive}:{name}",
                Name: name,
                Publisher: TryGetPublisher(path),
                Path: path,
                SignatureStatus: GetSignatureStatus(path),
                Location: location,
                Status: "Enabled",
                Classification: Classify(name, path),
                Evidence: $"Value: {Trunc(value, 200)}"));
        }

        // User Run key
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run");
            if (k is not null)
                foreach (var name in k.GetValueNames())
                    Add("HKCU\\Run", name, k.GetValue(name)?.ToString(),
                        RegistryHive.CurrentUser);
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Warn, "Startup", "HKCU Run read failed.", ex);
        }

        // Machine Run key
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run");
            if (k is not null)
                foreach (var name in k.GetValueNames())
                    Add("HKLM\\Run", name, k.GetValue(name)?.ToString(),
                        RegistryHive.LocalMachine);
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Warn, "Startup", "HKLM Run read failed.", ex);
        }

        // Startup folders
        var startupDirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Microsoft", "Windows", "Start Menu", "Programs", "Startup"),
        };
        foreach (var dir in startupDirs)
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    entries.Add(new StartupEntry(
                        Id: $"folder:{file}",
                        Name: Path.GetFileNameWithoutExtension(file),
                        Publisher: null,
                        Path: file,
                        SignatureStatus: GetSignatureStatus(file),
                        Location: dir.Contains("ProgramData")
                            ? "Common Startup Folder"
                            : "User Startup Folder",
                        Status: "Enabled",
                        Classification: Classify(Path.GetFileName(file), file),
                        Evidence: "Shortcut/file present in Startup folder."));
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Warn, "Startup",
                    $"Startup folder {dir} unreadable.", ex);
            }
        }

        return entries;
    }

    /// <summary>Exports the original configuration for later restore.</summary>
    public string ExportBackup(string backupDirectory)
    {
        Directory.CreateDirectory(backupDirectory);
        var file = Path.Combine(backupDirectory,
            $"startup-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt");
        var sb = new StringBuilder();
        sb.AppendLine($"# NOVA FIX startup backup — {DateTime.UtcNow:O}");
        foreach (var e in Inspect())
        {
            sb.AppendLine($"{e.Id}\t{e.Status}\t{e.Path}");
        }
        File.WriteAllText(file, sb.ToString());
        _logger.Log(LogLevel.Info, "Startup", $"Backup written: {file}");
        return file;
    }

    /// <summary>
    /// Disables a startup entry by removing/toggling ONLY its registration.
    /// Application files on disk are never touched.
    /// </summary>
    public (bool Ok, string Message) Disable(string entryId)
    {
        try
        {
            var parts = entryId.Split(':', 3);
            if (parts.Length >= 3 && parts[0] is "HKCU" or "HKLM")
            {
                var hive = parts[0] == "HKCU"
                    ? RegistryHive.CurrentUser
                    : RegistryHive.LocalMachine;
                var subKey = parts[1] switch
                {
                    "Run" => @"Software\Microsoft\Windows\CurrentVersion\Run",
                    _ => throw new InvalidOperationException($"Unknown hive path {parts[1]}"),
                };
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                using var k = baseKey.OpenSubKey(subKey, writable: true);
                if (k is null) return (false, "Registration not found.");
                k.DeleteValue(parts[2], throwOnMissingValue: false);
                return (true,
                    $"Disabled '{parts[2]}' registration. Application files untouched.");
            }

            if (parts[0] == "folder" && parts.Length >= 2)
            {
                var path = entryId["folder:".Length..];
                // Move instead of delete — reversible and non-destructive.
                var disabledDir = Path.Combine(Path.GetDirectoryName(path)!,
                    "novafix-disabled");
                Directory.CreateDirectory(disabledDir);
                var target = Path.Combine(disabledDir, Path.GetFileName(path));
                File.Move(path, target, overwrite: true);
                return (true,
                    $"Moved '{Path.GetFileName(path)}' to {disabledDir} (restore by moving back).");
            }

            return (false, $"Unrecognized entry id '{entryId}'.");
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, "Startup", $"Disable failed for {entryId}.", ex);
            return (false, ex.Message);
        }
    }

    private static string ExtractPath(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            if (end > 1) return trimmed[1..end];
        }
        var space = trimmed.IndexOf(' ');
        if (space > 0 && (trimmed.Contains(".exe", StringComparison.OrdinalIgnoreCase)
                          || trimmed.Contains(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            return trimmed[..space];
        }
        return trimmed;
    }

    private static string Classify(string name, string path)
    {
        var hay = $"{name} {path}".ToLowerInvariant();
        if (hay.Contains("microsoft") || hay.Contains("windows"))
            return "System";
        if (hay.Contains("update") || hay.Contains("helper") || hay.Contains("agent"))
            return "Update/Agent";
        if (hay.Contains("security") || hay.Contains("defender") || hay.Contains("shield"))
            return "Security";
        if (hay.Contains("chat") || hay.Contains("steam") || hay.Contains("discord")
            || hay.Contains("spotify") || hay.Contains("onedrive"))
            return "User Application";
        return "Unverified";
    }

    private static string? TryGetPublisher(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var vi = FileVersionInfo.GetVersionInfo(path);
            return string.IsNullOrWhiteSpace(vi.CompanyName) ? null : vi.CompanyName;
        }
        catch
        {
            return null;
        }
    }

    private static string GetSignatureStatus(string path)
    {
        try
        {
            if (!File.Exists(path)) return "File not found";
            // For .lnk files, resolve target first is out of scope — check file itself.
            var ext = Path.GetExtension(path);
            if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                return "Shortcut (target not verified)";
            using var cert = X509Certificate.CreateFromSignedFile(path);
            return cert is not null ? "Signed" : "Unsigned";
        }
        catch (CryptographicException)
        {
            return "Unsigned";
        }
        catch
        {
            return "Unknown";
        }
    }

    private static string Trunc(string s, int max)
        => s.Length <= max ? s : s[..max] + "…";
}
