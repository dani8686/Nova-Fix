using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace NOVA.FIX.Localization;

/// <summary>
/// Offline localization: English + Persian (RTL). Dictionaries are bundled JSON
/// resources — no satellite assemblies, no network. The UI subscribes to
/// LanguageChanged and re-reads keys via the indexer.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly Dictionary<string, Dictionary<string, string>> _tables = new();
    private string _language = "en";

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    public LocalizationService(string? resourceDirectory = null)
    {
        resourceDirectory ??= Path.Combine(AppContext.BaseDirectory, "data", "localization");
        LoadDirectory(resourceDirectory);
        if (_tables.Count == 0)
        {
            // Built-in fallback so the UI always has text even if files are missing.
            _tables["en"] = BuiltInEnglish();
            _tables["fa"] = BuiltInPersian();
        }
    }

    public string Language
    {
        get => _language;
        set
        {
            var normalized = value?.ToLowerInvariant() == "fa" ? "fa" : "en";
            if (_language == normalized) return;
            _language = normalized;
            CultureInfo.CurrentUICulture = new CultureInfo(normalized == "fa" ? "fa-IR" : "en-US");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>True when the current language reads right-to-left.</summary>
    public bool IsRightToLeft => _language == "fa";

    /// <summary>WPF FlowDirection value name for the current language.</summary>
    public string FlowDirection => IsRightToLeft ? "RightToLeft" : "LeftToRight";

    public string this[string key]
    {
        get
        {
            if (_tables.TryGetValue(_language, out var table)
                && table.TryGetValue(key, out var value))
                return value;
            if (_tables.TryGetValue("en", out var en) && en.TryGetValue(key, out var fallback))
                return fallback;
            return key; // visible missing-key marker, never crashes
        }
    }

    public IReadOnlyList<string> AvailableLanguages => new[] { "en", "fa" };

    public string DisplayName(string code) => code == "fa" ? "فارسی" : "English";

    private void LoadDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                var code = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                var table = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    File.ReadAllText(file), JsonOptions);
                if (table is not null)
                    _tables[code] = table;
            }
            catch
            {
                // corrupt dictionary skipped — built-in fallback still applies
            }
        }
    }

    private static Dictionary<string, string> BuiltInEnglish() => new()
    {
        ["app.title"] = "NOVA FIX",
        ["nav.overview"] = "System Overview",
        ["nav.scanner"] = "Deep Scanner",
        ["nav.detective"] = "AI Problem Detective",
        ["nav.repair"] = "One-Click Repair",
        ["nav.performance"] = "Performance Lab",
        ["nav.startup"] = "Startup Manager",
        ["nav.services"] = "Service Manager",
        ["nav.drivers"] = "Driver Intelligence",
        ["nav.storage"] = "Storage Guardian",
        ["nav.compatibility"] = "Windows Compatibility",
        ["nav.rescue"] = "Rescue & Restore",
        ["nav.reports"] = "Report Center",
        ["nav.autopilot"] = "NOVA Autopilot",
        ["nav.updates"] = "Offline Updates",
        ["nav.ai"] = "Local AI Settings",
        ["nav.settings"] = "Settings & About",
        ["action.scan"] = "Run Scan",
        ["action.pause"] = "Pause",
        ["action.resume"] = "Resume",
        ["action.cancel"] = "Cancel",
        ["action.approve"] = "Approve",
        ["action.reject"] = "Reject",
        ["status.ready"] = "Ready",
        ["status.scanning"] = "Scanning…",
        ["status.unavailable"] = "Unavailable",
        ["about.created"] = "Created and Developed by Danial Mousavi",
        ["about.instagram"] = "Instagram",
        ["about.email"] = "Email",
        ["about.copy"] = "Copy",
        ["about.copied"] = "Copied!",
    };

    private static Dictionary<string, string> BuiltInPersian() => new()
    {
        ["app.title"] = "نوا فیکس",
        ["nav.overview"] = "نمای کلی سیستم",
        ["nav.scanner"] = "اسکن عمیق",
        ["nav.detective"] = "کارآگاه هوش مصنوعی",
        ["nav.repair"] = "تعمیر یک‌کلیکی",
        ["nav.performance"] = "آزمایشگاه عملکرد",
        ["nav.startup"] = "مدیر راه‌اندازی",
        ["nav.services"] = "مدیر سرویس‌ها",
        ["nav.drivers"] = "هوشمندی درایور",
        ["nav.storage"] = "نگهبان ذخیره‌سازی",
        ["nav.compatibility"] = "سازگاری ویندوز",
        ["nav.rescue"] = "نجات و بازیابی",
        ["nav.reports"] = "مرکز گزارش‌ها",
        ["nav.autopilot"] = "خلبان خودکار",
        ["nav.updates"] = "به‌روزرسانی آفلاین",
        ["nav.ai"] = "تنظیمات هوش مصنوعی محلی",
        ["nav.settings"] = "تنظیمات و درباره",
        ["action.scan"] = "اجرای اسکن",
        ["action.pause"] = "توقف موقت",
        ["action.resume"] = "ادامه",
        ["action.cancel"] = "لغو",
        ["action.approve"] = "تأیید",
        ["action.reject"] = "رد",
        ["status.ready"] = "آماده",
        ["status.scanning"] = "در حال اسکن…",
        ["status.unavailable"] = "در دسترس نیست",
        ["about.created"] = "ساخته و توسعه‌یافته توسط دنیال موسوی",
        ["about.instagram"] = "اینستاگرام",
        ["about.email"] = "ایمیل",
        ["about.copy"] = "کپی",
        ["about.copied"] = "کپی شد!",
    };
}
