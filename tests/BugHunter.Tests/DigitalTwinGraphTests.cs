using BugHunter.Api.Twin;
using Xunit;

namespace BugHunter.Tests;

public class DigitalTwinGraphTests
{
    [Fact]
    public void Graph_AddNodesAndEdges_ConstructsTopology()
    {
        var graph = new DigitalTwinGraph { ProjectId = "demo-shop" };

        var page = graph.AddNode("page:cart", GraphEntityType.Page, "Shopping Cart");
        var button = graph.AddNode("action:checkout", GraphEntityType.Action, "Proceed to Checkout");
        var api = graph.AddNode("api:POST:/api/orders", GraphEntityType.Api, "Create Order");

        graph.AddEdge(page.Id, button.Id, GraphRelationType.CONTAINS);
        graph.AddEdge(button.Id, api.Id, GraphRelationType.CALLS);

        Assert.Equal(3, graph.Nodes.Count);
        Assert.Equal(2, graph.Edges.Count);

        var outEdges = graph.GetOutEdges("page:cart");
        Assert.Single(outEdges);
        Assert.Equal("action:checkout", outEdges[0].To);
    }

    [Fact]
    public void Graph_BlastRadius_IdentifiesAllDownstreamImpactedNodes()
    {
        var graph = new DigitalTwinGraph { ProjectId = "demo-shop" };

        graph.AddNode("page:cart", GraphEntityType.Page, "Cart");
        graph.AddNode("action:checkout", GraphEntityType.Action, "Checkout");
        graph.AddNode("api:orders", GraphEntityType.Api, "Orders API");
        graph.AddNode("workflow:checkout-flow", GraphEntityType.Workflow, "Checkout Workflow");

        graph.AddEdge("page:cart", "action:checkout", GraphRelationType.TRANSITIONS_TO);
        graph.AddEdge("action:checkout", "api:orders", GraphRelationType.CALLS);
        graph.AddEdge("workflow:checkout-flow", "api:orders", GraphRelationType.DEPENDS_ON);

        var blastRadius = graph.FindBlastRadius("api:orders");

        Assert.Contains("api:orders", blastRadius);
        Assert.Contains("action:checkout", blastRadius);
        Assert.Contains("workflow:checkout-flow", blastRadius);
    }

    [Fact]
    public void Graph_VerifyRoleAccess_EnforcesRbacBoundaries()
    {
        var graph = new DigitalTwinGraph { ProjectId = "demo-hotel" };

        var adminPage = graph.AddNode("page:admin", GraphEntityType.Page, "Admin Console");
        graph.AddNode("role:admin", GraphEntityType.Role, "Admin Role");
        graph.AddEdge(adminPage.Id, "role:admin", GraphRelationType.REQUIRES_ROLE);

        var guestAllowed = graph.VerifyRoleAccess("guest", adminPage.Id);
        var adminAllowed = graph.VerifyRoleAccess("role:admin", adminPage.Id);

        Assert.False(guestAllowed);
        Assert.True(adminAllowed);
    }

    [Fact]
    public void Graph_SynchronizeFromAppTwin_PopulatesFullTopology()
    {
        var appTwin = new AppTwin
        {
            ProjectId = "demo-shop",
            Version = 3,
            States =
            [
                new TwinState { Id = "state:home", Url = "http://localhost:3000/", Title = "Home", Roles = ["guest"] },
                new TwinState { Id = "state:shop", Url = "http://localhost:3000/shop", Title = "Catalog", Roles = ["guest"] }
            ],
            Roads =
            [
                new TwinRoad { From = "state:home", To = "state:shop", Actions = "click Catalog" }
            ],
            Endpoints =
            [
                new TwinEndpoint { Method = "GET", Path = "/api/products", Roles = ["guest"] }
            ]
        };

        var graph = new DigitalTwinGraph();
        graph.SynchronizeFromAppTwin(appTwin);

        Assert.Equal("demo-shop", graph.ProjectId);
        Assert.Equal(3, graph.Version);
        Assert.NotNull(graph.GetNode("state:home"));
        Assert.NotNull(graph.GetNode("state:shop"));
        Assert.NotNull(graph.GetNode("api:GET:/api/products"));
        Assert.NotEmpty(graph.Edges);
    }
}

