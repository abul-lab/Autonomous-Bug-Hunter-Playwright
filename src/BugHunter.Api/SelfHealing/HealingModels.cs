namespace BugHunter.Api.SelfHealing;

public class IntentSpec
{
    public string OriginalIntent { get; set; } = string.Empty;
    public string FailedLocator { get; set; } = string.Empty;
    public string PageUrl { get; set; } = string.Empty;
    public string? ActionType { get; set; } = "click"; // click, fill, select
    public string? TargetValue { get; set; }
    public string? AxTreeSnippet { get; set; }
}

public class CandidateLocator
{
    public string Selector { get; set; } = string.Empty;
    public string Strategy { get; set; } = "role"; // role, test-id, text, css
    public double MatchScore { get; set; }
    public double StabilityScore { get; set; }
    public double CombinedConfidence => Math.Round((MatchScore * 0.6) + (StabilityScore * 0.4), 2);
    public string Rationale { get; set; } = string.Empty;
}

public class HealedPatch
{
    public string SpecFilePath { get; set; } = string.Empty;
    public string OriginalCode { get; set; } = string.Empty;
    public string PatchedCode { get; set; } = string.Empty;
    public string FailedLocator { get; set; } = string.Empty;
    public CandidateLocator SelectedCandidate { get; set; } = new();
    public string UnifiedDiff { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string PrTitle { get; set; } = string.Empty;
    public string PrDescription { get; set; } = string.Empty;
    public bool IsValidated { get; set; }
    public string ValidationStatus { get; set; } = "Pending";
}

