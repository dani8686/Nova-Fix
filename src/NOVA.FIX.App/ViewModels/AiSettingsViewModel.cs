using NOVA.FIX.Core.Mvvm;
using NOVA.FIX.Core.Services;
using NOVA.FIX.Domain.Contracts;
using NOVA.FIX.Domain.Enums;

namespace NOVA.FIX.App.ViewModels;

/// <summary>Local AI Settings: model profile, load status, resource controls.</summary>
public sealed class AiSettingsViewModel : ViewModelBase
{
    private readonly ILocalLanguageModel _llm;
    private readonly ISettingsService _settings;
    private readonly IKnowledgeBase _knowledge;
    private readonly string _modelsDir;

    private string _statusText = string.Empty;
    private string _selectedProfile;

    public AiSettingsViewModel(ServiceContainer c)
    {
        _llm = c.Resolve<ILocalLanguageModel>();
        _settings = c.Resolve<ISettingsService>();
        _knowledge = c.Resolve<IKnowledgeBase>();
        _modelsDir = c.Resolve<AppPaths>().Models;
        _selectedProfile = _settings.Current.AiModelProfile;

        LoadCommand = new AsyncRelayCommand(LoadAsync);
        UnloadCommand = new RelayCommand(() =>
        {
            _llm.Unload();
            UpdateStatus();
        });
    }

    public AsyncRelayCommand LoadCommand { get; }
    public RelayCommand UnloadCommand { get; }

    public IReadOnlyList<string> Profiles { get; } =
        new[] { "Lightweight", "Balanced", "Advanced" };

    public string SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                _settings.Update(s => s.AiModelProfile = value);
                OnPropertyChanged(nameof(ProfileHint));
            }
        }
    }

    public string ProfileHint => SelectedProfile switch
    {
        "Lightweight" => "Smallest memory footprint (~1–2 GB RAM) — fastest load, " +
                         "concise answers. Best for 8 GB systems.",
        "Advanced" => "Largest quality (~4–6 GB RAM) — slower on older CPUs. " +
                      "Best when plenty of free memory is available.",
        _ => "Balanced (~2–4 GB RAM) — recommended default for 16 GB systems " +
             "such as the Dell Precision 7520 reference machine.",
    };

    public bool AiEnabled
    {
        get => _settings.Current.AiEnabled;
        set
        {
            _settings.Update(s => s.AiEnabled = value);
            OnPropertyChanged();
        }
    }

    public bool BackgroundAnalysis
    {
        get => _settings.Current.AllowBackgroundAnalysis;
        set
        {
            _settings.Update(s => s.AllowBackgroundAnalysis = value);
            OnPropertyChanged();
        }
    }

    public int MaxContextTokens
    {
        get => _settings.Current.AiMaxContextTokens;
        set
        {
            _settings.Update(s => s.AiMaxContextTokens = value);
            OnPropertyChanged();
        }
    }

    public string KnowledgeInfo =>
        $"Knowledge base: v{_knowledge.DatabaseVersion}, " +
        $"built {_knowledge.DatabaseBuildDate:yyyy-MM-dd} — bundled offline data.";

    public string ModelsDirInfo => $"Model directory: {_modelsDir}";

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        IsBusy = true;
        StatusText = "Loading local model (CPU)…";
        try
        {
            var ok = await _llm.LoadAsync(ct);
            StatusText = ok
                ? $"Loaded: {_llm.ModelProfile}"
                : $"Unavailable: {_llm.LoadError}\n\n" +
                  "Deterministic diagnostics, repair workflows, RAG retrieval and " +
                  "reporting remain fully operational without the model.";
        }
        finally
        {
            IsBusy = false;
            UpdateStatus();
        }
    }

    public void UpdateStatus()
    {
        StatusText = _llm.IsLoaded
            ? $"Loaded: {_llm.ModelProfile}"
            : _llm.LoadError is not null
                ? $"Not loaded: {_llm.LoadError}"
                : "Not loaded. Use Load to start the bundled model.";
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(KnowledgeInfo));
    }
}
