namespace NOVA.FIX.Domain.Enums;

/// <summary>Finding severity levels used across diagnostics and AI correlation.</summary>
public enum FindingSeverity
{
    Informational = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4,
}

/// <summary>Confidence that a finding is real, given the collected evidence.</summary>
public enum ConfidenceLevel
{
    Speculative = 0,
    Plausible = 1,
    Likely = 2,
    High = 3,
    Confirmed = 4,
}

/// <summary>Where a conclusion came from — the AI must distinguish these honestly.</summary>
public enum EvidenceBasis
{
    SystemEvidence,
    ReferenceDocumentation,
    RuleBasedInference,
    ProbabilisticInterpretation,
}

/// <summary>Risk of executing a repair action.</summary>
public enum ActionRisk
{
    Safe = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Destructive = 4,
}

/// <summary>Autopilot operation modes.</summary>
public enum AutopilotMode
{
    Safe,
    Guided,
    Advanced,
}

/// <summary>Lifecycle state of a repair/optimization operation.</summary>
public enum OperationStatus
{
    NotStarted,
    Running,
    Succeeded,
    Failed,
    PartiallySucceeded,
    Cancelled,
    Skipped,
}

/// <summary>Local AI model profile tiers.</summary>
public enum AiModelTier
{
    Lightweight,
    Balanced,
    Advanced,
}
