using System.Text.Json;
using System.Text.Json.Serialization;

namespace BugHunter.Api.Twin;

/// <summary>
/// Property Graph Core representing the living Application Digital Twin.
/// Models pages, actions, workflows, APIs, components, roles, risks, failures, and locators.
/// </summary>
public enum GraphEntityType
{
    Page,
    Action,
    Workflow,
    Api,
    Component,
    Role,
    Risk,
    Failure,
    Locator
}

public enum GraphRelationType
{
    CONTAINS,
    DEPENDS_ON,
    CALLS,
    TRANSITIONS_TO,
    FAILS_ON,
    USES,
    REQUIRES_ROLE
}

public class GraphNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GraphEntityType Type { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("properties")]
    public Dictionary<string, object> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public T? GetProperty<T>(string key)
    {
        if (Properties.TryGetValue(key, out var val))
        {
            if (val is T typed) return typed;
            if (val is JsonElement elem)
            {
                return JsonSerializer.Deserialize<T>(elem.GetRawText());
            }
        }
        return default;
    }
}

public class GraphEdge
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("from")]
    public string From { get; set; } = string.Empty;

    [JsonPropertyName("to")]
    public string To { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GraphRelationType Type { get; set; }

    [JsonPropertyName("properties")]
    public Dictionary<string, object> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class DigitalTwinGraph
{
    public string ProjectId { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    private readonly Dictionary<string, GraphNode> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GraphEdge> _edges = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<GraphEdge>> _outEdges = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<GraphEdge>> _inEdges = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<GraphNode> Nodes => _nodes.Values;
    public IReadOnlyCollection<GraphEdge> Edges => _edges.Values;

    public GraphNode AddNode(string id, GraphEntityType type, string label, Dictionary<string, object>? properties = null)
    {
        if (_nodes.TryGetValue(id, out var existing))
        {
            existing.Label = label;
            existing.Type = type;
            if (properties != null)
            {
                foreach (var (k, v) in properties) existing.Properties[k] = v;
            }
            return existing;
        }

        var node = new GraphNode
        {
            Id = id,
            Type = type,
            Label = label,
            Properties = properties ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        };
        _nodes[id] = node;
        return node;
    }

    public GraphNode? GetNode(string id) => _nodes.GetValueOrDefault(id);

    public GraphEdge AddEdge(string fromId, string toId, GraphRelationType type, Dictionary<string, object>? properties = null)
    {
        var edgeId = $"{fromId}->{type}->{toId}";
        if (_edges.TryGetValue(edgeId, out var existing))
        {
            if (properties != null)
            {
                foreach (var (k, v) in properties) existing.Properties[k] = v;
            }
            return existing;
        }

        var edge = new GraphEdge
        {
            Id = edgeId,
            From = fromId,
            To = toId,
            Type = type,
            Properties = properties ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        };

        _edges[edgeId] = edge;
        if (!_outEdges.TryGetValue(fromId, out var outs)) _outEdges[fromId] = outs = [];
        outs.Add(edge);

        if (!_inEdges.TryGetValue(toId, out var ins)) _inEdges[toId] = ins = [];
        ins.Add(edge);

        return edge;
    }

    public IReadOnlyList<GraphEdge> GetOutEdges(string nodeId) => _outEdges.GetValueOrDefault(nodeId) ?? [];
    public IReadOnlyList<GraphEdge> GetInEdges(string nodeId) => _inEdges.GetValueOrDefault(nodeId) ?? [];

    /// <summary>
    /// Calculates the blast radius (all downstream dependent workflows, components, and pages) if a node changes or breaks.
    /// </summary>
    public HashSet<string> FindBlastRadius(string changedNodeId)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(changedNodeId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current)) continue;

            // Inward dependencies (things that DEPEND_ON, CALL, or USE this node)
            foreach (var inEdge in GetInEdges(current))
            {
                if (inEdge.Type is GraphRelationType.DEPENDS_ON or GraphRelationType.CALLS or GraphRelationType.USES or GraphRelationType.TRANSITIONS_TO)
                {
                    if (!visited.Contains(inEdge.From)) queue.Enqueue(inEdge.From);
                }
            }

            // Outward containment/transitions
            foreach (var outEdge in GetOutEdges(current))
            {
                if (outEdge.Type is GraphRelationType.TRANSITIONS_TO or GraphRelationType.CONTAINS)
                {
                    if (!visited.Contains(outEdge.To)) queue.Enqueue(outEdge.To);
                }
            }
        }

        return visited;
    }

    /// <summary>
    /// Finds accessible routes between two pages or workflows.
    /// </summary>
    public List<string> FindPath(string fromNodeId, string toNodeId)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(fromNodeId);
        visited.Add(fromNodeId);

        while (queue.Count > 0)
        {
            var curr = queue.Dequeue();
            if (curr.Equals(toNodeId, StringComparison.OrdinalIgnoreCase))
            {
                var path = new List<string> { curr };
                while (parent.TryGetValue(curr, out var p))
                {
                    path.Insert(0, p);
                    curr = p;
                }
                return path;
            }

            foreach (var edge in GetOutEdges(curr))
            {
                if (edge.Type == GraphRelationType.TRANSITIONS_TO && visited.Add(edge.To))
                {
                    parent[edge.To] = curr;
                    queue.Enqueue(edge.To);
                }
            }
        }

        return [];
    }

    /// <summary>
    /// Verifies if a given role is authorized to execute or access an entity.
    /// </summary>
    public bool VerifyRoleAccess(string role, string targetNodeId)
    {
        var edges = GetOutEdges(targetNodeId).Where(e => e.Type == GraphRelationType.REQUIRES_ROLE).ToList();
        if (edges.Count == 0) return true; // public / unrestricted

        return edges.Any(e => e.To.Equals(role, StringComparison.OrdinalIgnoreCase) ||
                             e.To.Equals("public", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Populates and synchronizes graph entities and relationships from the traditional AppTwin model.
    /// </summary>
    public void SynchronizeFromAppTwin(AppTwin twin)
    {
        ProjectId = twin.ProjectId;
        Version = twin.Version;
        LastUpdated = twin.BuiltAt;

        // 1. Pages / States
        foreach (var state in twin.States)
        {
            var pageNode = AddNode(state.Id, GraphEntityType.Page, state.Title, new()
            {
                ["url"] = state.Url,
                ["landmarks"] = state.Landmarks,
                ["status"] = state.Status,
                ["roles"] = state.Roles
            });

            foreach (var role in state.Roles)
            {
                AddNode(role, GraphEntityType.Role, role);
                AddEdge(pageNode.Id, role, GraphRelationType.REQUIRES_ROLE);
            }
        }

        // 2. Transitions / Roads
        foreach (var road in twin.Roads)
        {
            if (_nodes.ContainsKey(road.From) && _nodes.ContainsKey(road.To))
            {
                var actionId = $"action:{road.From}->{road.To}";
                AddNode(actionId, GraphEntityType.Action, road.Actions, new() { ["status"] = road.Status });
                AddEdge(road.From, actionId, GraphRelationType.CONTAINS);
                AddEdge(actionId, road.To, GraphRelationType.TRANSITIONS_TO);
            }
        }

        // 3. API Endpoints
        foreach (var ep in twin.Endpoints)
        {
            var apiId = $"api:{ep.Method.ToUpperInvariant()}:{ep.Path}";
            var apiNode = AddNode(apiId, GraphEntityType.Api, $"{ep.Method} {ep.Path}", new()
            {
                ["method"] = ep.Method,
                ["path"] = ep.Path,
                ["roles"] = ep.Roles
            });

            foreach (var role in ep.Roles)
            {
                AddNode(role, GraphEntityType.Role, role);
                AddEdge(apiNode.Id, role, GraphRelationType.REQUIRES_ROLE);
            }
        }

        // 4. Features & Entities
        foreach (var feature in twin.Features)
        {
            var wfId = $"workflow:{feature.Name}";
            var wfNode = AddNode(wfId, GraphEntityType.Workflow, feature.Name, new()
            {
                ["pages"] = feature.Pages,
                ["stories"] = feature.Stories,
                ["forRoles"] = feature.ForRoles
            });

            foreach (var epPath in feature.Endpoints)
            {
                var matched = _nodes.Values.FirstOrDefault(n => n.Type == GraphEntityType.Api && (string)n.Properties.GetValueOrDefault("path", "") == epPath);
                if (matched != null)
                {
                    AddEdge(wfNode.Id, matched.Id, GraphRelationType.CALLS);
                }
            }
        }
    }
}
