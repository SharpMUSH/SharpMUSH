using System.Text.Json;
using SharpMUSH.Client.Components.Scenes;

namespace SharpMUSH.Client.Components.Schema;

/// <summary>
/// Where a <c>graph</c> element draws its nodes: the focus node in the middle, then a ring for each step away
/// from it, the way a relationship web reads outward from one character. Holds no UI state, so a test can check
/// the drawing without a browser.
/// </summary>
public static class SchemaGraphLayout
{
	/// <summary>The radius of a node's circle, in the drawing's own units.</summary>
	public const double NodeRadius = 26;

	/// <summary>How far each ring sits from the one inside it.</summary>
	public const double RingStep = 150;

	/// <summary>Room around the outer ring for a node's circle and its name below it.</summary>
	private const double Margin = 60;

	/// <summary>One node as read from the data: its id, the name drawn under it, an optional picture.</summary>
	public sealed record Node(string Id, string Label, string? Image, bool Focus, JsonElement Source);

	/// <summary>
	/// One edge: <c>From</c> and <c>To</c> are node ids, <c>Label</c> the text at its middle, <c>Tone</c> a
	/// <c>tone()</c> name for its colour, <c>Directed</c> an arrow at <c>To</c>.
	/// </summary>
	public sealed record Edge(string From, string To, string Label, string? Tone, bool Directed);

	/// <summary>A node placed in the drawing, with <c>Ring</c> its steps from the focus.</summary>
	public sealed record PlacedNode(Node Node, double X, double Y, int Ring);

	/// <summary>An edge between two placed nodes: the line runs from (X1,Y1) to (X2,Y2), shortened at an arrow.</summary>
	public sealed record PlacedEdge(Edge Edge, PlacedNode From, PlacedNode To, double X1, double Y1, double X2, double Y2)
	{
		public double MidX => (X1 + X2) / 2;
		public double MidY => (Y1 + Y2) / 2;

		/// <summary>The portal token the edge's tone names, or null for the theme's own line colour.</summary>
		public string? ToneToken => PoseTypeStyle.ToneToken(Edge.Tone);
	}

	/// <summary>
	/// The drawing: the square the rings were laid out in, the box that holds what was drawn (the nodes and the
	/// room for their names), and every node and edge in place.
	/// </summary>
	public sealed record Drawing(double Size, IReadOnlyList<PlacedNode> Nodes, IReadOnlyList<PlacedEdge> Edges)
	{
		public double Centre => Size / 2;

		public double Left => Nodes.Count == 0 ? 0 : Math.Max(0, Nodes.Min(n => n.X) - Margin);
		public double Top => Nodes.Count == 0 ? 0 : Math.Max(0, Nodes.Min(n => n.Y) - Margin);
		public double Width => Nodes.Count == 0 ? 0 : Math.Min(Size, Nodes.Max(n => n.X) + Margin) - Left;
		public double Height => Nodes.Count == 0 ? 0 : Math.Min(Size, Nodes.Max(n => n.Y) + Margin) - Top;
	}

	/// <summary>
	/// Reads <c>{ nodes: [{id, label, image, focus}], edges: [{from, to, label, tone, directed}] }</c>. A node
	/// without an id, a repeated id, and an edge naming a node that is not there are left out.
	/// </summary>
	public static (IReadOnlyList<Node> Nodes, IReadOnlyList<Edge> Edges) Read(JsonElement? value)
	{
		if (value is not { ValueKind: JsonValueKind.Object } graph)
		{
			return ([], []);
		}

		var nodes = new List<Node>();
		var ids = new HashSet<string>(StringComparer.Ordinal);
		foreach (var item in Items(graph, "nodes"))
		{
			var id = Text(item, "id");
			if (string.IsNullOrEmpty(id) || !ids.Add(id))
			{
				continue;
			}

			var label = Text(item, "label");
			nodes.Add(new Node(id, string.IsNullOrEmpty(label) ? id : label, NullIfEmpty(Text(item, "image")), Flag(item, "focus"), item.Clone()));
		}

		var edges = Items(graph, "edges")
			.Select(item => new Edge(Text(item, "from"), Text(item, "to"), Text(item, "label"), NullIfEmpty(Text(item, "tone")), Flag(item, "directed")))
			.Where(edge => ids.Contains(edge.From) && ids.Contains(edge.To) && edge.From != edge.To)
			.ToList();

		return (nodes, edges);
	}

	/// <summary>
	/// Places the nodes: the focus (the first node marked <c>focus</c>, else the first node) at the centre, every
	/// node one edge away on the first ring, and so on outward. Nodes no edge reaches go on the outermost ring.
	/// A ring's nodes are spread evenly, each outer one near the inner node that reaches it.
	/// </summary>
	public static Drawing Place(IReadOnlyList<Node> nodes, IReadOnlyList<Edge> edges)
	{
		if (nodes.Count == 0)
		{
			return new Drawing(0, [], []);
		}

		var focus = nodes.FirstOrDefault(n => n.Focus) ?? nodes[0];
		var rings = Rings(focus, nodes, edges);
		var outer = rings.Values.Max();
		var size = 2 * (outer * RingStep + Margin);
		var centre = size / 2;

		var angles = new Dictionary<string, double>(StringComparer.Ordinal) { [focus.Id] = 0 };
		var placed = new Dictionary<string, PlacedNode>(StringComparer.Ordinal)
		{
			[focus.Id] = new PlacedNode(focus, centre, centre, 0),
		};

		for (var ring = 1; ring <= outer; ring++)
		{
			var inRing = nodes.Where(n => rings[n.Id] == ring).ToList();
			var radius = ring * RingStep;
			foreach (var (node, angle) in Angles(inRing, ring, rings, angles, edges))
			{
				angles[node.Id] = angle;
				placed[node.Id] = new PlacedNode(node, centre + radius * Math.Cos(angle), centre + radius * Math.Sin(angle), ring);
			}
		}

		var lines = edges.Select(edge => Line(edge, placed[edge.From], placed[edge.To])).ToList();
		return new Drawing(size, nodes.Select(n => placed[n.Id]).ToList(), lines);
	}

	// Steps from the focus along edges either way; a node no edge reaches sits one ring past the furthest one
	// (at least the first ring).
	private static Dictionary<string, int> Rings(Node focus, IReadOnlyList<Node> nodes, IReadOnlyList<Edge> edges)
	{
		var rings = new Dictionary<string, int>(StringComparer.Ordinal) { [focus.Id] = 0 };
		var queue = new Queue<string>([focus.Id]);
		while (queue.TryDequeue(out var id))
		{
			foreach (var next in edges.Where(e => e.From == id || e.To == id).Select(e => e.From == id ? e.To : e.From))
			{
				if (rings.TryAdd(next, rings[id] + 1))
				{
					queue.Enqueue(next);
				}
			}
		}

		var loose = Math.Max(rings.Values.Max() + 1, 1);
		foreach (var node in nodes)
		{
			rings.TryAdd(node.Id, loose);
		}

		return rings;
	}

	/// <summary>
	/// The angles of one ring's nodes. The first ring is spread evenly from the top. An outer node sits at the
	/// angle of the inner nodes that reach it, so its line runs outward rather than across the drawing; nodes
	/// that would sit too close together are pushed apart, in order around the ring.
	/// </summary>
	private static IEnumerable<(Node Node, double Angle)> Angles(List<Node> inRing, int ring, Dictionary<string, int> rings,
		Dictionary<string, double> angles, IReadOnlyList<Edge> edges)
	{
		if (inRing.Count == 0)
		{
			return [];
		}

		if (ring == 1)
		{
			return inRing.Select((node, i) => (node, -Math.PI / 2 + 2 * Math.PI * i / inRing.Count));
		}

		var gap = Math.Min(2 * Math.PI / inRing.Count, MinimumGap);
		var wanted = inRing
			.Select(node => (Node: node, Angle: ParentAngle(node, ring, rings, angles, edges)))
			.OrderBy(x => x.Angle)
			.ToList();
		var placed = new List<(Node, double)>(wanted.Count);
		var last = double.NegativeInfinity;
		foreach (var (node, angle) in wanted)
		{
			last = Math.Max(angle, last + gap);
			placed.Add((node, last));
		}

		return placed;
	}

	/// <summary>The least angle between two nodes on an outer ring, in radians, while the ring has room for it.</summary>
	private const double MinimumGap = 0.45;

	// The mean angle of the inner nodes that reach this one; a node reached by none (loose) sits at the bottom.
	private static double ParentAngle(Node node, int ring, Dictionary<string, int> rings, Dictionary<string, double> angles, IReadOnlyList<Edge> edges)
	{
		var parents = edges
			.Where(e => e.From == node.Id || e.To == node.Id)
			.Select(e => e.From == node.Id ? e.To : e.From)
			.Where(other => rings[other] == ring - 1 && angles.ContainsKey(other))
			.Select(other => angles[other])
			.ToList();
		if (parents.Count == 0)
		{
			return Math.PI / 2;
		}

		return Math.Atan2(parents.Average(Math.Sin), parents.Average(Math.Cos));
	}

	// The line between two node centres, stopped at the edge of each circle so an arrowhead lands on the rim.
	private static PlacedEdge Line(Edge edge, PlacedNode from, PlacedNode to)
	{
		var dx = to.X - from.X;
		var dy = to.Y - from.Y;
		var length = Math.Sqrt(dx * dx + dy * dy);
		if (length <= 2 * NodeRadius)
		{
			return new PlacedEdge(edge, from, to, from.X, from.Y, to.X, to.Y);
		}

		var ux = dx / length;
		var uy = dy / length;
		return new PlacedEdge(edge, from, to,
			from.X + ux * NodeRadius, from.Y + uy * NodeRadius,
			to.X - ux * NodeRadius, to.Y - uy * NodeRadius);
	}

	private static IEnumerable<JsonElement> Items(JsonElement graph, string name)
		=> graph.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array
			? list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object)
			: [];

	private static string Text(JsonElement item, string name)
		=> item.TryGetProperty(name, out var value) ? SchemaViewRenderer.ValueToString(value) : string.Empty;

	private static bool Flag(JsonElement item, string name)
		=> item.TryGetProperty(name, out var value)
			&& (value.ValueKind == JsonValueKind.True
				|| (value.ValueKind == JsonValueKind.String && value.GetString() is "1" or "true")
				|| (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number != 0));

	private static string? NullIfEmpty(string text) => string.IsNullOrEmpty(text) ? null : text;
}
