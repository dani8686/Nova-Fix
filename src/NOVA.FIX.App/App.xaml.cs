using System.Windows;
using NOVA.FIX.App.ViewModels;
using NOVA.FIX.App.Views;
using NOVA.FIX.Core.Branding;
using NOVA.FIX.Core.Logging;
using NOVA.FIX.Core.Services;
using NOVA.FIX.Domain.Contracts;
using NOVA.FIX.Infrastructure.Data;
using NOVA.FIX.Localization;

namespace NOVA.FIX.App;

/// <summary>
/// Application composition root. Wires every engine from local resources only —
/// no network services exist anywhere in this graph.
/// </summary>
public partial class App : Application
{
    private ServiceContainer? _container;
    private FileLogger? _logger;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            _logger?.Log(LogLevel.Error, "App",
                "Unhandled UI exception.", args.Exception);
            MessageBox.Show(
                $"An unexpected error occurred and was logged.\n\n{args.Exception.Message}",
                CreatorInformation.ProductName,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        try
        {
            var paths = new AppPaths();
            _logger = new FileLogger(paths.Logs);
            _logger.Log(LogLevel.Info, "App",
                $"{CreatorInformation.ProductName} starting — " +
                $"{CreatorInformation.AttributionLine}");

            var settings = new SettingsService(paths);
            await settings.LoadAsync();

            _container = AppBootstrap.BuildContainer(paths, settings, _logger);

            // Initialize offline data stores before the window shows.
            var db = new DatabaseInitializer(paths.Database);
            db.EnsureMainDatabase();
            db.EnsureKnowledgeDatabase();

            var knowledge = _container.Resolve<IKnowledgeBase>();
            await knowledge.InitializeAsync();

            var llm = _container.Resolve<ILocalLanguageModel>();
            _ = Task.Run(async () =>
            {
                var loaded = await llm.LoadAsync();
                _logger.Log(loaded ? LogLevel.Info : LogLevel.Warn, "App",
                    loaded
                        ? $"Local AI ready: {llm.ModelProfile}"
                        : $"Local AI unavailable: {llm.LoadError}");
            });

            var localization = _container.Resolve<LocalizationService>();
            localization.Language = settings.Current.Language;

            // Apply persisted theme (Dark default).
            if (settings.Current.Theme.Equals("Light",
                    StringComparison.OrdinalIgnoreCase))
            {
                var light = new ResourceDictionary
                {
                    Source = new Uri("Themes/Light.xaml", UriKind.Relative),
                };
                Resources.MergedDictionaries.Add(light);
            }

            var mainWindow = new MainWindow(_container);
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            _logger?.Log(LogLevel.Error, "App", "Startup failed.", ex);
            MessageBox.Show(
                $"Failed to start {CreatorInformation.ProductName}:\n{ex.Message}\n\n" +
                "Deterministic components may still run from a fresh install.",
                CreatorInformation.ProductName,
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Log(LogLevel.Info, "App", "Shutting down.");
        try
        {
            _container?.Resolve<ILocalLanguageModel>().Unload();
        }
        catch
        {
            // best effort
        }
        _logger?.Dispose();
        base.OnExit(e);
    }
}
