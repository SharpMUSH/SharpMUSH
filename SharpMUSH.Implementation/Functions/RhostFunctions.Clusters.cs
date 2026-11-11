using System.Globalization;
using System.Text.RegularExpressions;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Clusters;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// RhostMUSH's cluster functions. RhostMUSH spreads a cluster's attributes over several objects and searches them
/// all; its import here merges each cluster into one object (<see cref="ObjectClusters"/>), so each function is the
/// function it is named for, run on that object once it is known to be a cluster. A name that is not a cluster gives
/// <c>#-1 NO SUCH CLUSTER</c>, as there.
/// </summary>
public partial class Functions
{
	/// <summary>How a cluster function's first argument names the cluster.</summary>
	private enum ClusterTarget
	{
		/// <summary><c>&lt;object&gt;</c>.</summary>
		Object,

		/// <summary><c>&lt;object&gt;/&lt;attribute&gt;</c> or <c>&lt;object&gt;[/&lt;pattern&gt;]</c>: the object is before the first <c>/</c>.</summary>
		ObjectFirst,

		/// <summary><c>[&lt;object&gt;/]&lt;attribute&gt;</c>: without a <c>/</c>, the caller.</summary>
		OptionalObject,

		/// <summary><c>[&lt;object&gt;/]&lt;attribute&gt;/&lt;role&gt;</c>: with one <c>/</c>, the caller.</summary>
		OptionalObjectWithRole
	}

	[SharpFunction(Name = "cluster_get", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object/attribute"])]
	public ValueTask<CallState> ClusterGet(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> OnClusterAsync(parser, attribute, ClusterTarget.ObjectFirst, Get);

	[SharpFunction(Name = "cluster_get_eval", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular,
		ParameterNames = ["object/attribute"])]
	public ValueTask<CallState> ClusterGetEval(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> OnClusterAsync(parser, attribute, ClusterTarget.ObjectFirst, GetEval);

	[SharpFunction(Name = "cluster_xget", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object", "attribute"])]
	public ValueTask<CallState> ClusterAlternativeGet(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> OnClusterAsync(parser, attribute, ClusterTarget.Object, AlternativeGet);

	[SharpFunction(Name = "cluster_default", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.NoParse,
		ParameterNames = ["object/attribute", "default"])]
	public ValueTask<CallState> ClusterDefault(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> OnClusterAsync(parser, attribute, ClusterTarget.ObjectFirst, Default);

	[SharpFunction(Name = "cluster_edefault", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.NoParse,
		ParameterNames = ["object/attribute", "default"])]
	public ValueTask<CallState> ClusterEvaluateDefault(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> OnClusterAsync(parser, attribute, ClusterTarget.ObjectFirst, EvaluateDefault);

	[SharpFunction(Name = "cluster_hasattr", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object[/attribute]", "attribute"])]
	public ValueTask<CallState> ClusterHasAttribute(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> OnClusterAsync(parser, attribute, ClusterTarget.ObjectFirst, HasAttribute);

	[SharpFunction(Name = "cluster_lattr", MinArgs = 1, MaxArgs = 6, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object[/pattern]", "target", "type", "flags", "regexp", "tree"])]
	public ValueTask<CallState> ClusterListAttributes(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> OnClusterAsync(parser, attribute, ClusterTarget.ObjectFirst, ListAttributes);

	[SharpFunction(Name = "cluster_flags", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object[/attribute]"])]
	public ValueTask<CallState> ClusterFlags(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> OnClusterAsync(parser, attribute, ClusterTarget.ObjectFirst, Flags);

	[SharpFunction(Name = "cluster_hasflag", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object[/attribute]", "flag"])]
	public ValueTask<CallState> ClusterHasFlag(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> OnClusterAsync(parser, attribute, ClusterTarget.ObjectFirst, HasFlag);

	/// <summary>
	/// <c>cluster_attrcnt(&lt;object&gt;[/&lt;pattern&gt;][, &lt;key&gt;])</c>: how many attributes the cluster has, or how
	/// many match <c>&lt;pattern&gt;</c>, a wildcard pattern or, when it starts with <c>^</c>, a regular expression.
	/// RhostMUSH's <c>&lt;key&gt;</c> chooses how it treats a damaged attribute, which SharpMUSH has no such thing as.
	/// </summary>
	[SharpFunction(Name = "cluster_attrcnt", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object[/pattern]", "key"])]
	public ValueTask<CallState> ClusterAttributeCount(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> OnClusterAsync(parser, attribute, ClusterTarget.ObjectFirst, (inner, _) =>
		{
			var target = inner.CurrentState.Arguments["0"].Message.ToPlainText();
			var slash = target.IndexOf('/');
			var pattern = slash < 0 ? null : target[(slash + 1)..];
			return AttributePatternAsync(inner, slash < 0 ? target : target[..slash], pattern is { Length: > 0 } ? pattern : null, false,
				pattern is ['^', ..] ? IAttributeService.AttributePatternMode.Regex : IAttributeService.AttributePatternMode.Wildcard,
				async names => new CallState(await names.CountAsync(ExecutionBudget.CurrentToken)));
		}, keep: 1);

	[SharpFunction(Name = "cluster_vattrcnt", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object"])]
	public ValueTask<CallState> ClusterUserAttributeCount(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> OnClusterAsync(parser, attribute, ClusterTarget.Object, UserAttributeCount);

	[SharpFunction(Name = "cluster_set", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX,
		ParameterNames = ["object[/attribute]", "flag or attribute:value"])]
	public ValueTask<CallState> ClusterSet(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> OnClusterAsync(parser, attribute, ClusterTarget.ObjectFirst, Set);

	/// <summary>
	/// <c>cluster_wipe(&lt;object&gt;[/&lt;pattern&gt;][, &lt;regexp&gt;[, &lt;owner&gt;[, &lt;preserve&gt;]]])</c>: <c>wipe()</c> on the
	/// cluster. Only the wildcard form is taken: a 1 for the regular expression, owner or keep-the-rest form is refused
	/// rather than read as a wipe of something else.
	/// </summary>
	[SharpFunction(Name = "cluster_wipe", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX,
		ParameterNames = ["object[/pattern]", "regexp", "owner", "preserve"])]
	public ValueTask<CallState> ClusterWipe(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> parser.CurrentState.ArgumentsOrdered.Skip(1).Any(argument => argument.Value.Message.ToPlainText().Trim() is not ("" or "0"))
			? ValueTask.FromResult(new CallState(ErrorMessages.Returns.ClusterWipeWildcardOnly))
			: OnClusterAsync(parser, attribute, ClusterTarget.ObjectFirst, Wipe, keep: 1);

	[SharpFunction(Name = "cluster_u", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular,
		ParameterNames = ["object/attribute", "arguments..."])]
	public ValueTask<CallState> ClusterUserFunction(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> OnClusterAsync(parser, attribute, ClusterTarget.OptionalObject, UserAttributeFunction);

	[SharpFunction(Name = "cluster_u2", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular,
		ParameterNames = ["object/attribute", "arguments..."])]
	public ValueTask<CallState> ClusterUserFunctionAsCaller(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> OnClusterAsync(parser, attribute, ClusterTarget.OptionalObject, UserAttributeAsCaller);

	[SharpFunction(Name = "cluster_ulocal", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular | FunctionFlags.Localize,
		ParameterNames = ["object/attribute", "arguments..."])]
	public ValueTask<CallState> ClusterUserFunctionLocalized(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> OnClusterAsync(parser, attribute, ClusterTarget.OptionalObject, UserAttributeLocalized);

	[SharpFunction(Name = "cluster_u2local", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular | FunctionFlags.Localize,
		ParameterNames = ["object/attribute", "arguments..."])]
	public ValueTask<CallState> ClusterUserFunctionAsCallerLocalized(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> OnClusterAsync(parser, attribute, ClusterTarget.OptionalObject, UserAttributeAsCallerLocalized);

	[SharpFunction(Name = "cluster_udefault", MinArgs = 2, MaxArgs = 34, Flags = FunctionFlags.NoParse,
		ParameterNames = ["object/attribute", "default", "arguments..."])]
	public ValueTask<CallState> ClusterUserFunctionDefault(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> OnClusterAsync(parser, attribute, ClusterTarget.OptionalObject, UserAttributeDefault);

	[SharpFunction(Name = "cluster_uldefault", MinArgs = 2, MaxArgs = 34, Flags = FunctionFlags.NoParse | FunctionFlags.Localize,
		ParameterNames = ["object/attribute", "default", "arguments..."])]
	public ValueTask<CallState> ClusterUserFunctionLocalizedDefault(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> OnClusterAsync(parser, attribute, ClusterTarget.OptionalObject, UserAttributeLocalizedDefault);

	[SharpFunction(Name = "cluster_u2default", MinArgs = 2, MaxArgs = 34, Flags = FunctionFlags.NoParse,
		ParameterNames = ["object/attribute", "default", "arguments..."])]
	public ValueTask<CallState> ClusterUserFunctionAsCallerDefault(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> OnClusterAsync(parser, attribute, ClusterTarget.OptionalObject, UserAttributeAsCallerDefault);

	[SharpFunction(Name = "cluster_u2ldefault", MinArgs = 2, MaxArgs = 34, Flags = FunctionFlags.NoParse | FunctionFlags.Localize,
		ParameterNames = ["object/attribute", "default", "arguments..."])]
	public ValueTask<CallState> ClusterUserFunctionAsCallerLocalizedDefault(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> OnClusterAsync(parser, attribute, ClusterTarget.OptionalObject, UserAttributeAsCallerLocalizedDefault);

	[SharpFunction(Name = "cluster_ueval", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular,
		ParameterNames = ["object/attribute/role", "arguments..."])]
	public ValueTask<CallState> ClusterUEval(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> OnClusterAsync(parser, attribute, ClusterTarget.OptionalObjectWithRole, UEval);

	/// <summary>
	/// <c>cluster_grep(&lt;object&gt;, &lt;attributes&gt;, &lt;pattern&gt;[, &lt;type&gt;[, &lt;key&gt;]])</c>: the cluster's
	/// attributes, of those <c>&lt;attributes&gt;</c> names, whose value matches <c>&lt;pattern&gt;</c>, ignoring case. A
	/// pattern with no <c>*</c> or <c>?</c> matches anywhere in the value; with key 1 it must be a whole word of it.
	/// Type 1 writes the cluster's dbref in brackets ahead of the names, type 2 each as
	/// <c>&lt;dbref&gt;/&lt;attribute&gt;</c>.
	/// </summary>
	[SharpFunction(Name = "cluster_grep", MinArgs = 3, MaxArgs = 5, Flags = FunctionFlags.Regular,
		ParameterNames = ["object", "attributes", "pattern", "type", "key"])]
	public ValueTask<CallState> ClusterGrep(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
	{
		var args = parser.CurrentState.Arguments;
		var pattern = args["2"].Message.ToPlainText();
		var type = ClusterNumber(args, "3");
		Func<string, bool> matches = ClusterNumber(args, "4") != 0
			? value => ContainsWord(value, pattern)
			: pattern.IndexOfAny(['*', '?', '\\']) >= 0
				? value => MushText.IsWildcardMatch(MarkupText.Plain(value), pattern)
				: value => value.Contains(pattern, StringComparison.OrdinalIgnoreCase);

		return OnClusterAsync(parser, attribute, ClusterTarget.Object, (inner, _) =>
			ClusterSearchAsync(inner, IAttributeService.AttributePatternMode.Wildcard, type, matches));
	}

	/// <summary>
	/// <c>cluster_regrep(&lt;object&gt;, &lt;attributes&gt;, &lt;regexp&gt;[, &lt;key&gt;])</c>: <c>regrep()</c> on the cluster;
	/// with key 1, <c>&lt;attributes&gt;</c> is a regular expression too.
	/// </summary>
	[SharpFunction(Name = "cluster_regrep", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular,
		ParameterNames = ["object", "attributes", "regexp", "key"])]
	public ValueTask<CallState> ClusterRegularExpressionGrep(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> ClusterRegexGrepAsync(parser, attribute, RegexOptions.None);

	/// <summary><c>cluster_regrepi()</c>: <c>cluster_regrep()</c>, ignoring case.</summary>
	[SharpFunction(Name = "cluster_regrepi", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular,
		ParameterNames = ["object", "attributes", "regexp", "key"])]
	public ValueTask<CallState> ClusterRegularExpressionGrepCaseless(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> ClusterRegexGrepAsync(parser, attribute, RegexOptions.IgnoreCase);

	/// <summary>
	/// <c>iscluster(&lt;object&gt;)</c>: 1 when the object is a cluster, 0 when not, and <c>#-1</c> for an object the
	/// caller cannot find or does not control.
	/// </summary>
	[SharpFunction(Name = "iscluster", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object"])]
	public async ValueTask<CallState> IsCluster(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var name = parser.CurrentState.Arguments["0"].Message.ToPlainText();
		return await LocateService.Locate(parser, executor, executor, name, LocateFlags.All) is AnySharpObject target
			&& await PermissionService.Controls(executor, target)
				? new CallState(await ObjectClusters.IsClusterAsync(Mediator, target) ? "1" : "0")
				: new CallState("#-1");
	}

	/// <summary>
	/// <c>cluster_stats(&lt;object&gt;, &lt;type&gt;[, &lt;attribute&gt;])</c>, for a cluster the caller controls. Type 0 is 1;
	/// type 1 lists the cluster's numbers, its own first and then the objects merged into it; type 4 gives the cluster's
	/// dbref when it holds <c>&lt;attribute&gt;</c>. RhostMUSH's threshold, its actions and their delays (types 2, 3, 5 and
	/// 6) have no counterpart, as one object holds the whole cluster: types 2, 3 and 5 are empty and 6 gives no delay.
	/// </summary>
	[SharpFunction(Name = "cluster_stats", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object", "type", "attribute"])]
	public async ValueTask<CallState> ClusterStats(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var name = args["0"].Message.ToPlainText();
		if (await LocateService.Locate(parser, executor, executor, name, LocateFlags.All) is not AnySharpObject target
				|| await ObjectClusters.ClusterOfAsync(Mediator, target) is not IReadOnlyList<DBRef> numbers)
		{
			return new CallState(ErrorMessages.Returns.NoSuchCluster);
		}

		if (!await PermissionService.Controls(executor, target))
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var type = ClusterNumber(args, "1");
		return type switch
		{
			1 => new CallState(string.Join(' ', numbers)),
			4 => await ClusterHoldsAsync(executor, target, args.TryGetValue("2", out var attribute) ? attribute.Message.ToPlainText() : string.Empty),
			2 or 3 or 5 => CallState.Empty,
			6 => new CallState("Action: 0 |ActionFunction: 0"),
			_ => new CallState("1")
		};
	}

	/// <summary>
	/// <c>cluster_add(&lt;cluster&gt;, &lt;object&gt;)</c>. A cluster here is one object, which holds as many attributes as the
	/// game allows any object, so there is nothing to add an object to; it is refused, and changes nothing.
	/// </summary>
	[SharpFunction(Name = "cluster_add", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX,
		ParameterNames = ["cluster", "object"])]
	public ValueTask<CallState> ClusterAdd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(new CallState(ErrorMessages.Returns.ClusterIsOneObject));

	/// <summary>
	/// Finds the cluster <paramref name="target"/>'s first argument names, and runs <paramref name="plain"/> with that
	/// argument naming the cluster's object instead. Arguments past <paramref name="keep"/> are left off.
	/// </summary>
	private async ValueTask<CallState> OnClusterAsync(IMUSHCodeParser parser, SharpFunctionAttribute attribute, ClusterTarget target,
		Func<IMUSHCodeParser, SharpFunctionAttribute, ValueTask<CallState>> plain, int keep = int.MaxValue)
	{
		var state = parser.CurrentState;
		var first = state.Arguments["0"];
		var text = attribute.Flags.HasFlag(FunctionFlags.NoParse)
			? (await first.GetParsedResultAsync()).Message.ToPlainText()
			: first.Message.ToPlainText();

		var slash = text.IndexOf('/');
		var named = target switch
		{
			ClusterTarget.Object => text,
			ClusterTarget.ObjectFirst => slash < 0 ? text : text[..slash],
			ClusterTarget.OptionalObject => slash < 0 ? null : text[..slash],
			_ => slash < 0 || text.IndexOf('/', slash + 1) < 0 ? null : text[..slash]
		};
		var rest = named is null ? "/" + text : text[named.Length..];

		var executor = await state.KnownExecutorObject(Mediator);
		var found = named is null
			? executor
			: await LocateService.Locate(parser, executor, executor, named.Trim(), LocateFlags.All) is AnySharpObject located
				? located
				: (AnySharpObject?)null;
		if (found is null || !await ObjectClusters.IsClusterAsync(Mediator, found))
		{
			return new CallState(ErrorMessages.Returns.NoSuchCluster);
		}

		var arguments = state.Arguments
			.Where(argument => !int.TryParse(argument.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var position) || position < keep)
			.ToDictionary(argument => argument.Key, argument => argument.Value);
		arguments["0"] = new CallState(MarkupText.Plain(found.Object().DBRef + rest));
		return await plain(parser.Push(state with { Arguments = arguments }), attribute);
	}

	private ValueTask<CallState> ClusterRegexGrepAsync(IMUSHCodeParser parser, SharpFunctionAttribute attribute, RegexOptions options)
	{
		var args = parser.CurrentState.Arguments;
		Regex regex;
		try
		{
			regex = SoftcodeRegex.Create(args["2"].Message.ToPlainText(), options);
		}
		catch (ArgumentException)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.RegexpInvalid));
		}

		var mode = ClusterNumber(args, "3") != 0 ? IAttributeService.AttributePatternMode.Regex : IAttributeService.AttributePatternMode.Wildcard;
		return OnClusterAsync(parser, attribute, ClusterTarget.Object, async (inner, _) =>
		{
			try
			{
				return await ClusterSearchAsync(inner, mode, 0, value => value.Length > 0 && regex.IsMatch(value));
			}
			catch (RegexMatchTimeoutException)
			{
				return new CallState(ErrorMessages.Returns.RegexpTimeout);
			}
		});
	}

	/// <summary>The cluster's attributes named by the second argument whose value <paramref name="matches"/>.</summary>
	private async ValueTask<CallState> ClusterSearchAsync(IMUSHCodeParser parser, IAttributeService.AttributePatternMode mode, int type,
		Func<string, bool> matches)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var objectName = args["0"].Message.ToPlainText();
		var names = args["1"].Message.ToPlainText();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, objectName, LocateFlags.All,
			async found =>
			{
				if (!await PermissionService.CanExamine(executor, found))
				{
					return new CallState(ErrorMessages.Returns.NoSuchCluster);
				}

				var attributes = await AttributeService.LazilyGetAttributePatternAsync(executor, found,
					names.Length > 0 ? names : "*", false, mode);
				if (attributes is not IAsyncEnumerable<LazySharpAttribute> listed)
				{
					return CallState.Empty;
				}

				var dbref = found.Object().DBRef.Number;
				var matched = await MatchingValuesAsync(listed, matches, attr => type == 2 ? $"#{dbref}/{attr.LongName}" : attr.LongName);
				return new CallState(type == 1 && matched.Count > 0
					? $"[#{dbref}] {string.Join(' ', matched)}"
					: string.Join(' ', matched));
			});
	}

	/// <summary>Type 4 of <c>cluster_stats()</c>: the cluster's dbref when it holds the attribute with a value.</summary>
	private async ValueTask<CallState> ClusterHoldsAsync(AnySharpObject executor, AnySharpObject target, string attribute)
		=> attribute.Length > 0
				&& await AttributeService.GetAttributeAsync(executor, target, attribute, IAttributeService.AttributeMode.Read, parent: false)
					is SharpAttribute[] { Length: > 0 } path
				&& path[^1].Value.Length > 0
			? new CallState($"#{target.Object().DBRef.Number}")
			: CallState.Empty;

	/// <summary>Whether <paramref name="word"/> stands in <paramref name="value"/> as a whole word, ignoring case.</summary>
	private static bool ContainsWord(string value, string word)
	{
		if (word.Length == 0)
		{
			return false;
		}

		for (var at = value.IndexOf(word, StringComparison.OrdinalIgnoreCase); at >= 0;
				 at = value.IndexOf(word, at + 1, StringComparison.OrdinalIgnoreCase))
		{
			var end = at + word.Length;
			if ((at == 0 || char.IsWhiteSpace(value[at - 1])) && (end == value.Length || char.IsWhiteSpace(value[end])))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>An optional number argument read as RhostMUSH's <c>atoi</c> does: anything else is 0.</summary>
	private static int ClusterNumber(Dictionary<string, CallState> args, string key)
		=> args.TryGetValue(key, out var value)
			&& int.TryParse(ClusterLeadingInteger().Match(value.Message.ToPlainText().TrimStart()).Value, NumberStyles.AllowLeadingSign,
				CultureInfo.InvariantCulture, out var number)
				? number
				: 0;

	[GeneratedRegex(@"^[-+]?\d+")]
	private static partial Regex ClusterLeadingInteger();
}
