using BugHunter.Api.FailureIntelligence;
using BugHunter.Api.SelfHealing;
using BugHunter.Api.Twin;

namespace BugHunter.Api.Workflow;

public static class QeIntelligenceEndpoints
{
    public static RouteGroupBuilder MapQeIntelligence(this RouteGroupBuilder group)
    {
        // 1. Digital Twin Graph Endpoints
        group.MapGet("/twin/graph", (DigitalTwinGraph graph) => Results.Ok(new
        {
            projectId = graph.ProjectId,
            version = graph.Version,
            lastUpdated = graph.LastUpdated,
            nodeCount = graph.Nodes.Count,
            edgeCount = graph.Edges.Count,
            nodes = graph.Nodes,
            edges = graph.Edges
        })).WithTags("Digital Twin Graph");

        group.MapGet("/twin/graph/blast-radius", (DigitalTwinGraph graph, string nodeId) =>
        {
            var radius = graph.FindBlastRadius(nodeId);
            return Results.Ok(new { sourceNode = nodeId, affectedNodeCount = radius.Count, blastRadius = radius });
        }).WithTags("Digital Twin Graph");

        // 2. Failure Intelligence Endpoints
        group.MapPost("/failure-intelligence/ingest", (FailureClusterEngine engine, IngestedFailure failure) =>
        {
            var normalized = engine.Ingest(failure);
            return Results.Ok(normalized);
        }).WithTags("Failure Intelligence");

        group.MapGet("/failure-intelligence/report", (FailureClusterEngine engine) =>
        {
            return Results.Ok(engine.GenerateReport());
        }).WithTags("Failure Intelligence");

        group.MapPost("/failure-intelligence/similar", (FailureClusterEngine engine, IngestedFailure failure) =>
        {
            var similar = engine.FindSimilarFailures(failure);
            return Results.Ok(similar);
        }).WithTags("Failure Intelligence");

        // Dataset Curation Endpoints
        group.MapGet("/dataset/pending-review", (FailureClusterEngine engine) =>
        {
            return Results.Ok(engine.GetPendingReviews());
        }).WithTags("Dataset Curation");

        group.MapPost("/dataset/curate", (FailureClusterEngine engine, CurationDecision decision) =>
        {
            var curated = engine.CurateFailure(decision);
            return curated != null ? Results.Ok(curated) : Results.NotFound(new { error = "FailureId not found" });
        }).WithTags("Dataset Curation");

        group.MapGet("/dataset/stats", (FailureClusterEngine engine) =>
        {
            return Results.Ok(engine.GetDatasetStats());
        }).WithTags("Dataset Curation");

        // 3. Self-Healing AST Endpoints
        group.MapPost("/self-healing/propose", (IntentSpec intent, string specCode, string? specPath) =>
        {
            var candidates = AstHealer.FindCandidates(intent);
            var best = candidates.FirstOrDefault() ?? new CandidateLocator
            {
                Selector = $"page.getByRole('button', {{ name: '{intent.OriginalIntent}' }})",
                Strategy = "role",
                MatchScore = 0.8,
                StabilityScore = 0.9
            };

            var patch = AstHealer.ProposePatch(specCode, intent, best, specPath ?? "tests/e2e.spec.ts");
            return Results.Ok(patch);
        }).WithTags("Self-Healing AST");

        // 4. Closed-Loop Execution Endpoint
        group.MapPost("/qe/closed-loop", (
            ClosedLoopQeOrchestrator orchestrator,
            ClosedLoopRequest request) =>
        {
            var result = orchestrator.ProcessFailure(request.Failure, request.SpecCode, request.SpecPath ?? "tests/e2e.spec.ts");
            return Results.Ok(result);
        }).WithTags("Closed-Loop QE");

        return group;
    }
}

public record ClosedLoopRequest(IngestedFailure Failure, string? SpecCode, string? SpecPath);

