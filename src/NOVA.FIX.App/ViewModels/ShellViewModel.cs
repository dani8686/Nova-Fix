using System.Windows;
using NOVA.FIX.App.Views;
using NOVA.FIX.Core.Branding;
using NOVA.FIX.Core.Mvvm;
using NOVA.FIX.Core.Services;
using NOVA.FIX.Localization;

namespace NOVA.FIX.App.ViewModels;

/// <summary>
/// Shell view-model: navigation between the 16 modules, theme/language
/// switching, status line and version/creator display.
/// </summary>
public sealed class ShellViewModel : ViewModelBase
{
    private readonly ServiceContainer _container;
    private readonly ISettingsService _settings;
    private readonly LocalizationService _loc;

    private object? _currentPage;
    private string _currentHeader = "System Overview";
    private string _currentMode = "Module 1 / 16";
    private string _themeToggleLabel = "☀ Light";
    private string _languageToggleLabel = "فارسی";
    private string _statusMessage = "Ready — fully offline";

    private readonly Dictionary<string, (string Header, string Mode, Func<object> Factory)>
        _pages = new(StringComparer.OrdinalIgnoreCase);

    public ShellViewModel(ServiceContainer container)
    {
        _container = container;
        _settings = container.Resolve<ISettingsService>();
        _loc = container.Resolve<LocalizationService>();

        Title = CreatorInformation.ProductName;
        Tagline = CreatorInformation.Tagline;
        CreatorLine = CreatorInformation.AttributionLine + "\n" +
                      CreatorInformation.InstagramHandle + " · " +
                      CreatorInformation.ContactEmail;
        VersionLine = $"v{typeof(ShellViewModel).Assembly.GetName().Version?.ToString(3) ?? "2.0.0"}" +
                      " · " + CreatorInformation.ProductName;

        RegisterPages();

        NavigateCommand = new RelayCommand(Navigate);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        ToggleLanguageCommand = new RelayCommand(ToggleLanguage);

        // restore last opened module
        var last = _settings.Current.LastOpenedModule;
        Navigate(last is not null && _pages.ContainsKey(last) ? last : "overview");

        _loc.LanguageChanged += (_, _) =>
        {
            LanguageToggleLabel = _loc.Language == "fa" ? "English" : "فارسی";
            OnPropertyChanged(nameof(FlowDirection));
            RefreshCurrentHeader();
        };

        ApplyThemeLabels();
    }

    public string Title { get; }
    public string Tagline { get; }
    public string CreatorLine { get; }
    public string VersionLine { get; }

    public RelayCommand NavigateCommand { get; }
    public RelayCommand ToggleThemeCommand { get; }
    public RelayCommand ToggleLanguageCommand { get; }

    public string FlowDirection => _loc.IsRightToLeft
        ? nameof(System.Windows.FlowDirection.RightToLeft)
        : nameof(System.Windows.FlowDirection.LeftToRight);

    public object? CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public string CurrentHeader
    {
        get => _currentHeader;
        private set => SetProperty(ref _currentHeader, value);
    }

    public string CurrentMode
    {
        get => _currentMode;
        private set => SetProperty(ref _currentMode, value);
    }

    public string ThemeToggleLabel
    {
        get => _themeToggleLabel;
        private set => SetProperty(ref _themeToggleLabel, value);
    }

    public string LanguageToggleLabel
    {
        get => _languageToggleLabel;
        private set => SetProperty(ref _languageToggleLabel, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    private void RegisterPages()
    {
        void Add(string key, string header, string mode, Func<object> factory)
            => _pages[key] = (header, mode, factory);

        Add("overview", "System Overview", "Module 1 / 16",
            () => new OverviewViewModel(_container));
        Add("scanner", "Deep Scanner", "Module 2 / 16",
            () => new ScannerViewModel(_container));
        Add("detective", "AI Problem Detective", "Module 3 / 16",
            () => new DetectiveViewModel(_container));
        Add("repair", "One-Click Repair", "Module 4 / 16",
            () => new RepairViewModel(_container));
        Add("performance", "Performance Lab", "Module 5 / 16",
            () => new PerformanceViewModel(_container));
        Add("startup", "Startup Manager", "Module 6 / 16",
            () => new StartupViewModel(_container));
        Add("services", "Service Manager", "Module 7 / 16",
            () => new ServicesViewModel(_container));
        Add("drivers", "Driver Intelligence", "Module 8 / 16",
            () => new DriversViewModel(_container));
        Add("storage", "Storage Guardian", "Module 9 / 16",
            () => new StorageViewModel(_container));
        Add("compatibility", "Windows Compatibility", "Module 10 / 16",
            () => new CompatibilityViewModel(_container));
        Add("rescue", "Rescue & Restore", "Module 11 / 16",
            () => new RescueViewModel(_container));
        Add("reports", "Report Center", "Module 12 / 16",
            () => new ReportsViewModel(_container));
        Add("autopilot", "NOVA Autopilot", "Module 13 / 16",
            () => new AutopilotViewModel(_container));
        Add("updates", "Offline Updates", "Module 14 / 16",
            () => new UpdatesViewModel(_container));
        Add("ai", "Local AI Settings", "Module 15 / 16",
            () => new AiSettingsViewModel(_container));
        Add("settings", "Settings & About", "Module 16 / 16",
            () => new SettingsViewModel(_container));
    }

    private void Navigate(object? parameter)
    {
        var key = parameter as string;
        if (key is null || !_pages.TryGetValue(key, out var page)) return;

        CurrentHeader = page.Header;
        CurrentMode = page.Mode;
        CurrentPage = page.Factory();
        _settings.Update(s => s.LastOpenedModule = key);
        StatusMessage = _loc["status.ready"] + " — " + page.Header;
    }

    private void RefreshCurrentHeader()
    {
        // Re-create the page so localized strings re-resolve.
        var key = _settings.Current.LastOpenedModule;
        if (key is not null && _pages.TryGetValue(key, out var page))
        {
            CurrentHeader = page.Header;
            CurrentPage = page.Factory();
        }
    }

    private void ToggleTheme()
    {
        var isDark = _settings.Current.Theme != "Light";
        var next = isDark ? "Light" : "Dark";
        _settings.Update(s => s.Theme = next);

        var themeUri = new Uri($"Themes/{next}.xaml", UriKind.Relative);
        var newDict = new ResourceDictionary { Source = themeUri };

        var app = Application.Current;
        var merged = app.Resources.MergedDictionaries;
        // Replace the theme dictionary (index 0 = theme, 1 = controls).
        merged[0] = newDict;
        ApplyThemeLabels();
    }

    private void ToggleLanguage()
    {
        var next = _loc.Language == "en" ? "fa" : "en";
        _loc.Language = next;
        _settings.Update(s => s.Language = next);
        LanguageToggleLabel = next == "fa" ? "English" : "فارسی";
        OnPropertyChanged(nameof(FlowDirection));
        RefreshCurrentHeader();
        StatusMessage = next == "fa" ? "زبان فارسی (راست‌به‌چپ) فعال شد"
            : "English language enabled";
    }

    private void ApplyThemeLabels()
        => ThemeToggleLabel = _settings.Current.Theme == "Light"
            ? "🌙 Dark"
            : "☀ Light";
}
