using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// The graph functions (<c>help graph functions</c>): graph_reach(), graph_path(), graph_fold() and
/// graph_components() walk a graph whose links a softcode function names, and json_walk() walks a JSON tree. The
/// engine keeps the queue, the visited set and the order; it reads no game data itself, so every read goes through the
/// caller's function, under the caller's permissions and limits.
/// </summary>
public partial class Functions
{
	private const string GraphCycle = "#-1 CYCLE";
	private const string GraphNoPath = "#-1 NO PATH";
	private const string GraphNoCost = "#-1 NO COST";
	private const string GraphNegativeCost = "#-1 COST MUST NOT BE NEGATIVE";
	private const string GraphConflictingOptions = "#-1 CONFLICTING OPTIONS";

	/// <summary>One call of a function a graph function was given: an attribute, or a #lambda.</summary>
	private delegate ValueTask<CallState> GraphCall(Dictionary<string, CallState> arguments);

	private union GraphCallFetch(GraphCall, CallState);

	/// <summary>A node's links, from a link attribute or from a JSON graph. An error stops the walk.</summary>
	private delegate ValueTask<Result<List<MString>>> GraphLinks(MString node);

	/// <summary>Where a walk reads its links, and the JSON graph when they come from one.</summary>
	private sealed record GraphSource(GraphLinks Links, JsonGraph? Json);

	private union GraphSourceFetch(GraphSource, CallState);

	private readonly record struct GraphOptions(string Order, bool Starts);

	/// <summary>A node in a walk's output: its key, its level and the node it was reached from.</summary>
	private readonly record struct GraphVisit(string Node, int Level, string? From);

	[SharpFunction(Name = "graph_reach", MinArgs = 2, MaxArgs = 38, Flags = FunctionFlags.Regular,
		ParameterNames = ["links", "starts", "options", "depth", "delimiter", "output-separator", "level-separator", "format", "arg..."])]
	public ValueTask<CallState> GraphReach(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ReachAsync(parser, "graph_reach", fromJson: false);

	[SharpFunction(Name = "json_graph_reach", MinArgs = 2, MaxArgs = 38, Flags = FunctionFlags.Regular,
		ParameterNames = ["graph", "starts", "options", "depth", "delimiter", "output-separator", "level-separator", "format", "arg..."])]
	public ValueTask<CallState> JsonGraphReach(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ReachAsync(parser, "json_graph_reach", fromJson: true);

	private async ValueTask<CallState> ReachAsync(IMUSHCodeParser parser, string name, bool fromJson)
	{
		var errors = new ListEvaluationErrors();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var delim = await errors.DefaultArgumentAsync(parser, 4, MarkupText.Space);
		var osep = await errors.OutputSeparatorAsync(parser, 5, MarkupText.Space);
		var lsep = await errors.OutputSeparatorAsync(parser, 6, osep, whenEmpty: osep);
		var extra = GraphExtraArguments(parser, 8);

		if (ParseGraphOptions(GraphArgument(parser, 2), ["bfs", "dfs", "post", "layers"], "bfs") is not GraphOptions options)
		{
			return errors.Complete(new CallState(GraphOptionError(GraphArgument(parser, 2), ["bfs", "dfs", "post", "layers"])));
		}

		var depthText = GraphArgument(parser, 3);
		var depth = int.MaxValue;
		if (depthText.Length > 0 && (!int.TryParse(depthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out depth) || depth < 0))
		{
			return errors.Complete(new CallState(ErrorMessages.Returns.NonNegativeInteger));
		}

		GraphSource source;
		switch (await GraphSourceAsync(parser, executor, name, fromJson, delim, extra, errors))
		{
			case CallState refusal:
				return errors.Complete(refusal);
			case GraphSource found:
				source = found;
				break;
			default:
				return errors.Complete(new CallState(MarkupText.Empty));
		}

		GraphCall? format = null;
		if (GraphArgument(parser, 7).Length > 0)
		{
			switch (await GraphCallAsync(parser, executor, GraphArgumentMarkup(parser, 7)))
			{
				case GraphCall call:
					format = call;
					break;
				case CallState refusal:
					return errors.Complete(refusal);
			}
		}

		var walk = new GraphWalk(source.Links, delim);
		var starts = walk.AddAll(GraphArgumentMarkup(parser, 1));

		return await ReachAsync(walk, starts, options, depth, osep.ToPlainText()) switch
		{
			Error<string> error => errors.Complete(new CallState(error.Value)),
			List<List<GraphVisit>> levels => errors.Complete(new CallState(
				await JoinLevelsAsync(walk, levels, format, extra, osep, lsep, errors)))
		};
	}

	[SharpFunction(Name = "graph_path", MinArgs = 3, MaxArgs = 37, Flags = FunctionFlags.Regular,
		ParameterNames = ["links", "from", "to", "options", "cost", "delimiter", "output-separator", "arg..."])]
	public ValueTask<CallState> GraphPath(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> PathAsync(parser, "graph_path", fromJson: false);

	[SharpFunction(Name = "json_graph_path", MinArgs = 3, MaxArgs = 7, Flags = FunctionFlags.Regular,
		ParameterNames = ["graph", "from", "to", "options", "cost-field", "delimiter", "output-separator"])]
	public ValueTask<CallState> JsonGraphPath(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> PathAsync(parser, "json_graph_path", fromJson: true);

	private async ValueTask<CallState> PathAsync(IMUSHCodeParser parser, string name, bool fromJson)
	{
		var errors = new ListEvaluationErrors();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var delim = await errors.DefaultArgumentAsync(parser, 5, MarkupText.Space);
		var osep = await errors.OutputSeparatorAsync(parser, 6, MarkupText.Space);
		var extra = GraphExtraArguments(parser, 7);

		if (ParseGraphOptions(GraphArgument(parser, 3), [], "") is not GraphOptions options)
		{
			return errors.Complete(new CallState(GraphOptionError(GraphArgument(parser, 3), [])));
		}

		GraphSource source;
		switch (await GraphSourceAsync(parser, executor, name, fromJson, delim, extra, errors))
		{
			case CallState refusal:
				return errors.Complete(refusal);
			case GraphSource found:
				source = found;
				break;
			default:
				return errors.Complete(new CallState(MarkupText.Empty));
		}

		GraphCall? cost = null;
		if (GraphArgument(parser, 4) is { Length: > 0 } costText)
		{
			if (source.Json is { } graph)
			{
				cost = graph.Cost(costText);
			}
			else
			{
				switch (await GraphCallAsync(parser, executor, GraphArgumentMarkup(parser, 4)))
				{
					case GraphCall call:
						cost = call;
						break;
					case CallState refusal:
						return errors.Complete(refusal);
				}
			}
		}

		var walk = new GraphWalk(source.Links, delim);
		var from = walk.Add(GraphArgumentMarkup(parser, 1));
		var targets = new HashSet<string>(walk.AddAll(GraphArgumentMarkup(parser, 2)), StringComparer.OrdinalIgnoreCase);
		if (from.Length == 0 || targets.Count == 0)
		{
			return errors.Complete(new CallState(GraphNoPath));
		}

		var route = cost is null
			? await FewestStepsAsync(walk, from, targets)
			: await LowestCostAsync(walk, from, targets, cost, extra, errors);

		return route switch
		{
			Error<string> error => errors.Complete(new CallState(error.Value)),
			List<string> path => errors.Complete(new CallState(MarkupText.Join(osep,
				(options.Starts ? path : path.Skip(1)).Select(walk.Show))))
		};
	}

	[SharpFunction(Name = "graph_fold", MinArgs = 3, MaxArgs = 37, Flags = FunctionFlags.Regular,
		ParameterNames = ["links", "root", "format", "combine", "options", "delimiter", "output-separator", "arg..."])]
	public ValueTask<CallState> GraphFold(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> FoldAsync(parser, "graph_fold", fromJson: false);

	[SharpFunction(Name = "json_graph_fold", MinArgs = 3, MaxArgs = 37, Flags = FunctionFlags.Regular,
		ParameterNames = ["graph", "root", "format", "combine", "options", "delimiter", "output-separator", "arg..."])]
	public ValueTask<CallState> JsonGraphFold(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> FoldAsync(parser, "json_graph_fold", fromJson: true);

	private async ValueTask<CallState> FoldAsync(IMUSHCodeParser parser, string name, bool fromJson)
	{
		var errors = new ListEvaluationErrors();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var delim = await errors.DefaultArgumentAsync(parser, 5, MarkupText.Space);
		var osep = await errors.OutputSeparatorAsync(parser, 6, MarkupText.Space);
		var extra = GraphExtraArguments(parser, 7);

		if (ParseGraphOptions(GraphArgument(parser, 4), ["up", "down"], "up") is not GraphOptions options)
		{
			return errors.Complete(new CallState(GraphOptionError(GraphArgument(parser, 4), ["up", "down"])));
		}

		GraphSource source;
		switch (await GraphSourceAsync(parser, executor, name, fromJson, delim, extra, errors))
		{
			case CallState refusal:
				return errors.Complete(refusal);
			case GraphSource found:
				source = found;
				break;
			default:
				return errors.Complete(new CallState(MarkupText.Empty));
		}

		GraphCall format;
		switch (await GraphCallAsync(parser, executor, GraphArgumentMarkup(parser, 2)))
		{
			case CallState refusal:
				return errors.Complete(refusal);
			case GraphCall call:
				format = call;
				break;
			default:
				return errors.Complete(new CallState(MarkupText.Empty));
		}

		GraphCall? combine = null;
		if (GraphArgument(parser, 3).Length > 0)
		{
			switch (await GraphCallAsync(parser, executor, GraphArgumentMarkup(parser, 3)))
			{
				case GraphCall call:
					combine = call;
					break;
				case CallState refusal:
					return errors.Complete(refusal);
			}
		}

		var walk = new GraphWalk(source.Links, delim);
		var root = walk.Add(GraphArgumentMarkup(parser, 1));
		var fold = new GraphFoldCalls(format, combine, extra, osep, errors);

		var result = options.Order == "down"
			? await FoldDownAsync(walk, root, fold, options.Starts)
			: await FoldUpAsync(walk, root, fold);

		return result switch
		{
			Error<string> error => errors.Complete(new CallState(error.Value)),
			MString text => errors.Complete(new CallState(text))
		};
	}

	[SharpFunction(Name = "graph_components", MinArgs = 2, MaxArgs = 35, Flags = FunctionFlags.Regular,
		ParameterNames = ["links", "nodes", "delimiter", "output-separator", "group-separator", "arg..."])]
	public ValueTask<CallState> GraphComponents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ComponentsAsync(parser, "graph_components", fromJson: false);

	[SharpFunction(Name = "json_graph_components", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular,
		ParameterNames = ["graph", "nodes", "delimiter", "output-separator", "group-separator"])]
	public ValueTask<CallState> JsonGraphComponents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ComponentsAsync(parser, "json_graph_components", fromJson: true);

	private async ValueTask<CallState> ComponentsAsync(IMUSHCodeParser parser, string name, bool fromJson)
	{
		var errors = new ListEvaluationErrors();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var delim = await errors.DefaultArgumentAsync(parser, 2, MarkupText.Space);
		var osep = await errors.OutputSeparatorAsync(parser, 3, MarkupText.Space);
		var csep = await errors.OutputSeparatorAsync(parser, 4, MarkupText.Plain("|"), whenEmpty: MarkupText.Plain("|"));
		var extra = GraphExtraArguments(parser, 5);

		GraphSource source;
		switch (await GraphSourceAsync(parser, executor, name, fromJson, delim, extra, errors))
		{
			case CallState refusal:
				return errors.Complete(refusal);
			case GraphSource found:
				source = found;
				break;
			default:
				return errors.Complete(new CallState(MarkupText.Empty));
		}

		var walk = new GraphWalk(source.Links, delim);
		var nodes = walk.AddAll(GraphArgumentMarkup(parser, 1));
		if (await walk.SpanAsync(nodes, int.MaxValue) is Error<string> error)
		{
			return errors.Complete(new CallState(error.Value));
		}

		var groups = Components(walk).Select(group => MarkupText.Join(osep, group.Select(walk.Show)));
		return errors.Complete(new CallState(MarkupText.Join(csep, groups)));
	}

	[SharpFunction(Name = "graph_json", MinArgs = 2, MaxArgs = 33, Flags = FunctionFlags.Regular,
		ParameterNames = ["links", "starts", "delimiter", "arg..."])]
	public async ValueTask<CallState> GraphJson(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var errors = new ListEvaluationErrors();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var delim = await errors.DefaultArgumentAsync(parser, 2, MarkupText.Space);
		var extra = GraphExtraArguments(parser, 3);

		GraphSource source;
		switch (await GraphSourceAsync(parser, executor, "graph_json", fromJson: false, delim, extra, errors))
		{
			case CallState refusal:
				return errors.Complete(refusal);
			case GraphSource found:
				source = found;
				break;
			default:
				return errors.Complete(new CallState(MarkupText.Empty));
		}

		var walk = new GraphWalk(source.Links, delim);
		var starts = walk.AddAll(GraphArgumentMarkup(parser, 1));
		if (await walk.SpanAsync(starts, int.MaxValue) is Error<string> error)
		{
			return errors.Complete(new CallState(error.Value));
		}

		var graph = new System.Text.Json.Nodes.JsonObject();
		foreach (var node in walk.Discovery)
		{
			graph[walk.Show(node).ToPlainText()] = new System.Text.Json.Nodes.JsonArray(walk.KnownLinks(node)
				.Select(link => (System.Text.Json.Nodes.JsonNode?)walk.Show(link).ToPlainText())
				.ToArray());
		}

		return errors.Complete(new CallState(graph.ToJsonString(JsonHelpers.RelaxedJsonOptions)));
	}

	[SharpFunction(Name = "json_tree_graph", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular,
		ParameterNames = ["json", "children"])]
	public ValueTask<CallState> JsonTreeGraph(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var children = GraphArgument(parser, 1) is { Length: > 0 } named ? named : "children";

		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(GraphArgument(parser, 0));
		}
		catch (JsonException)
		{
			return ValueTask.FromResult(new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, "json_tree_graph")));
		}

		using (document)
		{
			return ValueTask.FromResult(JsonWalkChildrenPath(children) switch
			{
				Error<string> error => new CallState(error.Value),
				JsonWalkChildren key => new CallState(TreeGraph(document.RootElement, key).ToJsonString(JsonHelpers.RelaxedJsonOptions))
			});
		}
	}

	/// <summary>
	/// json_tree_graph(): every node of the tree as a key named by its JSONPath, holding its children's paths, parents
	/// first. A top-level array is a list of roots.
	/// </summary>
	private static System.Text.Json.Nodes.JsonObject TreeGraph(JsonElement root, JsonWalkChildren children)
	{
		var graph = new System.Text.Json.Nodes.JsonObject();
		var stack = new Stack<(JsonElement Node, string Path)>();
		var roots = root.ValueKind == JsonValueKind.Array
			? root.EnumerateArray().Select((element, index) => (element, $"$[{index}]")).ToList()
			: [(root, "$")];
		for (var i = roots.Count - 1; i >= 0; i--) stack.Push(roots[i]);

		while (stack.TryPop(out var frame))
		{
			var below = JsonWalkChildNodes(frame.Node, frame.Path, children);
			graph[frame.Path] = new System.Text.Json.Nodes.JsonArray(below
				.Select(child => (System.Text.Json.Nodes.JsonNode?)child.Path)
				.ToArray());
			for (var i = below.Count - 1; i >= 0; i--) stack.Push(below[i]);
		}

		return graph;
	}

	/// <summary>The links a graph function reads: its first argument, as a link attribute or as a JSON graph.</summary>
	private async ValueTask<GraphSourceFetch> GraphSourceAsync(IMUSHCodeParser parser, AnySharpObject executor, string name,
		bool fromJson, MString delim, List<CallState> extra, ListEvaluationErrors errors)
	{
		if (fromJson)
		{
			return JsonGraph.Parse(GraphArgument(parser, 0)) switch
			{
				JsonGraph graph => (GraphSourceFetch)new GraphSource(graph.LinksAsync, graph),
				Error<string> => (GraphSourceFetch)new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, name))
			};
		}

		return await GraphCallAsync(parser, executor, GraphArgumentMarkup(parser, 0)) switch
		{
			GraphCall call => (GraphSourceFetch)new GraphSource(AttributeLinks(call, delim, extra, errors), null),
			CallState refusal => (GraphSourceFetch)refusal
		};
	}

	/// <summary>A link attribute's links: %0 the node, the caller's extra arguments after it, split by the delimiter.</summary>
	private static GraphLinks AttributeLinks(GraphCall call, MString delim, List<CallState> extra, ListEvaluationErrors errors)
		=> async node =>
		{
			var result = errors.Record(await call(GraphArguments(extra, new CallState(node))));
			var text = result.ToPlainText();
			if (text.StartsWith("#-1", StringComparison.Ordinal)) return new Error<string>(text);
			return MushText.SplitList(delim, result).ToList();
		};

	/// <summary>The arguments a graph function passes on from its own position <paramref name="from"/>.</summary>
	private static List<CallState> GraphExtraArguments(IMUSHCodeParser parser, int from)
	{
		var extra = new List<CallState>();
		for (var i = from; i < parser.CurrentState.ArgumentList.Count; i++)
		{
			extra.Add(parser.CurrentState.ArgumentList[i.ToString()]);
		}

		return extra;
	}

	/// <summary>A call's arguments: its own as %0 onward, then the caller's extra arguments.</summary>
	private static Dictionary<string, CallState> GraphArguments(List<CallState> extra, params CallState[] own)
	{
		var arguments = new Dictionary<string, CallState>();
		for (var i = 0; i < own.Length; i++) arguments[i.ToString()] = own[i];
		for (var i = 0; i < extra.Count; i++) arguments[(own.Length + i).ToString()] = extra[i];
		return arguments;
	}

	/// <summary>
	/// The nodes of a finished span, grouped so that two nodes share a group when links join them, whichever way the
	/// links point. Groups and the nodes in each keep the order the span first met them.
	/// </summary>
	private static List<List<string>> Components(GraphWalk walk)
	{
		var parent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var node in walk.Discovery) parent[node] = node;

		string Root(string node)
		{
			while (!string.Equals(parent[node], node, StringComparison.OrdinalIgnoreCase))
			{
				parent[node] = parent[parent[node]];
				node = parent[node];
			}

			return node;
		}

		foreach (var node in walk.Discovery)
		{
			foreach (var next in walk.KnownLinks(node))
			{
				parent[Root(next)] = Root(node);
			}
		}

		var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		var order = new List<List<string>>();
		foreach (var node in walk.Discovery)
		{
			var root = Root(node);
			if (!groups.TryGetValue(root, out var group))
			{
				group = [];
				groups[root] = group;
				order.Add(group);
			}

			group.Add(node);
		}

		return order;
	}

	[SharpFunction(Name = "json_walk", MinArgs = 2, MaxArgs = 33, Flags = FunctionFlags.Regular,
		ParameterNames = ["attribute", "json", "children", "output-separator", "arg..."])]
	public async ValueTask<CallState> JsonWalk(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var errors = new ListEvaluationErrors();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var osep = await errors.OutputSeparatorAsync(parser, 3, MarkupText.Space);
		var children = GraphArgument(parser, 2) is { Length: > 0 } named ? named : "children";

		var userArgs = new Dictionary<string, CallState>();
		for (var i = 4; i < parser.CurrentState.ArgumentList.Count; i++)
		{
			userArgs[i.ToString()] = parser.CurrentState.ArgumentList[i.ToString()];
		}

		GraphCall visit;
		switch (await GraphCallAsync(parser, executor, GraphArgumentMarkup(parser, 0)))
		{
			case CallState refusal:
				return errors.Complete(refusal);
			case GraphCall call:
				visit = call;
				break;
			default:
				return errors.Complete(new CallState(MarkupText.Empty));
		}

		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(GraphArgument(parser, 1));
		}
		catch (JsonException)
		{
			return errors.Complete(new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, "json_walk")));
		}

		using (document)
		{
			return JsonWalkChildrenPath(children) switch
			{
				Error<string> error => errors.Complete(new CallState(error.Value)),
				JsonWalkChildren key => errors.Complete(new CallState(MarkupText.Join(osep,
					await JsonWalkAsync(document.RootElement, key, visit, userArgs, errors))))
			};
		}
	}

	/// <summary>
	/// Every node <paramref name="starts"/> lead to, grouped by level, in <paramref name="options"/>' order. A node
	/// is in the output when a link reaches it, and a start also when the options name <c>starts</c>.
	/// </summary>
	private static async ValueTask<Result<List<List<GraphVisit>>>> ReachAsync(GraphWalk walk, List<string> starts,
		GraphOptions options, int depth, string osep)
	{
		if (await walk.SpanAsync(starts, depth) is Error<string> spanError) return spanError;

		return options.Order switch
		{
			"dfs" => await PreOrderAsync(walk, starts, options.Starts, depth),
			"post" => await PostOrderAsync(walk, starts, options.Starts, depth, osep, byHeight: false),
			"layers" => await PostOrderAsync(walk, starts, options.Starts, depth, osep, byHeight: true),
			_ => BreadthFirst(walk, starts, options.Starts)
		};
	}

	/// <summary>bfs: the starts (with <c>starts</c>), then each node in the order the walk first reached it by a link.</summary>
	private static List<List<GraphVisit>> BreadthFirst(GraphWalk walk, List<string> starts, bool withStarts)
	{
		var visits = new List<GraphVisit>();
		if (withStarts) visits.AddRange(starts.Select(start => new GraphVisit(start, 0, null)));

		var isStart = new HashSet<string>(starts, StringComparer.OrdinalIgnoreCase);
		foreach (var node in walk.LinkOrder)
		{
			if (withStarts && isStart.Contains(node)) continue;
			var from = walk.FirstFrom[node];
			visits.Add(new GraphVisit(node, isStart.Contains(node) ? walk.Distance[from] + 1 : walk.Distance[node], from));
		}

		return GroupConsecutiveLevels(visits);
	}

	/// <summary>dfs: a node, then everything below it, then its next sibling.</summary>
	private static async ValueTask<Result<List<List<GraphVisit>>>> PreOrderAsync(GraphWalk walk, List<string> starts,
		bool withStarts, int depth)
	{
		var visits = new List<GraphVisit>();
		var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var isStart = new HashSet<string>(starts, StringComparer.OrdinalIgnoreCase);

		foreach (var start in starts)
		{
			if (!visited.Add(start)) continue;
			if (withStarts && emitted.Add(start)) visits.Add(new GraphVisit(start, 0, null));

			var stack = new Stack<(string Node, int Level, int Next)>();
			stack.Push((start, 0, 0));
			while (stack.TryPop(out var frame))
			{
				var below = walk.Distance[frame.Node] < depth ? walk.KnownLinks(frame.Node) : [];
				if (frame.Next >= below.Count) continue;

				stack.Push(frame with { Next = frame.Next + 1 });
				var child = below[frame.Next];
				var (level, from) = withStarts && isStart.Contains(child) ? (0, null) : (frame.Level + 1, frame.Node);
				if (emitted.Add(child)) visits.Add(new GraphVisit(child, level, from));
				if (visited.Add(child)) stack.Push((child, level, 0));
			}
		}

		await ValueTask.CompletedTask;
		return new List<List<GraphVisit>> { visits };
	}

	/// <summary>
	/// post: everything below a node, then the node. layers (<paramref name="byHeight"/>): nodes grouped by how far
	/// below them the walk goes, lowest first. Both refuse a loop, naming it.
	/// </summary>
	private static async ValueTask<Result<List<List<GraphVisit>>>> PostOrderAsync(GraphWalk walk, List<string> starts,
		bool withStarts, int depth, string osep, bool byHeight)
	{
		var isStart = new HashSet<string>(starts, StringComparer.OrdinalIgnoreCase);
		var done = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var visits = new List<GraphVisit>();

		// Reached by a link, or a start the options ask for.
		bool Shown(string node) => walk.FirstFrom.ContainsKey(node) || withStarts && isStart.Contains(node);

		void Emit(string node, int level, string? from)
		{
			if (emitted.Add(node)) visits.Add(new GraphVisit(node, level, from));
		}

		foreach (var start in starts)
		{
			if (done.ContainsKey(start)) continue;

			var path = new List<(string Node, int Level, string? From, int Next)> { (start, 0, null, 0) };
			var onPath = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { start };
			while (path.Count > 0)
			{
				var frame = path[^1];
				var below = walk.Distance[frame.Node] < depth ? walk.KnownLinks(frame.Node) : [];
				if (frame.Next < below.Count)
				{
					path[^1] = frame with { Next = frame.Next + 1 };
					var child = below[frame.Next];
					if (onPath.Contains(child))
					{
						return CycleError(walk, path.Select(step => step.Node), child, osep);
					}

					if (!done.ContainsKey(child))
					{
						onPath.Add(child);
						path.Add(withStarts && isStart.Contains(child) ? (child, 0, null, 0) : (child, frame.Level + 1, frame.Node, 0));
					}

					continue;
				}

				path.RemoveAt(path.Count - 1);
				onPath.Remove(frame.Node);
				done[frame.Node] = below.Count == 0 ? 0 : below.Max(child => done[child]) + 1;

				if (!byHeight && Shown(frame.Node))
				{
					Emit(frame.Node, frame.Level, frame.From);
				}
			}
		}

		await ValueTask.CompletedTask;
		if (!byHeight) return new List<List<GraphVisit>> { visits };

		// layers: each emitted node by height, in the order the walk first met it within a height.
		var layered = walk.Discovery
			.Where(Shown)
			.Select(node => new GraphVisit(node, done[node],
				withStarts && isStart.Contains(node) ? null : walk.FirstFrom.GetValueOrDefault(node)))
			.GroupBy(visit => visit.Level)
			.OrderBy(group => group.Key)
			.Select(group => group.ToList())
			.ToList();
		return layered;
	}

	/// <summary><c>#-1 CYCLE</c> and the loop: from where <paramref name="child"/> sits on the path back to it, joined by <paramref name="separator"/>.</summary>
	private static Error<string> CycleError(GraphWalk walk, IEnumerable<string> path, string child, string separator)
	{
		var loop = path.SkipWhile(node => !string.Equals(node, child, StringComparison.OrdinalIgnoreCase))
			.Append(child)
			.Select(node => walk.Show(node).ToPlainText());
		return new Error<string>($"{GraphCycle} {string.Join(separator, loop)}");
	}

	private static List<List<GraphVisit>> GroupConsecutiveLevels(List<GraphVisit> visits)
	{
		var levels = new List<List<GraphVisit>>();
		foreach (var visit in visits)
		{
			if (levels.Count == 0 || levels[^1][0].Level != visit.Level) levels.Add([]);
			levels[^1].Add(visit);
		}

		return levels;
	}

	private static async ValueTask<MString> JoinLevelsAsync(GraphWalk walk, List<List<GraphVisit>> levels,
		GraphCall? format, List<CallState> extra, MString osep, MString lsep, ListEvaluationErrors errors)
	{
		var lines = new List<MString>(levels.Count);
		foreach (var level in levels)
		{
			var items = new List<MString>(level.Count);
			foreach (var visit in level)
			{
				items.Add(format is null
					? walk.Show(visit.Node)
					: errors.Record(await format(GraphArguments(extra,
						new CallState(walk.Show(visit.Node)),
						new CallState(visit.Level),
						new CallState(visit.From is null ? MarkupText.Empty : walk.Show(visit.From))))));
			}

			lines.Add(MarkupText.Join(osep, items));
		}

		return MarkupText.Join(lsep, lines);
	}

	/// <summary>graph_path() without a cost: the fewest links, the first found when two tie.</summary>
	private static async ValueTask<Result<List<string>>> FewestStepsAsync(GraphWalk walk, string from, HashSet<string> targets)
	{
		if (targets.Contains(from)) return new List<string> { from };

		var cameFrom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [from] = from };
		var queue = new Queue<string>([from]);
		while (queue.TryDequeue(out var node))
		{
			var read = await walk.LinksAsync(node);
			if (read is Error<string> failure) return failure;
			var below = read is List<string> list ? list : [];

			foreach (var next in below)
			{
				if (!cameFrom.TryAdd(next, node)) continue;
				if (targets.Contains(next)) return Trace(cameFrom, from, next);
				queue.Enqueue(next);
			}
		}

		return new Error<string>(GraphNoPath);
	}

	/// <summary>graph_path() with a cost: the lowest total, links met earlier winning a tie.</summary>
	private static async ValueTask<Result<List<string>>> LowestCostAsync(GraphWalk walk, string from, HashSet<string> targets,
		GraphCall cost, List<CallState> extra, ListEvaluationErrors errors)
	{
		var best = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { [from] = 0 };
		var cameFrom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [from] = from };
		var settled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var queue = new PriorityQueue<string, (double Cost, long Order)>();
		var order = 0L;
		queue.Enqueue(from, (0, order++));

		while (queue.TryDequeue(out var node, out var priority))
		{
			if (!settled.Add(node)) continue;
			if (targets.Contains(node)) return Trace(cameFrom, from, node);

			var read = await walk.LinksAsync(node);
			if (read is Error<string> failure) return failure;
			var below = read is List<string> list ? list : [];

			foreach (var next in below)
			{
				if (settled.Contains(next)) continue;

				var weightText = errors.Record(await cost(GraphArguments(extra,
					new CallState(walk.Show(node)),
					new CallState(walk.Show(next))))).ToPlainText().Trim();
				if (weightText.StartsWith("#-1", StringComparison.Ordinal)) return new Error<string>(weightText);
				if (!double.TryParse(weightText, NumberStyles.Float, CultureInfo.InvariantCulture, out var weight)
					|| !double.IsFinite(weight))
				{
					return new Error<string>(ErrorMessages.Returns.Number);
				}

				if (weight < 0) return new Error<string>(GraphNegativeCost);

				var total = priority.Cost + weight;
				if (!double.IsFinite(total)) return new Error<string>(ErrorMessages.Returns.Number);
				if (best.TryGetValue(next, out var known) && known <= total) continue;
				best[next] = total;
				cameFrom[next] = node;
				queue.Enqueue(next, (total, order++));
			}
		}

		return new Error<string>(GraphNoPath);
	}

	private static List<string> Trace(Dictionary<string, string> cameFrom, string from, string to)
	{
		var path = new List<string> { to };
		while (!string.Equals(path[^1], from, StringComparison.OrdinalIgnoreCase))
		{
			path.Add(cameFrom[path[^1]]);
		}

		path.Reverse();
		return path;
	}

	/// <summary>The functions graph_fold() runs on each node, and the separator that joins their lists.</summary>
	private sealed record GraphFoldCalls(GraphCall Format, GraphCall? Combine, List<CallState> Extra, MString Osep,
		ListEvaluationErrors Errors)
	{
		public async ValueTask<(MString Shown, MString Value)> NodeAsync(MString node, List<MString> shown, List<MString> values)
		{
			var value = Combine is null
				? MarkupText.Empty
				: Errors.Record(await Combine(GraphArguments(Extra,
					new CallState(node),
					new CallState(MarkupText.Join(Osep, values)))));

			var formatted = Errors.Record(await Format(GraphArguments(Extra,
				new CallState(node),
				new CallState(MarkupText.Join(Osep, shown)),
				new CallState(value))));

			return (formatted, value);
		}
	}

	/// <summary>graph_fold() up: children first; a node shared by two parents is folded once.</summary>
	private static async ValueTask<Result<MString>> FoldUpAsync(GraphWalk walk, string root, GraphFoldCalls fold)
	{
		var results = new Dictionary<string, (MString Shown, MString Value)>(StringComparer.OrdinalIgnoreCase);
		var path = new List<(string Node, List<string> Below, int Next)>();
		var onPath = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		async ValueTask<Error<string>?> EnterAsync(string node)
		{
			var read = await walk.LinksAsync(node);
			if (read is Error<string> failure) return failure;
			var below = read is List<string> list ? list : [];

			onPath.Add(node);
			path.Add((node, below, 0));
			return null;
		}

		if (await EnterAsync(root) is Error<string> rootError) return rootError;

		while (path.Count > 0)
		{
			var frame = path[^1];
			if (frame.Next < frame.Below.Count)
			{
				path[^1] = frame with { Next = frame.Next + 1 };
				var child = frame.Below[frame.Next];
				if (onPath.Contains(child))
				{
					return CycleError(walk, path.Select(step => step.Node), child, fold.Osep.ToPlainText());
				}

				if (!results.ContainsKey(child) && await EnterAsync(child) is Error<string> childError) return childError;
				continue;
			}

			path.RemoveAt(path.Count - 1);
			onPath.Remove(frame.Node);
			results[frame.Node] = await fold.NodeAsync(walk.Show(frame.Node),
				frame.Below.Select(child => results[child].Shown).ToList(),
				frame.Below.Select(child => results[child].Value).ToList());
		}

		return results[root].Shown;
	}

	/// <summary>
	/// graph_fold() down: parents first. A node is folded once every parent the walk found has been, with all
	/// their results; the output is each node's result in that order.
	/// </summary>
	private static async ValueTask<Result<MString>> FoldDownAsync(GraphWalk walk, string root, GraphFoldCalls fold, bool withRoot)
	{
		// The cycle check and the parents of each node, as a fold up would find them.
		var parents = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase) { [root] = [] };
		var path = new List<(string Node, List<string> Below, int Next)>();
		var onPath = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var entered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		async ValueTask<Error<string>?> EnterAsync(string node)
		{
			var read = await walk.LinksAsync(node);
			if (read is Error<string> failure) return failure;
			var below = read is List<string> list ? list : [];

			entered.Add(node);
			onPath.Add(node);
			path.Add((node, below, 0));
			return null;
		}

		if (await EnterAsync(root) is Error<string> rootError) return rootError;

		while (path.Count > 0)
		{
			var frame = path[^1];
			if (frame.Next < frame.Below.Count)
			{
				path[^1] = frame with { Next = frame.Next + 1 };
				var child = frame.Below[frame.Next];
				if (onPath.Contains(child))
				{
					return CycleError(walk, path.Select(step => step.Node), child, fold.Osep.ToPlainText());
				}

				if (!parents.TryGetValue(child, out var known)) parents[child] = known = [];

				known.Add(frame.Node);
				if (!entered.Contains(child) && await EnterAsync(child) is Error<string> childError) return childError;
				continue;
			}

			path.RemoveAt(path.Count - 1);
			onPath.Remove(frame.Node);
		}

		// Kahn's order: a node waits for all its parents; ties go to the node the walk met first.
		var waiting = parents.ToDictionary(pair => pair.Key, pair => pair.Value.Count, StringComparer.OrdinalIgnoreCase);
		var ready = new Queue<string>([root]);
		var results = new Dictionary<string, (MString Shown, MString Value)>(StringComparer.OrdinalIgnoreCase);
		var output = new List<MString>();
		while (ready.TryDequeue(out var node))
		{
			results[node] = await fold.NodeAsync(walk.Show(node),
				parents[node].Select(parent => results[parent].Shown).ToList(),
				parents[node].Select(parent => results[parent].Value).ToList());
			if (withRoot || !string.Equals(node, root, StringComparison.OrdinalIgnoreCase)) output.Add(results[node].Shown);

			foreach (var child in walk.KnownLinks(node))
			{
				if (--waiting[child] == 0) ready.Enqueue(child);
			}
		}

		return MarkupText.Join(fold.Osep, output);
	}

	/// <summary>Where json_walk() finds a node's children: a key, or a JSONPath from the node.</summary>
	private sealed record JsonWalkChildren(string? Key, Json.Path.JsonPath? Path, string PathText);

	private static Result<JsonWalkChildren> JsonWalkChildrenPath(string children)
	{
		if (!children.StartsWith('$')) return new JsonWalkChildren(children, null, "");

		try
		{
			return new JsonWalkChildren(null, Json.Path.JsonPath.Parse(children), children[1..]);
		}
		catch (Exception ex) when (ex is FormatException or ArgumentException or JsonException or Json.Path.PathParseException)
		{
			return new Error<string>(ErrorMessages.Returns.PathNotFound);
		}
	}

	/// <summary>
	/// json_walk()'s walk: parents first, each node handed over whole, with its level from 0, its place among
	/// its siblings from 1 and its JSONPath. A top-level array is a list of roots. Empty results are left out.
	/// </summary>
	private static async ValueTask<List<MString>> JsonWalkAsync(JsonElement root, JsonWalkChildren children,
		GraphCall visit, Dictionary<string, CallState> userArgs, ListEvaluationErrors errors)
	{
		var results = new List<MString>();
		var stack = new Stack<(JsonElement Node, int Level, int Place, string Path)>();
		var roots = root.ValueKind == JsonValueKind.Array
			? root.EnumerateArray().Select((element, index) => (element, 0, index + 1, $"$[{index}]")).ToList()
			: [(root, 0, 1, "$")];
		for (var i = roots.Count - 1; i >= 0; i--) stack.Push(roots[i]);

		while (stack.TryPop(out var frame))
		{
			var arguments = new Dictionary<string, CallState>
			{
				["0"] = new(frame.Node.GetRawText()),
				["1"] = new(frame.Level),
				["2"] = new(frame.Place),
				["3"] = new(frame.Path)
			};
			foreach (var argument in userArgs) arguments[argument.Key] = argument.Value;

			var result = errors.Record(await visit(arguments));
			if (result.Length > 0) results.Add(result);

			var below = JsonWalkChildNodes(frame.Node, frame.Path, children);
			for (var i = below.Count - 1; i >= 0; i--)
			{
				stack.Push((below[i].Node, frame.Level + 1, i + 1, below[i].Path));
			}
		}

		return results;
	}

	private static List<(JsonElement Node, string Path)> JsonWalkChildNodes(JsonElement node, string path, JsonWalkChildren children)
	{
		if (node.ValueKind != JsonValueKind.Object) return [];

		JsonElement container;
		string containerPath;
		if (children.Key is { } key)
		{
			if (!node.TryGetProperty(key, out container)) return [];
			containerPath = path + JsonPathSegment(key);
		}
		else
		{
			var match = children.Path!.Evaluate(System.Text.Json.Nodes.JsonNode.Parse(node.GetRawText()));
			if (match.Matches is not { Count: > 0 } || match.Matches[0].Value is null) return [];
			container = JsonSerializer.Deserialize<JsonElement>(match.Matches[0].Value!.ToJsonString());
			containerPath = path + children.PathText;
		}

		return container.ValueKind switch
		{
			JsonValueKind.Array => container.EnumerateArray().Select((element, index) => (element, $"{containerPath}[{index}]")).ToList(),
			JsonValueKind.Object => container.EnumerateObject().Select(property => (property.Value, containerPath + JsonPathSegment(property.Name))).ToList(),
			_ => []
		};
	}

	private static string JsonPathSegment(string key)
		=> PlainJsonKey().IsMatch(key)
			? "." + key
			: "['" + EscapeJsonPathKey(key) + "']";

	/// <summary>A key as RFC 9535 writes it between single quotes: <c>\</c>, <c>'</c> and control characters escaped.</summary>
	private static string EscapeJsonPathKey(string key)
	{
		var escaped = new StringBuilder(key.Length);
		foreach (var character in key)
		{
			escaped.Append(character switch
			{
				'\\' => "\\\\",
				'\'' => "\\'",
				'\b' => "\\b",
				'\f' => "\\f",
				'\n' => "\\n",
				'\r' => "\\r",
				'\t' => "\\t",
				< ' ' => $"\\u{(int)character:x4}",
				_ => character.ToString()
			});
		}

		return escaped.ToString();
	}

	[GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
	private static partial Regex PlainJsonKey();

	/// <summary>The links of every node a walk reads, each read once, with keys compared ignoring case.</summary>
	private sealed class GraphWalk(GraphLinks links, MString delim)
	{
		private readonly Dictionary<string, List<string>> _links = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, MString> _spelling = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>Fewest links from a start, for every node <see cref="SpanAsync"/> reached.</summary>
		public Dictionary<string, int> Distance { get; } = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>The node each node was first reached from by a link.</summary>
		public Dictionary<string, string> FirstFrom { get; } = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>Nodes in the order a link first reached them.</summary>
		public List<string> LinkOrder { get; } = [];

		/// <summary>Starts, then nodes, in the order the breadth-first span first met them.</summary>
		public List<string> Discovery { get; } = [];

		/// <summary>The node's key: its first spelling's text.</summary>
		public string Add(MString node)
		{
			var text = node.ToPlainText();
			if (_spelling.TryGetValue(text, out var first)) return first.ToPlainText();
			_spelling[text] = node;
			return text;
		}

		public List<string> AddAll(MString list) => AddItems(MushText.SplitList(delim, list));

		private List<string> AddItems(IEnumerable<MString> items)
		{
			var keys = new List<string>();
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var item in items)
			{
				if (item.ToPlainText().Length == 0) continue;
				var key = Add(item);
				if (seen.Add(key)) keys.Add(key);
			}

			return keys;
		}

		public MString Show(string key) => _spelling[key];

		/// <summary>Links already read; empty for a node that was not.</summary>
		public List<string> KnownLinks(string key) => _links.GetValueOrDefault(key) ?? [];

		/// <summary>The node's links, read from the source the first time.</summary>
		public async ValueTask<Result<List<string>>> LinksAsync(string key)
		{
			if (_links.TryGetValue(key, out var known)) return known;

			return await links(_spelling[key]) switch
			{
				Error<string> failure => failure,
				List<MString> read => _links[key] = AddItems(read)
			};
		}

		/// <summary>
		/// The breadth-first span every order starts from: each node within <paramref name="depth"/> links of a
		/// start, with the fewest links to it, its links read once.
		/// </summary>
		public async ValueTask<Result<Success>> SpanAsync(List<string> starts, int depth)
		{
			var queue = new Queue<string>();
			foreach (var start in starts)
			{
				if (!Distance.TryAdd(start, 0)) continue;
				Discovery.Add(start);
				queue.Enqueue(start);
			}

			while (queue.TryDequeue(out var node))
			{
				if (Distance[node] >= depth) continue;
				var read = await LinksAsync(node);
				if (read is Error<string> failure) return failure;
				var below = read is List<string> list ? list : [];

				foreach (var next in below)
				{
					if (FirstFrom.TryAdd(next, node)) LinkOrder.Add(next);
					if (!Distance.TryAdd(next, Distance[node] + 1)) continue;
					Discovery.Add(next);
					queue.Enqueue(next);
				}
			}

			return new Success();
		}
	}

	private static string GraphArgument(IMUSHCodeParser parser, int index)
		=> GraphArgumentMarkup(parser, index).ToPlainText().Trim();

	private static MString GraphArgumentMarkup(IMUSHCodeParser parser, int index)
		=> parser.CurrentState.ArgumentsOrdered.TryGetValue(index.ToString(), out var argument)
			? argument.Message ?? MarkupText.Empty
			: MarkupText.Empty;

	private async ValueTask<GraphCallFetch> GraphCallAsync(IMUSHCodeParser parser, AnySharpObject executor, MString specification)
	{
		var text = specification.ToPlainText().Trim();
		if (HelperFunctions.IsLambdaOrApply(text))
		{
			return (GraphCall)(arguments => AttributeService.EvaluateAttributeFunctionResultAsync(parser, executor, specification, arguments));
		}

		return await AttributeService.FetchAttributeFunctionAsync(parser, executor, text) switch
		{
			AttributeFunction function => (GraphCall)(arguments => CallAttributeWithArgumentsAsync(parser, function, arguments)),
			CallState refusal => refusal
		};
	}

	/// <summary>The option words: one order out of <paramref name="orders"/>, and <c>starts</c>.</summary>
	private static GraphOptions? ParseGraphOptions(string text, string[] orders, string defaultOrder)
	{
		string? order = null;
		var starts = false;
		foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			var lower = word.ToLowerInvariant();
			if (lower == "starts")
			{
				starts = true;
			}
			else if (orders.Contains(lower) && (order is null || order == lower))
			{
				order = lower;
			}
			else
			{
				return null;
			}
		}

		return new GraphOptions(order ?? defaultOrder, starts);
	}

	private static string GraphOptionError(string text, string[] orders)
		=> text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
			.Select(word => word.ToLowerInvariant())
			.All(word => word == "starts" || orders.Contains(word))
			? GraphConflictingOptions
			: ErrorMessages.Returns.NoSuchOption;

	/// <summary>
	/// A graph written as JSON: an object with a key for each node, holding a list of the nodes it links to, or an
	/// object whose keys are those nodes and whose values are the links' own fields. Keys compare ignoring case; a
	/// node with no key links nowhere.
	/// </summary>
	private sealed class JsonGraph
	{
		private readonly Dictionary<string, List<MString>> _links = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, Dictionary<string, JsonElement>> _edges = new(StringComparer.OrdinalIgnoreCase);

		public static Result<JsonGraph> Parse(string text)
		{
			JsonDocument document;
			try
			{
				document = JsonDocument.Parse(text);
			}
			catch (JsonException exception)
			{
				return new Error<string>(exception.Message);
			}

			using (document)
			{
				if (document.RootElement.ValueKind != JsonValueKind.Object) return new Error<string>("not an object");

				var graph = new JsonGraph();
				foreach (var node in document.RootElement.EnumerateObject())
				{
					if (graph.Read(node) is Error<string> error) return error;
				}

				return graph;
			}
		}

		private Result<Success> Read(JsonProperty node)
		{
			if (!_links.TryGetValue(node.Name, out var links)) _links[node.Name] = links = [];

			switch (node.Value.ValueKind)
			{
				case JsonValueKind.Null:
					return new Success();
				case JsonValueKind.Array:
					foreach (var item in node.Value.EnumerateArray())
					{
						switch (item.ValueKind)
						{
							case JsonValueKind.String:
								links.Add(MarkupText.Plain(item.GetString()!));
								break;
							case JsonValueKind.Number:
								links.Add(MarkupText.Plain(item.GetRawText()));
								break;
							default:
								return new Error<string>("a link is not a name");
						}
					}

					return new Success();
				case JsonValueKind.Object:
					if (!_edges.TryGetValue(node.Name, out var edges))
					{
						_edges[node.Name] = edges = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
					}

					foreach (var edge in node.Value.EnumerateObject())
					{
						links.Add(MarkupText.Plain(edge.Name));
						edges[edge.Name] = edge.Value.Clone();
					}

					return new Success();
				default:
					return new Error<string>("links are not a list or an object");
			}
		}

		public ValueTask<Result<List<MString>>> LinksAsync(MString node)
			=> ValueTask.FromResult<Result<List<MString>>>(_links.TryGetValue(node.ToPlainText(), out var links) ? links : []);

		/// <summary>json_graph_path()'s cost: the link's field <paramref name="field"/>, as %0 to %1 name the link.</summary>
		public GraphCall Cost(string field)
			=> arguments =>
			{
				var from = arguments["0"].Message?.ToPlainText() ?? "";
				var to = arguments["1"].Message?.ToPlainText() ?? "";
				var weight = _edges.TryGetValue(from, out var edges) && edges.TryGetValue(to, out var edge)
					&& edge.ValueKind == JsonValueKind.Object && edge.TryGetProperty(field, out var value)
						? value.ValueKind switch
						{
							JsonValueKind.Number => value.GetRawText(),
							JsonValueKind.String => value.GetString()!,
							_ => GraphNoCost
						}
						: GraphNoCost;
				return ValueTask.FromResult(new CallState(weight));
			};
	}
}
