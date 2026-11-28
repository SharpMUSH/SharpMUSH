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
	private const string GraphNegativeCost = "#-1 COST MUST NOT BE NEGATIVE";
	private const string GraphConflictingOptions = "#-1 CONFLICTING OPTIONS";

	/// <summary>One call of a function a graph function was given: an attribute, or a #lambda.</summary>
	private delegate ValueTask<CallState> GraphCall(Dictionary<string, CallState> arguments);

	private union GraphCallFetch(GraphCall, CallState);

	private readonly record struct GraphOptions(string Order, bool Starts);

	/// <summary>A node in a walk's output: its key, its level and the node it was reached from.</summary>
	private readonly record struct GraphVisit(string Node, int Level, string? From);

	[SharpFunction(Name = "graph_reach", MinArgs = 2, MaxArgs = 8, Flags = FunctionFlags.Regular,
		ParameterNames = ["links", "starts", "options", "depth", "delimiter", "output-separator", "level-separator", "format"])]
	public async ValueTask<CallState> GraphReach(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var errors = new ListEvaluationErrors();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var delim = await errors.DefaultArgumentAsync(parser, 4, MarkupText.Space);
		var osep = await errors.OutputSeparatorAsync(parser, 5, MarkupText.Space);
		var lsep = await errors.OutputSeparatorAsync(parser, 6, osep, whenEmpty: osep);

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

		GraphCall links;
		switch (await GraphCallAsync(parser, executor, GraphArgumentMarkup(parser, 0)))
		{
			case CallState refusal:
				return errors.Complete(refusal);
			case GraphCall call:
				links = call;
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

		var walk = new GraphWalk(links, delim, errors);
		var starts = walk.AddAll(GraphArgumentMarkup(parser, 1));

		return await ReachAsync(walk, starts, options, depth, osep.ToPlainText()) switch
		{
			Error<string> error => errors.Complete(new CallState(error.Value)),
			List<List<GraphVisit>> levels => errors.Complete(new CallState(
				await JoinLevelsAsync(walk, levels, format, osep, lsep, errors)))
		};
	}

	[SharpFunction(Name = "graph_path", MinArgs = 3, MaxArgs = 7, Flags = FunctionFlags.Regular,
		ParameterNames = ["links", "from", "to", "options", "cost", "delimiter", "output-separator"])]
	public async ValueTask<CallState> GraphPath(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var errors = new ListEvaluationErrors();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var delim = await errors.DefaultArgumentAsync(parser, 5, MarkupText.Space);
		var osep = await errors.OutputSeparatorAsync(parser, 6, MarkupText.Space);

		if (ParseGraphOptions(GraphArgument(parser, 3), [], "") is not GraphOptions options)
		{
			return errors.Complete(new CallState(GraphOptionError(GraphArgument(parser, 3), [])));
		}

		GraphCall links;
		switch (await GraphCallAsync(parser, executor, GraphArgumentMarkup(parser, 0)))
		{
			case CallState refusal:
				return errors.Complete(refusal);
			case GraphCall call:
				links = call;
				break;
			default:
				return errors.Complete(new CallState(MarkupText.Empty));
		}

		GraphCall? cost = null;
		if (GraphArgument(parser, 4).Length > 0)
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

		var walk = new GraphWalk(links, delim, errors);
		var from = walk.Add(GraphArgumentMarkup(parser, 1));
		var targets = new HashSet<string>(walk.AddAll(GraphArgumentMarkup(parser, 2)), StringComparer.OrdinalIgnoreCase);
		if (from.Length == 0 || targets.Count == 0)
		{
			return errors.Complete(new CallState(GraphNoPath));
		}

		var found = cost is null
			? await FewestStepsAsync(walk, from, targets)
			: await LowestCostAsync(walk, from, targets, cost, errors);

		return found switch
		{
			Error<string> error => errors.Complete(new CallState(error.Value)),
			List<string> path => errors.Complete(new CallState(MarkupText.Join(osep,
				(options.Starts ? path : path.Skip(1)).Select(walk.Show))))
		};
	}

	[SharpFunction(Name = "graph_fold", MinArgs = 3, MaxArgs = 7, Flags = FunctionFlags.Regular,
		ParameterNames = ["links", "root", "format", "combine", "options", "delimiter", "output-separator"])]
	public async ValueTask<CallState> GraphFold(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var errors = new ListEvaluationErrors();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var delim = await errors.DefaultArgumentAsync(parser, 5, MarkupText.Space);
		var osep = await errors.OutputSeparatorAsync(parser, 6, MarkupText.Space);

		if (ParseGraphOptions(GraphArgument(parser, 4), ["up", "down"], "up") is not GraphOptions options)
		{
			return errors.Complete(new CallState(GraphOptionError(GraphArgument(parser, 4), ["up", "down"])));
		}

		GraphCall links;
		switch (await GraphCallAsync(parser, executor, GraphArgumentMarkup(parser, 0)))
		{
			case CallState refusal:
				return errors.Complete(refusal);
			case GraphCall call:
				links = call;
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

		var walk = new GraphWalk(links, delim, errors);
		var root = walk.Add(GraphArgumentMarkup(parser, 1));
		var fold = new GraphFoldCalls(format, combine, osep, errors);

		var result = options.Order == "down"
			? await FoldDownAsync(walk, root, fold, options.Starts)
			: await FoldUpAsync(walk, root, fold);

		return result switch
		{
			Error<string> error => errors.Complete(new CallState(error.Value)),
			MString text => errors.Complete(new CallState(text))
		};
	}

	[SharpFunction(Name = "graph_components", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular,
		ParameterNames = ["links", "nodes", "delimiter", "output-separator", "group-separator"])]
	public async ValueTask<CallState> GraphComponents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var errors = new ListEvaluationErrors();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var delim = await errors.DefaultArgumentAsync(parser, 2, MarkupText.Space);
		var osep = await errors.OutputSeparatorAsync(parser, 3, MarkupText.Space);
		var csep = await errors.OutputSeparatorAsync(parser, 4, MarkupText.Plain("|"), whenEmpty: MarkupText.Plain("|"));

		GraphCall links;
		switch (await GraphCallAsync(parser, executor, GraphArgumentMarkup(parser, 0)))
		{
			case CallState refusal:
				return errors.Complete(refusal);
			case GraphCall call:
				links = call;
				break;
			default:
				return errors.Complete(new CallState(MarkupText.Empty));
		}

		var walk = new GraphWalk(links, delim, errors);
		var nodes = walk.AddAll(GraphArgumentMarkup(parser, 1));
		if (await walk.SpanAsync(nodes, int.MaxValue) is Error<string> error)
		{
			return errors.Complete(new CallState(error.Value));
		}

		var groups = Components(walk).Select(group => MarkupText.Join(osep, group.Select(walk.Show)));
		return errors.Complete(new CallState(MarkupText.Join(csep, groups)));
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
		GraphCall? format, MString osep, MString lsep, ListEvaluationErrors errors)
	{
		var lines = new List<MString>(levels.Count);
		foreach (var level in levels)
		{
			var items = new List<MString>(level.Count);
			foreach (var visit in level)
			{
				items.Add(format is null
					? walk.Show(visit.Node)
					: errors.Record(await format(new Dictionary<string, CallState>
					{
						["0"] = new(walk.Show(visit.Node)),
						["1"] = new(visit.Level),
						["2"] = new(visit.From is null ? MarkupText.Empty : walk.Show(visit.From))
					})));
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
		GraphCall cost, ListEvaluationErrors errors)
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

				var weightText = errors.Record(await cost(new Dictionary<string, CallState>
				{
					["0"] = new(walk.Show(node)),
					["1"] = new(walk.Show(next))
				})).ToPlainText().Trim();
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
	private sealed record GraphFoldCalls(GraphCall Format, GraphCall? Combine, MString Osep, ListEvaluationErrors Errors)
	{
		public async ValueTask<(MString Shown, MString Value)> NodeAsync(MString node, List<MString> shown, List<MString> values)
		{
			var value = Combine is null
				? MarkupText.Empty
				: Errors.Record(await Combine(new Dictionary<string, CallState>
				{
					["0"] = new(node),
					["1"] = new(MarkupText.Join(Osep, values))
				}));

			var formatted = Errors.Record(await Format(new Dictionary<string, CallState>
			{
				["0"] = new(node),
				["1"] = new(MarkupText.Join(Osep, shown)),
				["2"] = new(value)
			}));

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
	private sealed class GraphWalk(GraphCall links, MString delim, ListEvaluationErrors errors)
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

		public List<string> AddAll(MString list)
		{
			var keys = new List<string>();
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var item in MushText.SplitList(delim, list))
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

		/// <summary>The node's links, from the link function the first time. A result starting #-1 is an error.</summary>
		public async ValueTask<Result<List<string>>> LinksAsync(string key)
		{
			if (_links.TryGetValue(key, out var known)) return known;

			var result = errors.Record(await links(new Dictionary<string, CallState> { ["0"] = new(_spelling[key]) }));
			var text = result.ToPlainText();
			if (text.StartsWith("#-1", StringComparison.Ordinal)) return new Error<string>(text);

			var below = AddAll(result);
			_links[key] = below;
			return below;
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
}
