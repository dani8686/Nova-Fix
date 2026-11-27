using System.Windows;
using NOVA.FIX.Core.Branding;
using NOVA.FIX.Core.Mvvm;
using NOVA.FIX.Core.Services;
using NOVA.FIX.Localization;

namespace NOVA.FIX.App.ViewModels;

/// <summary>
/// Settings &amp; About: themes, language, intervals, report directory, privacy,
/// diagnostics history — and the mandatory About page with creator attribution
/// and copy-to-clipboard controls.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly LocalizationService _loc;
    private readonly AppPaths _paths;

    private string _copiedField = string.Empty;

    public SettingsViewModel(ServiceContainer c)
    {
        _settings = c.Resolve<ISettingsService>();
        _loc = c.Resolve<LocalizationService>();
        _paths = c.Resolve<AppPaths>();

        SaveCommand = new AsyncRelayCommand(async ct =>
        {
            await _settings.SaveAsync(ct);
            StatusMessage = "Settings saved locally.";
        });
        CopyCommand = new RelayCommand(() => Copy("creator"));
        CopyInstagramCommand = new RelayCommand(() => Copy("instagram"));
        CopyEmailCommand = new RelayCommand(() => Copy("email"));
        OpenReportsCommand = new RelayCommand(() =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _paths.Reports,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Cannot open folder: {ex.Message}";
            }
        });
    }

    public IReadOnlyList<string> Themes { get; } = new[] { "Dark", "Light" };
    public IReadOnlyList<string> LanguageOptions { get; } = new[] { "en", "fa" };

    // ---- About (mandatory attribution) ----

    public string AboutProduct => CreatorInformation.ProductName;
    public string AboutTagline => CreatorInformation.Tagline;
    public string AboutCategory => CreatorInformation.ProductCategory;
    public string AboutCreator => CreatorInformation.CreatorName;
    public string AboutInstagram => CreatorInformation.InstagramHandle;
    public string AboutEmail => CreatorInformation.ContactEmail;
    public string AboutAttribution => CreatorInformation.AttributionLine;

    public string CopiedField
    {
        get => _copiedField;
        private set => SetProperty(ref _copiedField, value);
    }

    public RelayCommand CopyCommand { get; }
    public RelayCommand CopyInstagramCommand { get; }
    public RelayCommand CopyEmailCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand OpenReportsCommand { get; }

    private void Copy(string target)
    {
        var text = target switch
        {
            "instagram" => CreatorInformation.InstagramHandle,
            "email" => CreatorInformation.ContactEmail,
            _ => CreatorInformation.CreatorName,
        };
        try
        {
            Clipboard.SetText(text);
            CopiedField = text;
            StatusMessage = $"Copied: {text}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Clipboard unavailable: {ex.Message}";
        }
    }

    // ---- settings bindings ----

    public string Theme
    {
        get => _settings.Current.Theme;
        set
        {
            _settings.Update(s => s.Theme = value);
            OnPropertyChanged();
        }
    }

    public string Language
    {
        get => _settings.Current.Language;
        set
        {
            _loc.Language = value;
            _settings.Update(s => s.Language = value);
            OnPropertyChanged();
        }
    }

    public int MonitoringInterval
    {
        get => _settings.Current.MonitoringIntervalSeconds;
        set
        {
            _settings.Update(s => s.MonitoringIntervalSeconds = value);
            OnPropertyChanged();
        }
    }

    public string ReportDirectory
    {
        get => string.IsNullOrWhiteSpace(_settings.Current.ReportDirectory)
            ? _paths.Reports
            : _settings.Current.ReportDirectory;
        set
        {
            _settings.Update(s => s.ReportDirectory = value);
            OnPropertyChanged();
        }
    }

    public bool CollectHistory
    {
        get => _settings.Current.CollectDiagnosticHistory;
        set
        {
            _settings.Update(s => s.CollectDiagnosticHistory = value);
            OnPropertyChanged();
        }
    }

    public bool Notifications
    {
        get => _settings.Current.ShowNotifications;
        set
        {
            _settings.Update(s => s.ShowNotifications = value);
            OnPropertyChanged();
        }
    }

    public bool RestorePointBeforeRepair
    {
        get => _settings.Current.CreateRestorePointBeforeRepair;
        set
        {
            _settings.Update(s => s.CreateRestorePointBeforeRepair = value);
            OnPropertyChanged();
        }
    }

    public int HistoryRetentionDays
    {
        get => _settings.Current.HistoryRetentionDays;
        set
        {
            _settings.Update(s => s.HistoryRetentionDays = value);
            OnPropertyChanged();
        }
    }

    public string VersionLine =>
        $"NOVA FIX v{typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "2.0.0"}";

    public string PrivacyNote =>
        "NOVA FIX performs no network communication. Diagnostic data, reports and " +
        "AI inference stay entirely on this computer. The creator's contact details " +
        "are product attribution and are never included in diagnostic reports or " +
        "transmitted anywhere.";
}
