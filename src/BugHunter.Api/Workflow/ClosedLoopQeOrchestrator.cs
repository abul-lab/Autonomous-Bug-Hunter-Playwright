using BugHunter.Api.FailureIntelligence;
using BugHunter.Api.SelfHealing;
using BugHunter.Api.Twin;

namespace BugHunter.Api.Workflow;

public class ClosedLoopExecutionResult
{
    public string ExecutionId { get; set; } = Guid.NewGuid().ToString("n")[..10];
    public NormalizedError Classification { get; set; } = new();
    public FailureCluster Cluster { get; set; } = new();
    public HashSet<string> BlastRadiusNodes { get; set; } = [];
    public HealedPatch? HealedPatch { get; set; }
    public bool AutoHealed { get; set; }
    public string NextRecommendedAction { get; set; } = string.Empty;
}

/// <summary>
/// Orchestrates the unified cognitive loop:
/// Failure Occurs -> Failure Intelligence Classifies -> Digital Twin Maps Blast Radius ->
/// Self-Healing Proposes AST Fix -> Deterministic Engine Validates -> Git PR Recommendation Generated.
/// </summary>
public class ClosedLoopQeOrchestrator(
    DigitalTwinGraph twinGraph,
    FailureClusterEngine clusterEngine)
{
    public ClosedLoopExecutionResult ProcessFailure(
        IngestedFailure failure,
        string? specCode = null,
        string specPath = "tests/checkout.spec.ts")
    {
        var result = new ClosedLoopExecutionResult();

        // 1. Failure Intelligence Ingestion & Classification
        var normalized = clusterEngine.Ingest(failure);
        result.Classification = normalized;

        var matchedCluster = clusterEngine.Clusters.FirstOrDefault(c => c.FailureIds.Contains(failure.FailureId))
                             ?? new FailureCluster { Category = normalized.InferredCategory };
        result.Cluster = matchedCluster;

        // 2. Digital Twin Graph Contextualization
        var targetNodeId = failure.Telemetry?.PageUrl ?? "page:unknown";
        var pageNode = twinGraph.GetNode(targetNodeId) ??
                       twinGraph.AddNode(targetNodeId, GraphEntityType.Page, failure.Telemetry?.PageTitle ?? targetNodeId);

        // Map failure to graph
        var failureNode = twinGraph.AddNode(
            $"failure:{failure.FailureId}",
            GraphEntityType.Failure,
            normalized.DiagnosticSummary,
            new()
            {
                ["category"] = normalized.InferredCategory.ToString(),
                ["confidence"] = normalized.Confidence
            });

        twinGraph.AddEdge(failureNode.Id, pageNode.Id, GraphRelationType.FAILS_ON);

        // Calculate blast radius across the graph
        result.BlastRadiusNodes = twinGraph.FindBlastRadius(pageNode.Id);

        // 3. Self-Healing AST Engine (For Locator Drift)
        if (normalized.InferredCategory == FailureCategory.LocatorDrift &&
            !string.IsNullOrEmpty(failure.FailedLocator) &&
            !string.IsNullOrEmpty(specCode))
        {
            var intent = new IntentSpec
            {
                OriginalIntent = failure.UserIntent ?? "Complete Action",
                FailedLocator = failure.FailedLocator,
                PageUrl = failure.Telemetry?.PageUrl ?? string.Empty,
                AxTreeSnippet = failure.Telemetry?.AxTreeSnippet
            };

            var candidates = AstHealer.FindCandidates(intent, twinGraph);
            var bestCandidate = candidates.FirstOrDefault();

            if (bestCandidate != null && bestCandidate.CombinedConfidence >= 0.70)
            {
                var patch = AstHealer.ProposePatch(specCode, intent, bestCandidate, specPath);

                // Deterministic Verification Gate:
                if (patch.IsValidated)
                {
                    result.HealedPatch = patch;
                    result.AutoHealed = true;
                    result.NextRecommendedAction = $"Submit PR '{patch.PrTitle}' to branch '{patch.BranchName}'.";

                    // Update Digital Twin with healed locator
                    var locatorNode = twinGraph.AddNode(
                        $"locator:{bestCandidate.Selector}",
                        GraphEntityType.Locator,
                        bestCandidate.Selector,
                        new()
                        {
                            ["strategy"] = bestCandidate.Strategy,
                            ["confidence"] = bestCandidate.CombinedConfidence,
                            ["status"] = "healed"
                        });
                    twinGraph.AddEdge(pageNode.Id, locatorNode.Id, GraphRelationType.USES);
                }
            }
        }

        if (!result.AutoHealed)
        {
            result.NextRecommendedAction = matchedCluster.SuggestedRemediation;
        }

        return result;
    }
}

