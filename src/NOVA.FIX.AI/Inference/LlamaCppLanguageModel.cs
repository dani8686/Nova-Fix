using LLama;
using LLama.Common;
using LLama.Sampling;
using NOVA.FIX.Core.Logging;
using NOVA.FIX.Domain.Contracts;
using NOVA.FIX.Domain.Enums;

namespace NOVA.FIX.AI.Inference;

/// <summary>
/// Local language model backed by llama.cpp through LLamaSharp (MIT), CPU-first.
///
/// Design rules:
///  - The bundled baseline model must load with no download of any kind.
///  - If the native runtime or the model file is missing, or resources are
///    insufficient, LoadAsync returns false with an honest LoadError —
///    deterministic diagnostics remain fully operational and the UI explains
///    the limitation. A rule-based answer is never presented as LLM output.
///  - GPU acceleration is never required (GpuLayerCount = 0).
///  - The model NEVER gets shell access; it only produces text.
/// </summary>
public sealed class LlamaCppLanguageModel : ILocalLanguageModel, IDisposable
{
    private readonly string _modelDirectory;
    private readonly AiModelTier _tier;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _generationGate = new(1, 1);

    private LLamaWeights? _weights;
    private StatelessExecutor? _executor;
    private bool _disposed;

    public LlamaCppLanguageModel(string modelDirectory, AiModelTier tier, IAppLogger logger)
    {
        _modelDirectory = modelDirectory;
        _tier = tier;
        _logger = logger;
    }

    public bool IsLoaded => _weights is not null;

    public string? ModelProfile { get; private set; }

    public string? LoadError { get; private set; }

    /// <summary>Preferred GGUF file name per tier (packaging script stages one of these).</summary>
    public static string ExpectedFileName(AiModelTier tier) => tier switch
    {
        AiModelTier.Lightweight => "novafix-lite.gguf",
        AiModelTier.Balanced => "novafix-balanced.gguf",
        AiModelTier.Advanced => "novafix-advanced.gguf",
        _ => "novafix-balanced.gguf",
    };

    public async Task<bool> LoadAsync(CancellationToken ct = default)
    {
        if (IsLoaded) return true;

        var modelPath = ResolveModelPath();
        if (modelPath is null)
        {
            LoadError =
                $"No bundled model found in '{_modelDirectory}'. " +
                "Deterministic diagnostics remain fully available; AI interpretation " +
                "is disabled until the offline package provides a model.";
            _logger.Log(LogLevel.Warn, "Llm", LoadError);
            return false;
        }

        try
        {
            var contextSize = _tier switch
            {
                AiModelTier.Lightweight => 2048u,
                AiModelTier.Advanced => 4096u,
                _ => 3072u,
            };

            var parameters = new ModelParams(modelPath)
            {
                ContextSize = contextSize,
                BatchSize = 512,
                UBatchSize = 512,
                // CPU-first: never require an NVIDIA GPU (reference machine:
                // Dell Precision 7520, 7th-gen Core i7, 16 GB RAM).
                GpuLayerCount = 0,
                Threads = Math.Max(2, Environment.ProcessorCount - 2),
                BatchThreads = Math.Max(2, Environment.ProcessorCount - 2),
                Embeddings = false,
            };

            _weights = await LLamaWeights.LoadFromFileAsync(parameters, ct)
                .ConfigureAwait(false);
            _executor = new StatelessExecutor(_weights, parameters);

            ModelProfile = $"{_tier} ({Path.GetFileName(modelPath)}, " +
                           $"{_weights.ParameterCount / 1_000_000_000.0:0.#}B params, CPU)";
            LoadError = null;
            _logger.Log(LogLevel.Info, "Llm", $"Model loaded: {ModelProfile}");
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Resource exhaustion, corrupt file, missing native runtime — all
            // degrade honestly without affecting deterministic diagnostics.
            UnloadCore();
            LoadError = ex switch
            {
                OutOfMemoryException =>
                    "Not enough free memory to load the AI model at this tier. " +
                    "Try the Lightweight profile in Local AI Settings. " +
                    "Deterministic diagnostics remain fully operational.",
                DllNotFoundException =>
                    "The llama.cpp native runtime is missing from this installation. " +
                    "Deterministic diagnostics remain fully operational.",
                FileNotFoundException =>
                    "The bundled model file is missing or unreadable. " +
                    "Deterministic diagnostics remain fully operational.",
                _ =>
                    $"The model could not be loaded ({ex.GetType().Name}). " +
                    "Deterministic diagnostics remain fully operational.",
            };
            _logger.Log(LogLevel.Warn, "Llm", LoadError, ex);
            return false;
        }
    }

    public async Task<string?> GenerateAsync(
        string prompt, int maxTokens = 512, CancellationToken ct = default)
    {
        if (_executor is null) return null;
        await _generationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var inferenceParams = new InferenceParams
            {
                MaxTokens = maxTokens,
                AntiPrompts = Array.Empty<string>(),
                SamplingPipeline = new DefaultSamplingPipeline(),
                OverflowStrategy = ContextOverflowStrategy.TruncateAndReprefill,
            };

            var sb = new System.Text.StringBuilder(Math.Min(maxTokens * 6, 4096));
            await foreach (var piece in _executor
                               .InferAsync(prompt, inferenceParams, ct)
                               .ConfigureAwait(false))
            {
                sb.Append(piece);
                if (sb.Length > 16_384) break; // bounded output
            }

            var text = sb.ToString().Trim();
            return text.Length == 0 ? null : text;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Warn, "Llm", "Generation failed.", ex);
            return null;
        }
        finally
        {
            _generationGate.Release();
        }
    }

    public void Unload() => UnloadCore();

    private void UnloadCore()
    {
        _executor = null;
        _weights?.Dispose();
        _weights = null;
        ModelProfile = null;
    }

    private string? ResolveModelPath()
    {
        var preferred = Path.Combine(_modelDirectory, ExpectedFileName(_tier));
        if (File.Exists(preferred)) return preferred;
        if (!Directory.Exists(_modelDirectory)) return null;

        // Accept any staged .gguf (packaging script may ship a renamed model).
        return Directory.EnumerateFiles(_modelDirectory, "*.gguf")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnloadCore();
        _generationGate.Dispose();
    }
}
