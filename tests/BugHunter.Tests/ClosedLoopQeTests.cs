using BugHunter.Api.FailureIntelligence;
using BugHunter.Api.Twin;
using BugHunter.Api.Workflow;
using Xunit;

namespace BugHunter.Tests;

public class ClosedLoopQeTests
{
    [Fact]
    public void ClosedLoop_WhenLocatorDriftOccurs_AutoHealsAndUpdatesTwin()
    {
        var graph = new DigitalTwinGraph { ProjectId = "demo-shop" };
        var engine = new FailureClusterEngine();
        var orchestrator = new ClosedLoopQeOrchestrator(graph, engine);

        var specCode = """
        import { test } from '@playwright/test';
        test('checkout test', async ({ page }) => {
          await page.locator('#obsolete-checkout-btn').click();
        });
        """;

        var failure = new IngestedFailure
        {
            FailureId = "fail-101",
            ErrorMessage = "Timeout 30000ms exceeded waiting for locator('#obsolete-checkout-btn')",
            FailedLocator = "#obsolete-checkout-btn",
            UserIntent = "Complete Checkout",
            Telemetry = new TelemetrySnapshot
            {
                PageUrl = "http://localhost:3000/cart",
                PageTitle = "Cart Page",
                AxTreeSnippet = "button \"Complete Checkout\" [data-testid=\"checkout-action\"]"
            }
        };

        var result = orchestrator.ProcessFailure(failure, specCode, "tests/cart.spec.ts");

        // 1. Failure Intelligence Verified
        Assert.Equal(FailureCategory.LocatorDrift, result.Classification.InferredCategory);

        // 2. Twin Verified (Node added & blast radius mapped)
        var pageNode = graph.GetNode("http://localhost:3000/cart");
        Assert.NotNull(pageNode);
        Assert.Contains("http://localhost:3000/cart", result.BlastRadiusNodes);

        // 3. Self-Healing Verified
        Assert.True(result.AutoHealed);
        Assert.NotNull(result.HealedPatch);
        Assert.True(result.HealedPatch.IsValidated);
        Assert.Contains("getByRole('button', { name: 'Complete Checkout' })", result.HealedPatch.PatchedCode);

        // 4. Twin Graph updated with healed locator
        var locatorNode = graph.GetNode("locator:page.getByRole('button', { name: 'Complete Checkout' })");
        Assert.NotNull(locatorNode);
        Assert.Equal("healed", locatorNode.GetProperty<string>("status"));
    }

    [Fact]
    public void ClosedLoop_WhenBackendFailureOccurs_MapsBlastRadiusAndRecommendsTriage()
    {
        var graph = new DigitalTwinGraph { ProjectId = "demo-hotel" };
        var engine = new FailureClusterEngine();
        var orchestrator = new ClosedLoopQeOrchestrator(graph, engine);

        // Populate graph topology with dependent endpoints
        graph.AddNode("page:hotel/booking", GraphEntityType.Page, "Booking");
        graph.AddNode("api:POST:/api/bookings", GraphEntityType.Api, "Create Booking");
        graph.AddNode("workflow:reservation", GraphEntityType.Workflow, "Reservation Journey");
        graph.AddEdge("page:hotel/booking", "api:POST:/api/bookings", GraphRelationType.CALLS);
        graph.AddEdge("workflow:reservation", "api:POST:/api/bookings", GraphRelationType.DEPENDS_ON);

        var failure = new IngestedFailure
        {
            FailureId = "fail-500",
            ErrorMessage = "Request failed with status code 500: Database lock timeout",
            Telemetry = new TelemetrySnapshot
            {
                PageUrl = "page:hotel/booking"
            }
        };

        var result = orchestrator.ProcessFailure(failure);

        Assert.Equal(FailureCategory.BackendApiFailure, result.Classification.InferredCategory);
        Assert.False(result.AutoHealed);
        Assert.Contains("Review backend logs", result.NextRecommendedAction);
        Assert.Contains("page:hotel/booking", result.BlastRadiusNodes);
    }
}

