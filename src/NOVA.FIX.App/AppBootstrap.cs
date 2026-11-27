using NOVA.FIX.AI.Correlation;
using NOVA.FIX.AI.Inference;
using NOVA.FIX.AI.Rag;
using NOVA.FIX.Core.Logging;
using NOVA.FIX.Core.Services;
using NOVA.FIX.Diagnostics.Monitoring;
using NOVA.FIX.Diagnostics.Probes;
using NOVA.FIX.Diagnostics.Scanner;
using NOVA.FIX.Domain.Contracts;
using NOVA.FIX.Domain.Enums;
using NOVA.FIX.Drivers;
using NOVA.FIX.Infrastructure.Data;
using NOVA.FIX.Infrastructure.Execution;
using NOVA.FIX.Localization;
using NOVA.FIX.Optimization;
using NOVA.FIX.Recovery;
using NOVA.FIX.Repair.Actions;
using NOVA.FIX.Repair.Workflows;
using NOVA.FIX.Reporting;
using NOVA.FIX.Storage;
using NOVA.FIX.Updates;
using System.IO;

namespace NOVA.FIX.App;

/// <summary>
/// Builds the full service graph shared by the running app and UI tests.
/// Everything resolves from local resources — no network services exist here.
/// </summary>
public static class AppBootstrap
{
    public static ServiceContainer BuildContainer(
        AppPaths paths, ISettingsService settings, IAppLogger logger)
    {
        var c = new ServiceContainer();

        c.RegisterInstance(paths);
        c.RegisterInstance(logger);
        c.RegisterInstance(settings);
        c.RegisterInstance(new LocalizationService());
        c.RegisterInstance(new ProcessRunner(logger));
        c.RegisterInstance(new ReportGenerator());

        var db = new DatabaseInitializer(paths.Database);
        c.RegisterInstance(db);

        var hardwareProbe = new HardwareProbe(logger);
        var windowsProbe = new WindowsProbe(logger);
        var perfMonitor = new PerformanceMonitor(logger);
        c.RegisterInstance<IHardwareProbe>(hardwareProbe);
        c.RegisterInstance<IWindowsProbe>(windowsProbe);
        c.RegisterInstance<IPerformanceMonitor>(perfMonitor);

        var knowledge = new SqliteKnowledgeBase(
            Path.Combine(paths.Database, "knowledge.db"),
            Path.Combine(AppPaths.BundledData, "knowledge", "articles.json"),
            logger);
        c.RegisterInstance<IKnowledgeBase>(knowledge);

        AiModelTier tier = settings.Current.AiModelProfile.ToLowerInvariant() switch
        {
            "lightweight" => AiModelTier.Lightweight,
            "advanced" => AiModelTier.Advanced,
            _ => AiModelTier.Balanced,
        };
        var llm = new LlamaCppLanguageModel(paths.Models, tier, logger);
        c.RegisterInstance<ILocalLanguageModel>(llm);

        var correlator = new EvidenceCorrelator(
            knowledge, llm, logger,
            allowAiInterpretation: settings.Current.AiEnabled);
        c.RegisterInstance<IDiagnosticEngine>(correlator);
        c.RegisterInstance(new RagService(knowledge, llm, logger));

        var registry = new ActionRegistry();
        c.RegisterInstance<IActionRegistry>(registry);
        c.RegisterInstance(registry);
        var verbs = new ActionVerbs(c.Resolve<ProcessRunner>(), logger);
        c.RegisterInstance(verbs);
        c.RegisterInstance<IRepairExecutor>(new RepairExecutor(
            registry, verbs, logger,
            Path.Combine(paths.Database, "novafix.db")));

        var rulesPath = Path.Combine(AppPaths.BundledData, "diagnostic-rules", "rules.json");
        var scanner = new DeepScanner(
            hardwareProbe, windowsProbe, correlator, logger, rulesPath);
        c.RegisterInstance(scanner);

        c.RegisterInstance(new StartupManager(logger));
        c.RegisterInstance(new ServiceManager(logger));
        c.RegisterInstance(new PerformanceLab(logger));
        c.RegisterInstance(new DriverIntelligence(logger,
            Path.Combine(paths.Data, "driver-repository")));
        c.RegisterInstance(new StorageGuardian(logger));
        c.RegisterInstance(new RescueManager(
            logger, paths.Snapshots, Path.Combine(paths.Database, "novafix.db")));
        c.RegisterInstance(new OfflineUpdateManager(
            logger, paths.Updates,
            Path.Combine(paths.Database, "novafix.db"),
            typeof(AppBootstrap).Assembly.GetName().Version?.ToString() ?? "2.0.0"));

        return c;
    }
}
