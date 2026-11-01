using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SharpMUSH.Client.Components.Schema;

/// <summary>
/// The value a <c>graph</c> field edits: <c>{ nodes: [{ key, kind, label, x, y, fields }], edges: [{ from, port,
/// to }], start }</c>. Every property the editor does not know, on the graph, a node or an edge, is kept as it
/// came, so a package can carry its own header (title, party size, ...) in the same value.
/// </summary>
/// <remarks>
/// An outcome (<c>port</c>) of a step may lead to several steps, one edge each; <see cref="Link"/> keeps it to one
/// for an outcome that leads to a single step. Positions are the author's layout only; nothing that runs the graph
/// reads them.
/// </remarks>
public sealed partial class GraphDraft
{
	/// <summary>A step's width and height on the map, in pixels.</summary>
	public const double NodeWidth = 172, NodeHeight = 68;

	private const double GapX = 72, GapY = 64, Margin = 16;

	private readonly JsonObject _extra;
	private readonly List<GraphNode> _nodes;
	private readonly List<GraphEdge> _edges;

	private GraphDraft(JsonObject extra, List<GraphNode> nodes, List<GraphEdge> edges, string? start)
	{
		_extra = extra;
		_nodes = nodes;
		_edges = edges;
		Start = start;
	}

	public IReadOnlyList<GraphNode> Nodes => _nodes;

	public IReadOnlyList<GraphEdge> Edges => _edges;

	/// <summary>The key of the step a run begins at, or <c>null</c> for the first step.</summary>
	public string? Start { get; private set; }

	/// <summary>The step a run begins at: the one <see cref="Start"/> names, else the first.</summary>
	public GraphNode? StartNode => Find(Start) ?? _nodes.FirstOrDefault();

	/// <summary>Reads a value; anything that is not an object reads as an empty graph.</summary>
	public static GraphDraft Read(JsonElement? value)
	{
		if (value is not { ValueKind: JsonValueKind.Object } element || JsonNode.Parse(element.GetRawText()) is not JsonObject root)
		{
			return new GraphDraft([], [], [], null);
		}

		var nodes = Items(root, "nodes").Select(GraphNode.Read).OfType<GraphNode>()
			.DistinctBy(node => node.Key, StringComparer.Ordinal).ToList();
		var keys = nodes.Select(node => node.Key).ToHashSet(StringComparer.Ordinal);
		var edges = Items(root, "edges").Select(GraphEdge.Read).OfType<GraphEdge>()
			.Where(edge => keys.Contains(edge.From) && keys.Contains(edge.To))
			.DistinctBy(edge => (edge.From, edge.Port, edge.To)).ToList();
		var start = Text(root, "start");
		root.Remove("nodes");
		root.Remove("edges");
		root.Remove("start");
		return new GraphDraft(root, nodes, edges, string.IsNullOrEmpty(start) ? null : start);
	}

	/// <summary>The value as the field posts it.</summary>
	public JsonElement ToJson()
	{
		var root = (JsonObject)_extra.DeepClone();
		root["nodes"] = new JsonArray([.. _nodes.Select(node => (JsonNode)node.ToJson())]);
		root["edges"] = new JsonArray([.. _edges.Select(edge => (JsonNode)edge.ToJson())]);
		if (Start is not null) root["start"] = Start;
		return JsonSerializer.SerializeToElement(root);
	}

	public GraphNode? Find(string? key) => key is null ? null : _nodes.FirstOrDefault(node => node.Key == key);

	/// <summary>The (first) step <paramref name="port"/> of <paramref name="from"/> leads to, or <c>null</c>.</summary>
	public GraphNode? Target(string from, string port) => Targets(from, port).FirstOrDefault();

	/// <summary>Every step <paramref name="port"/> of <paramref name="from"/> leads to, in the order they were linked.</summary>
	public IReadOnlyList<GraphNode> Targets(string from, string port)
		=> [.. _edges.Where(edge => edge.From == from && edge.Port == port).Select(edge => Find(edge.To)).OfType<GraphNode>()];

	/// <summary>The edges leading into <paramref name="key"/>.</summary>
	public IEnumerable<GraphEdge> Into(string key) => _edges.Where(edge => edge.To == key);

	/// <summary>
	/// Adds a step of <paramref name="kind"/> under the lowest one, keyed after its kind. The first step added is
	/// where a run begins.
	/// </summary>
	public GraphNode Add(string kind, string label)
	{
		var key = NewKey(kind);
		var y = _nodes.Count == 0 ? Margin : _nodes.Max(node => node.Y) + NodeHeight + GapY;
		var node = new GraphNode(key, kind, label, Margin, y, [], []);
		_nodes.Add(node);
		if (_nodes.Count == 1) Start = key;
		return node;
	}

	/// <summary>Removes a step and every edge to or from it.</summary>
	public void Remove(string key)
	{
		_nodes.RemoveAll(node => node.Key == key);
		_edges.RemoveAll(edge => edge.From == key || edge.To == key);
		if (Start == key) Start = _nodes.FirstOrDefault()?.Key;
	}

	/// <summary>Makes <paramref name="key"/> the step a run begins at.</summary>
	public void SetStart(string key)
	{
		if (Find(key) is not null) Start = key;
	}

	/// <summary>Points <paramref name="port"/> of <paramref name="from"/> at <paramref name="to"/> alone, or at nothing.</summary>
	public void Link(string from, string port, string? to)
	{
		_edges.RemoveAll(edge => edge.From == from && edge.Port == port && edge.To != to);
		if (string.IsNullOrEmpty(to) || Find(to) is null || Find(from) is null)
		{
			_edges.RemoveAll(edge => edge.From == from && edge.Port == port);
			return;
		}

		if (!_edges.Any(edge => edge.From == from && edge.Port == port)) _edges.Add(new GraphEdge(from, port, to, []));
	}

	/// <summary>Adds <paramref name="to"/> to the steps <paramref name="port"/> of <paramref name="from"/> may lead to.</summary>
	public void AddLink(string from, string port, string to)
	{
		if (Find(from) is null || Find(to) is null || _edges.Any(edge => edge.From == from && edge.Port == port && edge.To == to)) return;
		_edges.Add(new GraphEdge(from, port, to, []));
	}

	/// <summary>The edge from <paramref name="from"/>'s <paramref name="port"/> to <paramref name="to"/>, or <c>null</c>.</summary>
	public GraphEdge? Edge(string from, string port, string to)
		=> _edges.FirstOrDefault(edge => edge.From == from && edge.Port == port && edge.To == to);

	/// <summary>Sets one of an edge's settings; <c>null</c> clears it.</summary>
	public void SetEdgeField(string from, string port, string to, string key, JsonElement? value)
	{
		var index = _edges.FindIndex(edge => edge.From == from && edge.Port == port && edge.To == to);
		if (index < 0 || key is "from" or "port" or "to") return;
		var extra = (JsonObject)_edges[index].Extra.DeepClone();
		if (value is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } element) extra[key] = JsonNode.Parse(element.GetRawText());
		else extra.Remove(key);
		_edges[index] = _edges[index] with { Extra = extra };
	}

	/// <summary>Takes <paramref name="to"/> off the steps <paramref name="port"/> of <paramref name="from"/> may lead to.</summary>
	public void Unlink(string from, string port, string to)
		=> _edges.RemoveAll(edge => edge.From == from && edge.Port == port && edge.To == to);

	/// <summary>Why <paramref name="key"/> cannot name a step other than <paramref name="current"/>, or <c>null</c> when it can.</summary>
	public KeyProblem? CheckKey(string current, string key)
		=> !KeyPattern().IsMatch(key) ? KeyProblem.Invalid
			: key != current && Find(key) is not null ? KeyProblem.Taken
			: null;

	/// <summary>Gives a step a new key, carrying its edges and the start along. False when the key is refused.</summary>
	public bool Rename(string current, string key)
	{
		if (current == key) return true;
		if (CheckKey(current, key) is not null || Find(current) is not { } node) return false;
		_nodes[_nodes.IndexOf(node)] = node with { Key = key };
		for (var i = 0; i < _edges.Count; i++)
		{
			var edge = _edges[i];
			if (edge.From == current || edge.To == current)
			{
				_edges[i] = edge with { From = edge.From == current ? key : edge.From, To = edge.To == current ? key : edge.To };
			}
		}
		if (Start == current) Start = key;
		return true;
	}

	/// <summary>Replaces a step with an edited copy of itself (same key).</summary>
	public void Update(GraphNode node)
	{
		var index = _nodes.FindIndex(n => n.Key == node.Key);
		if (index >= 0) _nodes[index] = node;
	}

	/// <summary>
	/// The steps in reading order: from the start, each step's outcomes in the order its kind lists them, then any
	/// step nothing leads to.
	/// </summary>
	public IReadOnlyList<GraphNode> Order(Func<string, IReadOnlyList<string>> portsOf)
	{
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var order = new List<GraphNode>();
		var queue = new Queue<GraphNode>();
		if (StartNode is { } start) queue.Enqueue(start);
		while (true)
		{
			while (queue.TryDequeue(out var node))
			{
				if (!seen.Add(node.Key)) continue;
				order.Add(node);
				foreach (var next in Next(node, portsOf)) queue.Enqueue(next);
			}

			if (_nodes.FirstOrDefault(node => !seen.Contains(node.Key)) is not { } rest) return order;
			queue.Enqueue(rest);
		}
	}

	/// <summary>
	/// Lays the steps out top to bottom: each step a row below the first step that leads to it, steps on one row
	/// side by side in reading order.
	/// </summary>
	public void Tidy(Func<string, IReadOnlyList<string>> portsOf)
	{
		var depth = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (var node in Order(portsOf))
		{
			if (!depth.ContainsKey(node.Key)) depth[node.Key] = depth.Count == 0 ? 0 : depth.Values.Max() + 1;
			foreach (var next in Next(node, portsOf))
			{
				depth.TryAdd(next.Key, depth[node.Key] + 1);
			}
		}

		foreach (var row in Order(portsOf).GroupBy(node => depth[node.Key]))
		{
			var column = 0;
			foreach (var node in row)
			{
				Update(node with { X = Margin + column++ * (NodeWidth + GapX), Y = Margin + row.Key * (NodeHeight + GapY) });
			}
		}
	}

	/// <summary>The step whose box holds the point (<paramref name="x"/>, <paramref name="y"/>), the topmost first.</summary>
	public GraphNode? At(double x, double y)
		=> _nodes.LastOrDefault(node => x >= node.X && x <= node.X + NodeWidth && y >= node.Y && y <= node.Y + NodeHeight);

	/// <summary>
	/// The place nearest (<paramref name="x"/>, <paramref name="y"/>) where a step overlaps none but
	/// <paramref name="except"/>: there, else beside it to the right or the left, else a row further down.
	/// </summary>
	public (double X, double Y) FreeSpot(double x, double y, string? except = null)
	{
		for (var row = 0; row < 50; row++)
		{
			foreach (var cx in ((int[])[0, 1, -1, 2, -2]).Select(shift => x + shift * (NodeWidth + GapX)))
			{
				var cy = y + row * (NodeHeight + GapY);
				if (cx < 0) continue;
				if (!_nodes.Any(node => node.Key != except && Math.Abs(node.X - cx) < NodeWidth + 8 && Math.Abs(node.Y - cy) < NodeHeight + 8))
				{
					return (cx, cy);
				}
			}
		}

		return (x, y);
	}

	/// <summary>Moves a step, keeping it on the map.</summary>
	public void Move(string key, double x, double y)
	{
		if (Find(key) is { } node) Update(node with { X = Math.Max(0, Math.Round(x)), Y = Math.Max(0, Math.Round(y)) });
	}

	private IEnumerable<GraphNode> Next(GraphNode node, Func<string, IReadOnlyList<string>> portsOf)
	{
		var ports = portsOf(node.Kind);
		return _edges.Where(edge => edge.From == node.Key)
			.OrderBy(edge => Rank(ports, edge.Port))
			.Select(edge => Find(edge.To)).OfType<GraphNode>();
	}

	/// <summary>Where <paramref name="port"/> comes among a kind's outcomes; one the kind does not list comes last.</summary>
	internal static int Rank(IReadOnlyList<string> ports, string port)
	{
		for (var i = 0; i < ports.Count; i++)
		{
			if (ports[i] == port) return i;
		}
		return int.MaxValue;
	}

	private string NewKey(string kind)
	{
		var stem = KeyStem().Replace(kind.ToLowerInvariant(), "");
		if (stem.Length == 0) stem = "step";
		for (var n = 1; ; n++)
		{
			var key = $"{stem}{n}";
			if (Find(key) is null) return key;
		}
	}

	private static IEnumerable<JsonObject> Items(JsonObject root, string name)
		=> root[name] is JsonArray array ? array.OfType<JsonObject>() : [];

	internal static string? Text(JsonObject node, string name)
		=> node[name] is JsonValue value && value.GetValueKind() is JsonValueKind.String or JsonValueKind.Number ? value.ToString() : null;

	[GeneratedRegex("^[a-z0-9][a-z0-9_-]*$")]
	private static partial Regex KeyPattern();

	[GeneratedRegex("[^a-z0-9_-]")]
	private static partial Regex KeyStem();
}

/// <summary>Why a key cannot name a step.</summary>
public enum KeyProblem
{
	/// <summary>Not lower-case letters, digits, <c>-</c> and <c>_</c>, starting with a letter or digit.</summary>
	Invalid,

	/// <summary>Another step has it.</summary>
	Taken
}

/// <summary>A step: its key, kind, name, place on the map, the values of its kind's fields, and anything else it carried.</summary>
public sealed record GraphNode(string Key, string Kind, string Label, double X, double Y, JsonObject Fields, JsonObject Extra)
{
	/// <summary>The value of one of the step's fields, or <c>null</c>.</summary>
	public JsonElement? Field(string key)
		=> Fields[key] is { } value ? JsonSerializer.SerializeToElement(value) : null;

	/// <summary>A copy of the step with one field set; <c>null</c> clears it.</summary>
	public GraphNode WithField(string key, JsonElement? value)
	{
		var fields = (JsonObject)Fields.DeepClone();
		if (value is { } element && element.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
		{
			fields[key] = JsonNode.Parse(element.GetRawText());
		}
		else
		{
			fields.Remove(key);
		}
		return this with { Fields = fields };
	}

	internal static GraphNode? Read(JsonObject item)
	{
		var key = GraphDraft.Text(item, "key");
		if (string.IsNullOrEmpty(key)) return null;
		var extra = (JsonObject)item.DeepClone();
		foreach (var name in (string[])["key", "kind", "label", "x", "y", "fields"]) extra.Remove(name);
		return new GraphNode(key, GraphDraft.Text(item, "kind") ?? "", GraphDraft.Text(item, "label") ?? key,
			Number(item, "x"), Number(item, "y"), item["fields"] is JsonObject fields ? (JsonObject)fields.DeepClone() : [], extra);
	}

	internal JsonObject ToJson()
	{
		var node = (JsonObject)Extra.DeepClone();
		node["key"] = Key;
		node["kind"] = Kind;
		node["label"] = Label;
		node["x"] = X;
		node["y"] = Y;
		node["fields"] = Fields.DeepClone();
		return node;
	}

	private static double Number(JsonObject item, string name)
		=> item[name] is JsonValue value && value.TryGetValue<double>(out var number) ? number
			: double.TryParse(GraphDraft.Text(item, name), System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
}

/// <summary>An outcome of a step leading to another, and anything else it carried.</summary>
public sealed record GraphEdge(string From, string Port, string To, JsonObject Extra)
{
	/// <summary>One of the edge's settings, or <c>null</c>.</summary>
	public JsonElement? Field(string key) => Extra[key] is { } value ? JsonSerializer.SerializeToElement(value) : null;

	internal static GraphEdge? Read(JsonObject item)
	{
		var from = GraphDraft.Text(item, "from");
		var to = GraphDraft.Text(item, "to");
		if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) return null;
		var extra = (JsonObject)item.DeepClone();
		foreach (var name in (string[])["from", "port", "to"]) extra.Remove(name);
		return new GraphEdge(from, GraphDraft.Text(item, "port") ?? "", to, extra);
	}

	internal JsonObject ToJson()
	{
		var edge = (JsonObject)Extra.DeepClone();
		edge["from"] = From;
		edge["port"] = Port;
		edge["to"] = To;
		return edge;
	}
}
