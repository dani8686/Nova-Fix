using System.Collections.ObjectModel;
using System.Windows;
using NOVA.FIX.AI.Rag;
using NOVA.FIX.Core.Mvvm;
using NOVA.FIX.Core.Services;
using NOVA.FIX.Domain.Contracts;
using NOVA.FIX.Domain.Models;

namespace NOVA.FIX.App.ViewModels;

/// <summary>AI Problem Detective: findings + offline RAG question box.</summary>
public sealed class DetectiveViewModel : ViewModelBase
{
    private readonly IDiagnosticEngine _engine;
    private readonly RagService _rag;
    private readonly DeepScanner _scanner;

    private DiagnosticFinding? _selected;
    private string _question = string.Empty;
    private string _answer = string.Empty;
    private string _answerBasis = string.Empty;

    public DetectiveViewModel(ServiceContainer c)
    {
        _engine = c.Resolve<IDiagnosticEngine>();
        _rag = c.Resolve<RagService>();
        _scanner = c.Resolve<DeepScanner>();

        LoadEvidenceCommand = new AsyncRelayCommand(LoadAsync);
        AskCommand = new AsyncRelayCommand(AskAsync, () => !string.IsNullOrWhiteSpace(Question));
    }

    public AsyncRelayCommand LoadEvidenceCommand { get; }
    public AsyncRelayCommand AskCommand { get; }

    public ObservableCollection<DiagnosticFinding> Findings { get; } = new();

    public DiagnosticFinding? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    public string Question
    {
        get => _question;
        set
        {
            if (SetProperty(ref _question, value))
                AskCommand.RaiseCanExecuteChanged();
        }
    }

    public string Answer
    {
        get => _answer;
        private set => SetProperty(ref _answer, value);
    }

    public string AnswerBasis
    {
        get => _answerBasis;
        private set => SetProperty(ref _answerBasis, value);
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        StatusMessage = "Collecting evidence…";
        var scan = await _scanner.ScanAsync(null, ct);
        Findings.Clear();
        foreach (var f in scan.Findings) Findings.Add(f);
        StatusMessage = $"{scan.Findings.Count} finding(s) from evidence " +
                        $"({scan.FailedProbeCount} probe failures reported honestly).";
        if (Findings.Count > 0) Selected = Findings[0];
    }

    private async Task AskAsync(CancellationToken ct)
    {
        StatusMessage = "Searching local knowledge base…";
        var answer = await _rag.AskAsync(Question, null, ct);
        Answer = answer.Answer;
        AnswerBasis =
            $"Basis: {answer.PrimaryBasis} · " +
            (answer.UsedLanguageModel
                ? $"local LLM ({answer.ModelProfile})"
                : "retrieved passages") +
            (answer.HonestFallbackNote is null
                ? string.Empty
                : $" · {answer.HonestFallbackNote}") +
            (answer.Citations.Count > 0
                ? " · sources: " + string.Join(", ", answer.Citations.Select(c => c.Article.Id))
                : string.Empty);
        StatusMessage = "Answer generated fully offline.";
    }
}
