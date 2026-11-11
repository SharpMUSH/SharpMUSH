using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Clusters;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

/// <summary>
/// RhostMUSH's <c>@cluster</c>, reached as <c>@cluster</c> under the RhostMUSH dialect. Its import merges each cluster
/// into one object (<see cref="ObjectClusters"/>), so the switches that work on a cluster's attributes are the
/// commands they are named for, run on that object, and those that build or tune a cluster have nothing to do.
/// </summary>
public partial class Commands
{
	private const string ClusterIsOneObjectNotice =
		"@cluster: a cluster is one object here, which holds every attribute it has; there are no objects to add or take out, and no threshold or action.";

	/// <summary>
	/// <c>@cluster/&lt;switch&gt; &lt;cluster&gt;[/&lt;attribute&gt;][=&lt;value&gt;]</c>. <c>/list</c> shows the cluster;
	/// <c>/set</c>, <c>/edit</c>, <c>/wipe</c> and <c>/trigger</c> are <c>@set</c>, <c>@edit</c>, <c>@wipe</c> and
	/// <c>@trigger</c> on the cluster's object, and <c>/grep[/regexp] &lt;cluster&gt;=&lt;attributes&gt;,&lt;pattern&gt;</c> is
	/// <c>@grep</c>. The text after the cluster's name is passed on as typed, so each command evaluates it as it does.
	/// </summary>
	[SharpCommand(Name = "@RHOST_CLUSTER", Switches = ["NEW", "ADD", "DEL", "CLEAR", "CUT", "REPAIR", "LIST", "THRESHOLD", "ACTION",
			"FUNC", "REACTION", "EDIT", "SET", "GREP", "REGEXP", "WIPE", "OWNER", "PRESERVE", "TRIGGER"],
		Behavior = CB.Default | CB.EqSplit | CB.NoParse | CB.RSNoParse | CB.NoGagged | CB.NoGuest, MinArgs = 0, MaxArgs = 2,
		ParameterNames = ["cluster/attribute", "value"])]
	public async ValueTask<Option<CallState>> RhostCluster(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var args = parser.CurrentState.Arguments;
		var left = args.TryGetValue("0", out var arg0) ? arg0.Message.ToPlainText() : string.Empty;
		var right = args.TryGetValue("1", out var arg1) ? arg1.Message.ToPlainText() : null;

		if (switches.Overlaps(["NEW", "ADD", "DEL", "CLEAR", "CUT", "REPAIR", "THRESHOLD", "ACTION", "FUNC", "REACTION"]))
		{
			await NotifyService.Notify(executor, ClusterIsOneObjectNotice);
			return new CallState(ClusterIsOneObjectNotice);
		}

		if (switches.Overlaps(["OWNER", "PRESERVE"]) || switches.Contains("REGEXP") && !switches.Contains("GREP"))
		{
			const string Refused = "@cluster: /wipe takes a wildcard pattern only here; it has no /regexp, /owner or /preserve.";
			await NotifyService.Notify(executor, Refused);
			return new CallState(Refused);
		}

		var slash = TopLevelIndexOf(left, '/');
		var named = (slash < 0 ? left : left[..slash]).Trim();
		var attribute = slash < 0 ? string.Empty : left[slash..];
		if (named.Length == 0)
		{
			await NotifyService.Notify(executor, "@cluster: name a cluster.");
			return new CallState(ErrorMessages.Returns.NoSuchCluster);
		}

		var evaluated = (await parser.FunctionParse(MarkupText.Plain(named)))?.Message.ToPlainText().Trim() ?? named;
		switch (await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, evaluated, LocateFlags.All))
		{
			case Error<CallState> error:
				return error.Value;
			case AnySharpObject found when await ObjectClusters.ClusterOfAsync(Mediator, found) is IReadOnlyList<DBRef> numbers:
				return await ClusterSwitchAsync(parser, executor, found, numbers, switches, attribute, right);
			default:
				await NotifyService.Notify(executor, $"@cluster: {evaluated} is not a cluster.");
				return new CallState(ErrorMessages.Returns.NoSuchCluster);
		}
	}

	private async ValueTask<Option<CallState>> ClusterSwitchAsync(IMUSHCodeParser parser, AnySharpObject executor, AnySharpObject cluster,
		IReadOnlyList<DBRef> numbers, HashSet<string> switches, string attribute, string? value)
	{
		var dbref = cluster.Object().DBRef;
		var assigned = value is null ? string.Empty : "=" + value;
		string? line = switches switch
		{
			_ when switches.Contains("SET") => $"@set {dbref}{attribute}{assigned}",
			_ when switches.Contains("EDIT") => $"@edit {dbref}{attribute}{assigned}",
			_ when switches.Contains("WIPE") => $"@wipe {dbref}{attribute}",
			_ when switches.Contains("TRIGGER") => $"@trigger {dbref}{attribute}{assigned}",
			_ when switches.Contains("GREP") => GrepLine(dbref, switches.Contains("REGEXP"), value ?? string.Empty),
			_ => null
		};

		if (line is not null)
		{
			return await parser.CommandParse(MarkupText.Plain(line));
		}

		if (!await PermissionService.CanExamine(executor, cluster))
		{
			return await NotifyService.PermissionDeniedNotControlled(executor, cluster);
		}

		var count = await AttributeService.LazilyGetAttributePatternAsync(executor, cluster, "**", false,
				IAttributeService.AttributePatternMode.Wildcard) is IAsyncEnumerable<LazySharpAttribute> attributes
			? await attributes.CountAsync(ExecutionBudget.CurrentToken)
			: 0;
		var listing = $"Cluster {cluster.Object().Name}({dbref.Number}): one object, answering to {string.Join(' ', numbers)}; {count} attribute(s).";
		await NotifyService.Notify(executor, listing);
		return new CallState(listing);
	}

	/// <summary><c>@cluster/grep &lt;cluster&gt;=&lt;attributes&gt;,&lt;pattern&gt;</c> as <c>@grep</c>.</summary>
	private static string GrepLine(DBRef dbref, bool regexp, string value)
	{
		var comma = TopLevelIndexOf(value, ',');
		var (names, pattern) = comma < 0 ? ("*", value) : (value[..comma].Trim(), value[(comma + 1)..]);
		var kind = regexp ? "/regexp" : pattern.IndexOfAny(['*', '?']) >= 0 ? "/wild" : string.Empty;
		return $"@grep{kind} {dbref}/{(names.Length == 0 ? "*" : names)}={pattern}";
	}

	/// <summary>Where <paramref name="separator"/> first stands outside brackets, braces and parentheses, or -1.</summary>
	private static int TopLevelIndexOf(string text, char separator)
	{
		var depth = 0;
		for (var i = 0; i < text.Length; i++)
		{
			switch (text[i])
			{
				case '\\' or '%':
					i++;
					break;
				case '[' or '{' or '(':
					depth++;
					break;
				case ']' or '}' or ')':
					depth = Math.Max(0, depth - 1);
					break;
				default:
					if (text[i] == separator && depth == 0) return i;
					break;
			}
		}

		return -1;
	}
}
