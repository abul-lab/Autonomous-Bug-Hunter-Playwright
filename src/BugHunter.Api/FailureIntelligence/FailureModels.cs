using System.Text.Json.Serialization;

namespace BugHunter.Api.FailureIntelligence;

/// <summary>
/// Root-cause taxonomy for Playwright and continuous quality engineering test failures.
/// </summary>
public enum FailureCategory
{
    LocatorDrift,
    TimingIssue,
    AuthenticationFailure,
    EnvironmentFailure,
    BackendApiFailure,
    TestDataFailure,
    VisualRegression,
    AssertionFailure,
    Unknown
}

public class TelemetrySnapshot
{
    public List<string> ConsoleErrors { get; set; } = [];
    public List<NetworkErrorSummary> NetworkErrors { get; set; } = [];
    public Dictionary<string, string>? ScreenshotMetadata { get; set; }
    public string? AxTreeSnippet { get; set; }
    public string? PageUrl { get; set; }
    public string? PageTitle { get; set; }
    public List<PrecedingActionRecord> PrecedingActionTrail { get; set; } = [];
    public DomFragmentContext? DomFragmentAtFailure { get; set; }
    public FrameworkStateContext? FrameworkState { get; set; }
}

public record PrecedingActionRecord(string ActionType, string? Selector, string? Payload, long DeltaMs);

public class DomFragmentContext
{
    public string OuterHtml { get; set; } = string.Empty;
    public string ParentTag { get; set; } = string.Empty;
    public Dictionary<string, string> ComputedStyles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class FrameworkStateContext
{
    public string Framework { get; set; } = "vanilla"; // nextjs, react, angular, vue, vanilla
    public bool IsHydrated { get; set; } = true;
    public int ActivePendingRequests { get; set; }
}

public class GovernanceMetadata
{
    public string PiiScanStatus { get; set; } = "PENDING"; // PASSED, FLAGGED, FAILED
    public string SecretScanStatus { get; set; } = "PENDING"; // PASSED, FLAGGED, FAILED
    public string ScreenshotAuditStatus { get; set; } = "PENDING"; // PASSED, REDACTED, REJECTED
    public bool ApprovedForTraining { get; set; }
    public bool ApprovedForPublicRelease { get; set; }
    public string? SignedBy { get; set; }
    public DateTime? AuditedAt { get; set; }
}

public record NetworkErrorSummary(string Method, string Url, int StatusCode, string? ErrorText);

public class IngestedFailure
{
    public string FailureId { get; set; } = Guid.NewGuid().ToString("n")[..12];
    public string TestId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string ErrorMessage { get; set; } = string.Empty;
    public string StackTrace { get; set; } = string.Empty;
    public string? FailedLocator { get; set; }
    public string? UserIntent { get; set; }
    public string VerificationStatus { get; set; } = "ai_proposed";
    public FailureCategory RootCauseCategory { get; set; } = FailureCategory.Unknown;
    public CurationMetadata? Curation { get; set; }
    public GovernanceMetadata Governance { get; set; } = new();
    public TelemetrySnapshot Telemetry { get; set; } = new();
}

public class CurationMetadata
{
    public string ReviewedBy { get; set; } = string.Empty;
    public DateTime ReviewedAt { get; set; } = DateTime.UtcNow;
    public string Notes { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
}

public record CurationDecision(string FailureId, FailureCategory VerifiedCategory, string ReviewerId, string Notes, bool Approved);

public record DatasetStats(int TotalIngested, int HumanVerified, int AiProposed, int Rejected, Dictionary<string, int> TaxonomyDistribution);

public class NormalizedError
{
    public string Signature { get; set; } = string.Empty;
    public string CleanedMessage { get; set; } = string.Empty;
    public List<string> SalientTokens { get; set; } = [];
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FailureCategory InferredCategory { get; set; }
    public double Confidence { get; set; }
    public string DiagnosticSummary { get; set; } = string.Empty;
}

public class FailureCluster
{
    public string ClusterId { get; set; } = string.Empty;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FailureCategory Category { get; set; }
    public string CentroidSignature { get; set; } = string.Empty;
    public string RootCauseSummary { get; set; } = string.Empty;
    public string SuggestedRemediation { get; set; } = string.Empty;
    public List<string> FailureIds { get; set; } = [];
    public int Frequency => FailureIds.Count;
    public DateTime FirstSeen { get; set; } = DateTime.UtcNow;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public Dictionary<string, int> AffectedPages { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class SimilarFailureMatch
{
    public string FailureId { get; set; } = string.Empty;
    public double SimilarityScore { get; set; }
    public FailureCategory Category { get; set; }
    public string RootCauseSummary { get; set; } = string.Empty;
}

public class FailureIntelligenceReport
{
    public int TotalFailuresIngested { get; set; }
    public Dictionary<string, int> CategoryBreakdown { get; set; } = new();
    public List<FailureCluster> Clusters { get; set; } = [];
    public List<string> TopHotspotUrls { get; set; } = [];
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

