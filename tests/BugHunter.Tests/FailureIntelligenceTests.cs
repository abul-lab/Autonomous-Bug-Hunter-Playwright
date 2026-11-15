using BugHunter.Api.FailureIntelligence;
using Xunit;

namespace BugHunter.Tests;

public class FailureIntelligenceTests
{
    [Fact]
    public void Classifier_IdentifiesLocatorDrift_Correctly()
    {
        var failure = new IngestedFailure
        {
            ErrorMessage = "Error: page.waitForSelector: Timeout 30000ms exceeded.\nwaiting for locator('button.checkout-legacy') to be visible",
            FailedLocator = "button.checkout-legacy",
            UserIntent = "Proceed to checkout"
        };

        var normalized = FailureClassifier.Classify(failure);

        Assert.Equal(FailureCategory.LocatorDrift, normalized.InferredCategory);
        Assert.True(normalized.Confidence >= 0.85);
        Assert.NotEmpty(normalized.Signature);
        Assert.Contains("LocatorDrift", normalized.Signature);
    }

    [Fact]
    public void Classifier_IdentifiesAuthAndBackendFailures()
    {
        var authFailure = new IngestedFailure
        {
            ErrorMessage = "Request failed with status code 401 Unauthorized: Invalid Bearer token"
        };
        var authNorm = FailureClassifier.Classify(authFailure);
        Assert.Equal(FailureCategory.AuthenticationFailure, authNorm.InferredCategory);

        var backendFailure = new IngestedFailure
        {
            ErrorMessage = "Unhandled 500 Internal Server Error in /api/bookings: NullReferenceException at Line 42"
        };
        var backendNorm = FailureClassifier.Classify(backendFailure);
        Assert.Equal(FailureCategory.BackendApiFailure, backendNorm.InferredCategory);
    }

    [Fact]
    public void ClusterEngine_GroupsSimilarFailuresIntoSingleCluster()
    {
        var engine = new FailureClusterEngine();

        var failure1 = new IngestedFailure
        {
            FailureId = "f1",
            ErrorMessage = "page.waitForSelector: Timeout 30000ms exceeded on button.reserve",
            FailedLocator = "button.reserve",
            Telemetry = new TelemetrySnapshot { PageUrl = "http://localhost:3000/hotel" }
        };

        var failure2 = new IngestedFailure
        {
            FailureId = "f2",
            ErrorMessage = "page.waitForSelector: Timeout 30000ms exceeded on button.reserve",
            FailedLocator = "button.reserve",
            Telemetry = new TelemetrySnapshot { PageUrl = "http://localhost:3000/hotel" }
        };

        engine.Ingest(failure1);
        engine.Ingest(failure2);

        var report = engine.GenerateReport();
        Assert.Equal(2, report.TotalFailuresIngested);
        Assert.Single(report.Clusters);
        Assert.Equal(2, report.Clusters[0].Frequency);
    }

    [Fact]
    public void ClusterEngine_FindSimilarFailures_RetrievesMatches()
    {
        var engine = new FailureClusterEngine();

        var f1 = new IngestedFailure
        {
            FailureId = "f-login-1",
            ErrorMessage = "401 Unauthorized for admin token renewal"
        };
        var f2 = new IngestedFailure
        {
            FailureId = "f-login-2",
            ErrorMessage = "401 Unauthorized for staff token renewal"
        };

        engine.Ingest(f1);
        engine.Ingest(f2);

        var query = new IngestedFailure
        {
            FailureId = "f-query",
            ErrorMessage = "401 Unauthorized for user session token"
        };

        var matches = engine.FindSimilarFailures(query);
        Assert.NotEmpty(matches);
        Assert.Equal(FailureCategory.AuthenticationFailure, matches[0].Category);
    }

    [Fact]
    public void ClusterEngine_CurateFailure_UpdatesToHumanVerified()
    {
        var engine = new FailureClusterEngine();
        var failure = new IngestedFailure
        {
            FailureId = "fail-review-01",
            ErrorMessage = "Timeout on button.checkout",
            FailedLocator = "button.checkout"
        };

        engine.Ingest(failure);

        var pending = engine.GetPendingReviews();
        Assert.Single(pending);
        Assert.Equal("ai_proposed", pending[0].VerificationStatus);

        var decision = new CurationDecision(
            "fail-review-01",
            FailureCategory.LocatorDrift,
            "principal-ai-architect",
            "Verified selector renamed in checkout redesign",
            true
        );

        var curated = engine.CurateFailure(decision);
        Assert.NotNull(curated);
        Assert.Equal("human_verified", curated.VerificationStatus);
        Assert.Equal(FailureCategory.LocatorDrift, curated.RootCauseCategory);
        Assert.NotNull(curated.Curation);
        Assert.Equal("principal-ai-architect", curated.Curation.ReviewedBy);

        var stats = engine.GetDatasetStats();
        Assert.Equal(1, stats.HumanVerified);
        Assert.Equal(0, stats.AiProposed);
    }
}

