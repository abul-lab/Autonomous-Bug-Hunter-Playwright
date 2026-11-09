namespace BugHunter.Api.FailureIntelligence;

/// <summary>
/// Unsupervised clustering, similarity matching, and remediation synthesis for test failure telemetry.
/// </summary>
public class FailureClusterEngine
{
    private readonly List<IngestedFailure> _ingested = [];
    private readonly List<FailureCluster> _clusters = [];
    private readonly Dictionary<string, NormalizedError> _normalizedCache = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<FailureCluster> Clusters => _clusters;
    public IReadOnlyList<IngestedFailure> IngestedFailures => _ingested;

    public NormalizedError Ingest(IngestedFailure failure)
    {
        var normalized = FailureClassifier.Classify(failure);
        failure.RootCauseCategory = normalized.InferredCategory;
        if (string.IsNullOrEmpty(failure.VerificationStatus))
        {
            failure.VerificationStatus = "ai_proposed";
        }
        _ingested.Add(failure);
        _normalizedCache[failure.FailureId] = normalized;

        AssignToCluster(failure, normalized);
        return normalized;
    }

    public List<IngestedFailure> GetPendingReviews() =>
        _ingested.Where(f => f.VerificationStatus is "unverified" or "ai_proposed").ToList();

    public IngestedFailure? CurateFailure(CurationDecision decision)
    {
        var failure = _ingested.FirstOrDefault(f => f.FailureId.Equals(decision.FailureId, StringComparison.OrdinalIgnoreCase));
        if (failure == null) return null;

        if (decision.Approved)
        {
            failure.RootCauseCategory = decision.VerifiedCategory;
            failure.VerificationStatus = "human_verified";
        }
        else
        {
            failure.VerificationStatus = "rejected";
        }

        failure.Curation = new CurationMetadata
        {
            ReviewedBy = decision.ReviewerId,
            ReviewedAt = DateTime.UtcNow,
            Notes = decision.Notes,
            Tags = ["curated", "dataset-v1"]
        };

        return failure;
    }

    public DatasetStats GetDatasetStats()
    {
        var verified = _ingested.Count(f => f.VerificationStatus == "human_verified");
        var proposed = _ingested.Count(f => f.VerificationStatus == "ai_proposed");
        var rejected = _ingested.Count(f => f.VerificationStatus == "rejected");
        var taxonomyDist = _ingested.GroupBy(f => f.RootCauseCategory.ToString())
                                   .ToDictionary(g => g.Key, g => g.Count());

        return new DatasetStats(_ingested.Count, verified, proposed, rejected, taxonomyDist);
    }

    private void AssignToCluster(IngestedFailure failure, NormalizedError normalized)
    {
        FailureCluster? bestCluster = null;
        var highestSim = 0.0;

        foreach (var cluster in _clusters.Where(c => c.Category == normalized.InferredCategory))
        {
            var sim = CalculateJaccardSimilarity(normalized.SalientTokens, cluster.CentroidSignature.Split(','));
            if (sim > highestSim && sim >= 0.50)
            {
                highestSim = sim;
                bestCluster = cluster;
            }
        }

        if (bestCluster != null)
        {
            bestCluster.FailureIds.Add(failure.FailureId);
            bestCluster.LastSeen = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(failure.Telemetry?.PageUrl))
            {
                bestCluster.AffectedPages[failure.Telemetry.PageUrl] =
                    bestCluster.AffectedPages.GetValueOrDefault(failure.Telemetry.PageUrl) + 1;
            }
        }
        else
        {
            var newCluster = new FailureCluster
            {
                ClusterId = $"cluster_{normalized.InferredCategory.ToString().ToLowerInvariant()}_{_clusters.Count + 1}",
                Category = normalized.InferredCategory,
                CentroidSignature = string.Join(",", normalized.SalientTokens),
                RootCauseSummary = normalized.DiagnosticSummary,
                SuggestedRemediation = GenerateRemediation(normalized.InferredCategory, failure),
                FailureIds = [failure.FailureId],
                FirstSeen = DateTime.UtcNow,
                LastSeen = DateTime.UtcNow
            };

            if (!string.IsNullOrEmpty(failure.Telemetry?.PageUrl))
            {
                newCluster.AffectedPages[failure.Telemetry.PageUrl] = 1;
            }

            _clusters.Add(newCluster);
        }
    }

    public List<SimilarFailureMatch> FindSimilarFailures(IngestedFailure target, double minSimilarity = 0.5)
    {
        var targetNorm = FailureClassifier.Classify(target);
        var matches = new List<SimilarFailureMatch>();

        foreach (var past in _ingested.Where(f => f.FailureId != target.FailureId))
        {
            var pastNorm = _normalizedCache.TryGetValue(past.FailureId, out var norm)
                ? norm
                : FailureClassifier.Classify(past);

            var sim = CalculateJaccardSimilarity(targetNorm.SalientTokens, pastNorm.SalientTokens);
            if (targetNorm.InferredCategory == pastNorm.InferredCategory)
            {
                sim = Math.Min(1.0, sim + 0.3); // Category boost
            }

            if (sim >= minSimilarity)
            {
                matches.Add(new SimilarFailureMatch
                {
                    FailureId = past.FailureId,
                    SimilarityScore = Math.Round(sim, 2),
                    Category = pastNorm.InferredCategory,
                    RootCauseSummary = pastNorm.DiagnosticSummary
                });
            }
        }

        return matches.OrderByDescending(m => m.SimilarityScore).Take(5).ToList();
    }

    public FailureIntelligenceReport GenerateReport()
    {
        var report = new FailureIntelligenceReport
        {
            TotalFailuresIngested = _ingested.Count,
            Clusters = _clusters.OrderByDescending(c => c.Frequency).ToList(),
            CategoryBreakdown = _clusters.GroupBy(c => c.Category.ToString())
                                        .ToDictionary(g => g.Key, g => g.Sum(c => c.Frequency)),
            TopHotspotUrls = _clusters.SelectMany(c => c.AffectedPages)
                                     .GroupBy(kv => kv.Key)
                                     .OrderByDescending(g => g.Sum(kv => kv.Value))
                                     .Select(g => g.Key)
                                     .Take(5)
                                     .ToList()
        };

        return report;
    }

    private static double CalculateJaccardSimilarity(IEnumerable<string> set1, IEnumerable<string> set2)
    {
        var s1 = new HashSet<string>(set1, StringComparer.OrdinalIgnoreCase);
        var s2 = new HashSet<string>(set2, StringComparer.OrdinalIgnoreCase);

        if (s1.Count == 0 && s2.Count == 0) return 1.0;
        if (s1.Count == 0 || s2.Count == 0) return 0.0;

        var intersection = s1.Count(s2.Contains);
        var union = s1.Count + s2.Count - intersection;

        return (double)intersection / union;
    }

    private static string GenerateRemediation(FailureCategory category, IngestedFailure failure) => category switch
    {
        FailureCategory.LocatorDrift =>
            $"Trigger Self-Healing AST Engine to query Application Digital Twin accessibility tree for intent '{failure.UserIntent ?? "Action"}' and generate Git PR with updated locator.",
        FailureCategory.TimingIssue =>
            "Wrap action with Playwright auto-retrying assertions or wait for network idle/element state (e.g. `await page.waitForLoadState('networkidle')`).",
        FailureCategory.AuthenticationFailure =>
            "Verify role token freshness in test environment fixtures; ensure credentials in SecretSet match active environment OAuth configuration.",
        FailureCategory.EnvironmentFailure =>
            "Inspect infrastructure healthcheck (/healthz) and container orchestration status; check for network proxy or DNS misconfiguration.",
        FailureCategory.BackendApiFailure =>
            "Review backend logs at timestamp for unhandled 500 exceptions or failed database migrations.",
        FailureCategory.TestDataFailure =>
            "Isolate test database seeds using UUIDs or execute test-reset hook before test suite execution.",
        FailureCategory.VisualRegression =>
            "Inspect visual diff artifact; update visual baseline if intentional UI redesign was accepted.",
        FailureCategory.AssertionFailure =>
            "Evaluate invariant contract against business requirements to confirm whether behavior is a regression or updated requirement.",
        _ => "Inspect full Playwright trace and console logs for intermittent execution anomalies."
    };
}

