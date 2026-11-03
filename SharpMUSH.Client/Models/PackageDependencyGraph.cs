namespace SharpMUSH.Client.Models;

/// <summary>Where a package in the dependency graph stands on this game.</summary>
public enum PackageGraphState
{
	/// <summary>Listed by the source and not installed.</summary>
	Available,

	/// <summary>Installed on this game.</summary>
	Installed,

	/// <summary>Waiting in the change queue.</summary>
	Queued,

	/// <summary>Needed by a listed package, but neither listed by the source nor installed.</summary>
	Missing,
}

/// <summary>
/// The dependency graph of a set of packages, laid out in columns: packages that need nothing on the left, and each
/// package one column right of the furthest package it needs, so every line runs from a package back to what it needs.
/// Holds no UI state, so a test can check the drawing without a browser.
/// </summary>
public static class PackageDependencyGraph
{
	public const double NodeWidth = 200;
	public const double NodeHeight = 44;
	public const double ColumnGap = 72;
	public const double RowGap = 12;

	/// <summary>How many times the columns are reordered to bring each package near those it is joined to.</summary>
	private const int Sweeps = 4;

	/// <summary>One package: its id, the name drawn for it, where it stands, and whether it is a bridge.</summary>
	public sealed record Node(string Id, string Label, PackageGraphState State, bool Bridge = false);

	/// <summary><paramref name="Dependent"/> needs <paramref name="Dependency"/>; both are node ids.</summary>
	public sealed record Edge(string Dependent, string Dependency);

	/// <summary>A node in place: its box's top-left corner and its column.</summary>
	public sealed record PlacedNode(Node Node, double X, double Y, int Column);

	/// <summary>A line from the left side of the package that needs to the right side of the package it needs.</summary>
	public sealed record PlacedEdge(Edge Edge, double X1, double Y1, double X2, double Y2)
	{
		/// <summary>The SVG path: a curve leaving and arriving level, so lines into one box fan out instead of crossing it.</summary>
		public string Path
		{
			get
			{
				var bend = Math.Max(24, (X1 - X2) / 2);
				return FormattableString.Invariant($"M {X1:0.#} {Y1:0.#} C {X1 - bend:0.#} {Y1:0.#}, {X2 + bend:0.#} {Y2:0.#}, {X2:0.#} {Y2:0.#}");
			}
		}
	}

	/// <summary>
	/// The drawing: the joined packages in place with the lines between them, its size, and the packages joined to no
	/// other (needing none and needed by none), which are listed apart rather than drawn.
	/// </summary>
	public sealed record Drawing(
		IReadOnlyList<PlacedNode> Nodes,
		IReadOnlyList<PlacedEdge> Edges,
		IReadOnlyList<Node> Unjoined,
		double Width,
		double Height);

	/// <summary>
	/// Lays out <paramref name="nodes"/> joined by <paramref name="edges"/>. A repeated node id, an edge to a node not
	/// given, an edge from a node to itself and a repeated edge are left out. A cycle is broken where the search first
	/// meets it, so every package still gets a column.
	/// </summary>
	public static Drawing Place(IReadOnlyList<Node> nodes, IReadOnlyList<Edge> edges)
	{
		var byId = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
		foreach (var node in nodes) byId.TryAdd(node.Id, node);

		var kept = edges
			.Where(e => byId.ContainsKey(e.Dependent) && byId.ContainsKey(e.Dependency)
				&& !string.Equals(e.Dependent, e.Dependency, StringComparison.OrdinalIgnoreCase))
			.Select(e => new Edge(byId[e.Dependent].Id, byId[e.Dependency].Id))
			.Distinct()
			.ToList();

		var joined = new HashSet<string>(kept.SelectMany(e => new[] { e.Dependent, e.Dependency }), StringComparer.Ordinal);
		var unjoined = byId.Values.Where(n => !joined.Contains(n.Id)).OrderBy(n => n.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
		var drawn = byId.Values.Where(n => joined.Contains(n.Id)).ToList();
		if (drawn.Count == 0) return new Drawing([], [], unjoined, 0, 0);

		var needs = kept.ToLookup(e => e.Dependent, e => e.Dependency, StringComparer.Ordinal);
		var neededBy = kept.ToLookup(e => e.Dependency, e => e.Dependent, StringComparer.Ordinal);
		var columns = Columns(drawn, needs);
		var order = Order(drawn, columns, needs, neededBy);

		var heights = Heights(order, needs, neededBy);
		var placed = new Dictionary<string, PlacedNode>(StringComparer.Ordinal);
		for (var column = 0; column < order.Count; column++)
		{
			foreach (var node in order[column])
			{
				placed[node.Id] = new PlacedNode(node, column * (NodeWidth + ColumnGap), heights[node.Id], column);
			}
		}

		var lines = kept
			.Select(e => (Edge: e, From: placed[e.Dependent], To: placed[e.Dependency]))
			.Select(x => new PlacedEdge(x.Edge, x.From.X, x.From.Y + NodeHeight / 2, x.To.X + NodeWidth, x.To.Y + NodeHeight / 2))
			.ToList();

		var width = order.Count * NodeWidth + (order.Count - 1) * ColumnGap;
		var height = heights.Values.Max() + NodeHeight;
		return new Drawing([.. order.SelectMany(c => c).Select(n => placed[n.Id])], lines, unjoined, width, height);
	}

	/// <summary>
	/// The packages <paramref name="id"/> needs, directly or through others (<paramref name="upstream"/>), or the
	/// packages that need it (otherwise), not counting <paramref name="id"/> itself.
	/// </summary>
	public static IReadOnlySet<string> Reach(string id, IReadOnlyList<Edge> edges, bool upstream)
	{
		var next = upstream
			? edges.ToLookup(e => e.Dependent, e => e.Dependency, StringComparer.OrdinalIgnoreCase)
			: edges.ToLookup(e => e.Dependency, e => e.Dependent, StringComparer.OrdinalIgnoreCase);
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var queue = new Queue<string>([id]);
		while (queue.TryDequeue(out var current))
		{
			foreach (var other in next[current])
			{
				if (!string.Equals(other, id, StringComparison.OrdinalIgnoreCase) && seen.Add(other)) queue.Enqueue(other);
			}
		}

		return seen;
	}

	/// <summary>
	/// The ids in <paramref name="ids"/> with everything they need, directly or through others: what a filtered graph
	/// draws, so a shown package never loses the packages it needs.
	/// </summary>
	public static IReadOnlySet<string> WithDependencies(IEnumerable<string> ids, IReadOnlyList<Edge> edges)
	{
		var all = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
		foreach (var id in all.ToList()) all.UnionWith(Reach(id, edges, upstream: true));
		return all;
	}

	// Each package's top edge. A column keeps its order and a row's spacing, but sits as near as it can to the packages
	// each of its packages is joined to, so a package needed by many sits among them rather than packed at the top of
	// its column. Sweeps right then left, as the ordering does, each time against both neighbouring columns.
	private static Dictionary<string, double> Heights(List<List<Node>> order, ILookup<string, string> needs, ILookup<string, string> neededBy)
	{
		const double pitch = NodeHeight + RowGap;
		var tallest = order.Max(c => c.Count);
		var heights = new Dictionary<string, double>(StringComparer.Ordinal);
		foreach (var column in order)
		{
			var offset = (tallest - column.Count) * pitch / 2;
			for (var i = 0; i < column.Count; i++) heights[column[i].Id] = offset + i * pitch;
		}

		for (var sweep = 0; sweep < Sweeps; sweep++)
		{
			var sequence = sweep % 2 == 0 ? Enumerable.Range(0, order.Count) : Enumerable.Range(0, order.Count).Reverse();
			foreach (var c in sequence)
			{
				var wanted = order[c]
					.Select(n => needs[n.Id].Concat(neededBy[n.Id]).Select(o => heights[o]).DefaultIfEmpty(heights[n.Id]).Average())
					.ToList();
				var spaced = Spaced(wanted, pitch);
				for (var i = 0; i < order[c].Count; i++) heights[order[c][i].Id] = spaced[i];
			}
		}

		var top = heights.Values.Min();
		foreach (var id in heights.Keys.ToList()) heights[id] -= top;
		return heights;
	}

	/// <summary>
	/// The heights nearest to <paramref name="wanted"/>, in least squares, that keep their order at least
	/// <paramref name="pitch"/> apart: with each height less its row's share of the spacing, that is the nearest
	/// non-decreasing sequence, which pooling adjacent values that break the order finds.
	/// </summary>
	private static double[] Spaced(IReadOnlyList<double> wanted, double pitch)
	{
		var blocks = new List<(double Sum, int Count)>();
		for (var i = 0; i < wanted.Count; i++)
		{
			blocks.Add((wanted[i] - i * pitch, 1));
			while (blocks.Count > 1 && blocks[^2].Sum / blocks[^2].Count > blocks[^1].Sum / blocks[^1].Count)
			{
				var merged = (blocks[^2].Sum + blocks[^1].Sum, blocks[^2].Count + blocks[^1].Count);
				blocks.RemoveRange(blocks.Count - 2, 2);
				blocks.Add(merged);
			}
		}

		var result = new double[wanted.Count];
		var row = 0;
		foreach (var (sum, count) in blocks)
		{
			for (var k = 0; k < count; k++, row++) result[row] = sum / count + row * pitch;
		}

		return result;
	}

	// A package's column: 0 when it needs nothing drawn, else one past the furthest column of what it needs. An edge
	// back into the search's own path (a cycle) is not followed.
	private static Dictionary<string, int> Columns(IReadOnlyList<Node> nodes, ILookup<string, string> needs)
	{
		var columns = new Dictionary<string, int>(StringComparer.Ordinal);
		var open = new HashSet<string>(StringComparer.Ordinal);

		int Column(string id)
		{
			if (columns.TryGetValue(id, out var known)) return known;
			open.Add(id);
			var column = 0;
			foreach (var dependency in needs[id])
			{
				if (open.Contains(dependency)) continue;
				column = Math.Max(column, Column(dependency) + 1);
			}

			open.Remove(id);
			columns[id] = column;
			return column;
		}

		foreach (var node in nodes) Column(node.Id);
		return columns;
	}

	// Each column's packages top to bottom: first by name, then, sweeping right and back left, by the mean row of the
	// packages each is joined to in the columns already ordered (a package with none keeps its place).
	private static List<List<Node>> Order(IReadOnlyList<Node> nodes, Dictionary<string, int> columns,
		ILookup<string, string> needs, ILookup<string, string> neededBy)
	{
		var order = Enumerable.Range(0, columns.Values.Max() + 1)
			.Select(c => nodes.Where(n => columns[n.Id] == c).OrderBy(n => n.Label, StringComparer.CurrentCultureIgnoreCase).ToList())
			.ToList();

		// Rows are counted from each column's middle, as the columns are drawn centred, so they compare across columns.
		var rows = new Dictionary<string, double>(StringComparer.Ordinal);
		void Measure(List<Node> column)
		{
			var offset = -(column.Count - 1) / 2.0;
			for (var i = 0; i < column.Count; i++) rows[column[i].Id] = offset + i;
		}

		order.ForEach(Measure);
		for (var sweep = 0; sweep < Sweeps; sweep++)
		{
			var rightward = sweep % 2 == 0;
			var sequence = rightward ? Enumerable.Range(1, order.Count - 1) : Enumerable.Range(0, order.Count - 1).Reverse();
			foreach (var c in sequence)
			{
				var neighbours = rightward ? needs : neededBy;
				order[c] = order[c]
					.Select((node, i) => (Node: node, Key: neighbours[node.Id].Select(n => rows[n]).DefaultIfEmpty(rows[node.Id]).Average(), Index: i))
					.OrderBy(x => x.Key).ThenBy(x => x.Index)
					.Select(x => x.Node)
					.ToList();
				Measure(order[c]);
			}
		}

		return order;
	}
}
