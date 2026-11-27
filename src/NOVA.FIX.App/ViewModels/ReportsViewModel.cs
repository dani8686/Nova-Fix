using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using NOVA.FIX.Core.Mvvm;
using NOVA.FIX.Core.Services;
using NOVA.FIX.Diagnostics.Scanner;
using NOVA.FIX.Domain.Models;
using NOVA.FIX.Domain.Results;
using NOVA.FIX.Reporting;

namespace NOVA.FIX.App.ViewModels;

/// <summary>Report Center view-model: generate TXT/HTML/JSON/CSV locally.</summary>
public sealed class ReportsViewModel : ViewModelBase
{
    private readonly DeepScanner _scanner;
    private readonly ReportGenerator _generator;
    private readonly AppPaths _paths;
    private readonly Domain.Contracts.IKnowledgeBase _knowledge;
    private readonly Domain.Contracts.ILocalLanguageModel _llm;

    private string _selectedFormat = "HTML";
    private string _lastGeneratedFile = string.Empty;

    public ReportsViewModel(ServiceContainer c)
    {
        _scanner = c.Resolve<DeepScanner>();
        _generator = c.Resolve<ReportGenerator>();
        _paths = c.Resolve<AppPaths>();
        _knowledge = c.Resolve<Domain.Contracts.IKnowledgeBase>();
        _llm = c.Resolve<Domain.Contracts.ILocalLanguageModel>();

        foreach (var f in new[] { "TXT", "HTML", "JSON", "CSV" }) Formats.Add(f);
        GenerateCommand = new AsyncRelayCommand(GenerateAsync);
    }

    public AsyncRelayCommand GenerateCommand { get; }

    public ObservableCollection<string> Formats { get; } = new();
    public ObservableCollection<string> Preview { get; } = new();

    public string SelectedFormat
    {
        get => _selectedFormat;
        set => SetProperty(ref _selectedFormat, value);
    }

    public string LastGeneratedFile
    {
        get => _lastGeneratedFile;
        private set => SetProperty(ref _lastGeneratedFile, value);
    }

    private async Task GenerateAsync(CancellationToken ct)
    {
        IsBusy = true;
        try
        {
            StatusMessage = "Scanning to collect report data…";
            var scan = await _scanner.ScanAsync(null, ct);

            var payload = new ReportPayload(
                Metadata: new ReportMetadata(
                    GeneratedBy: "NOVA FIX",
                    ProductVersion: typeof(ReportsViewModel).Assembly
                        .GetName().Version?.ToString() ?? "2.0.0",
                    AiModelVersion: _llm.IsLoaded
                        ? _llm.ModelProfile ?? "loaded"
                        : "not loaded (deterministic engine active)",
                    KnowledgeDbVersion: _knowledge.DatabaseVersion,
                    RulesDbVersion: "1 (2026-11-26)",
                    GeneratedAt: DateTime.UtcNow,
                    MachineAlias: Environment.MachineName),
                Hardware: scan.Hardware,
                Windows: scan.Windows,
                Findings: scan.Findings,
                ActionsPerformed: Array.Empty<OperationResult>(),
                RemainingIssues: scan.Findings.Select(f => f.Title).ToList());

            var format = SelectedFormat switch
            {
                "TXT" => ReportFormat.Txt,
                "JSON" => ReportFormat.Json,
                "CSV" => ReportFormat.Csv,
                _ => ReportFormat.Html,
            };

            var file = await Task.Run(
                () => _generator.Generate(payload, format, _paths.Reports), ct);
            LastGeneratedFile = file;

            Preview.Clear();
            try
            {
                var lines = await File.ReadAllLinesAsync(file, ct);
                foreach (var line in lines.Take(40)) Preview.Add(line);
            }
            catch
            {
                // binary-ish content is fine to skip
            }

            StatusMessage = $"Report written locally: {file} (never transmitted).";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
