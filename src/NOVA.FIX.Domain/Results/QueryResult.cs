namespace NOVA.FIX.Domain.Results;

/// <summary>
/// Result of a single isolated probe. Every hardware/software query is wrapped in
/// this type so that one failed query never aborts a complete diagnostic scan.
/// </summary>
public sealed class QueryResult<T>
{
    public required string ProbeName { get; init; }
    public bool Success { get; init; }
    public T? Value { get; init; }
    public string? Error { get; init; }
    public string? Method { get; init; }
    public TimeSpan Duration { get; init; }

    public static QueryResult<T> Ok(string probeName, T value, string? method = null, TimeSpan duration = default)
        => new() { ProbeName = probeName, Success = true, Value = value, Method = method, Duration = duration };

    public static QueryResult<T> Fail(string probeName, string error, string? method = null, TimeSpan duration = default)
        => new() { ProbeName = probeName, Success = false, Error = error, Method = method, Duration = duration };
}

/// <summary>Result of executing a repair/optimization action (never faked).</summary>
public sealed class OperationResult
{
    public required string ActionId { get; init; }
    public Domain.Enums.OperationStatus Status { get; init; }
    public int? ExitCode { get; init; }
    public string Output { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public bool? VerificationPassed { get; init; }
    public string? VerificationDetail { get; init; }

    public bool IsSuccess => Status == Domain.Enums.OperationStatus.Succeeded;
}
