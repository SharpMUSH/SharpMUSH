using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// RhostMUSH's <c>u2()</c> family and <c>privatize()</c>.
/// </summary>
/// <remarks>
/// RhostMUSH's default build (<c>TINY_U</c>) makes u() run an attribute as the object that holds it,
/// as PennMUSH's does, and keeps its older u() as u2(): the attribute's code runs as the object calling
/// u2(), so <c>me</c>, %! and %@ are the caller. The attribute is found and permission-checked as for
/// u().
/// </remarks>
public partial class Functions
{
	[SharpFunction(Name = "u2", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular, ParameterNames = ["object/attribute", "arguments..."])]
	public ValueTask<CallState> UserAttributeAsCaller(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> CallAttributeAsCallerAsync(parser);

	[SharpFunction(Name = "u2local", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular | FunctionFlags.Localize, ParameterNames = ["object/attribute", "arguments..."])]
	public ValueTask<CallState> UserAttributeAsCallerLocalized(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> CallAttributeAsCallerAsync(parser);

	[SharpFunction(Name = "u2default", MinArgs = 2, MaxArgs = 34, Flags = FunctionFlags.NoParse, ParameterNames = ["object/attribute", "default", "arguments..."])]
	public ValueTask<CallState> UserAttributeAsCallerDefault(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> UserAttributeDefaultAsync(parser, asCaller: true);

	[SharpFunction(Name = "u2ldefault", MinArgs = 2, MaxArgs = 34, Flags = FunctionFlags.NoParse | FunctionFlags.Localize, ParameterNames = ["object/attribute", "default", "arguments..."])]
	public ValueTask<CallState> UserAttributeAsCallerLocalizedDefault(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> UserAttributeDefaultAsync(parser, asCaller: true);

	/// <summary>
	/// u2()'s body: the attribute named by argument 0, fetched as u() fetches it, run as the caller with
	/// the rest of the arguments as %0 onward. A missing attribute gives nothing, as with u().
	/// </summary>
	private async ValueTask<CallState> CallAttributeAsCallerAsync(IMUSHCodeParser parser)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arguments = parser.CurrentState.ArgumentsOrdered.Skip(1)
			.Select((value, i) => new KeyValuePair<string, CallState>(i.ToString(), value.Value))
			.ToDictionary();

		return await AttributeService.FetchAttributeFunctionAsync(parser, executor,
				parser.CurrentState.Arguments["0"].Message.ToPlainText()) switch
		{
			AttributeFunction function => await CallAttributeWithArgumentsAsync(parser, function with { Owner = executor }, arguments),
			CallState refusal when refusal.Message.ToPlainText() == ErrorMessages.Returns.NoSuchAttribute => CallState.Empty,
			CallState refusal => refusal
		};
	}

	/// <summary>
	/// <c>privatize(&lt;on&gt;)</c> turns RhostMUSH's register privacy on or off for the rest of the
	/// evaluation: while it is on, localized code (<c>localize()</c>, <c>ulocal()</c> and the rest) starts
	/// with no q-registers. <c>privatize(&lt;on&gt;, &lt;code&gt;[, &lt;registers&gt;[, &lt;omit&gt;[, &lt;multi&gt;]]])</c>
	/// instead evaluates <c>&lt;code&gt;</c> with the registers localized, emptied first when
	/// <c>&lt;on&gt;</c> is true, and the switch set to <c>&lt;on&gt;</c> only while it runs.
	/// </summary>
	/// <remarks>
	/// <c>&lt;registers&gt;</c> limits both to the registers it names: each of its letters and digits
	/// names one, or, when <c>&lt;multi&gt;</c> is 1, each space-separated word does. The others pass
	/// through, so what the code sets in them stays set. A true <c>&lt;omit&gt;</c> turns the list
	/// around: the registers it names are the ones that pass through.
	/// </remarks>
	/// <summary>RhostMUSH's answer when privatize()'s first argument is empty.</summary>
	private const string PrivatizeNeedsItsSwitch = "#-1 FUNCTION (PRIVATIZE) EXPECTS 1 ARGUMENT [RECEIVED 0]";

	[SharpFunction(Name = "privatize", MinArgs = 1, MaxArgs = 5, Flags = FunctionFlags.NoParse, ParameterNames = ["on", "code", "registers", "omit", "multi"])]
	public async ValueTask<CallState> Privatize(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var switchText = (await args["0"].GetParsedResultAsync())?.Message.ToPlainText() ?? string.Empty;
		if (switchText.Length == 0)
		{
			return new CallState(PrivatizeNeedsItsSwitch);
		}

		var on = RhostNumbers.LeadingInteger(switchText) != 0;
		var privacy = parser.CurrentState.RegisterPrivacy;
		if (args.Count == 1)
		{
			if (privacy is not null) privacy.Enabled = on;
			return CallState.Empty;
		}

		var isLocalized = await LocalizedRegistersAsync(parser, args);
		var previous = privacy?.Enabled ?? false;
		if (privacy is not null) privacy.Enabled = on;
		try
		{
			return await EvaluatePrivatelyAsync(parser, args["1"].Message, on, isLocalized);
		}
		finally
		{
			if (privacy is not null) privacy.Enabled = previous;
		}
	}

	/// <summary>Which registers privatize() localizes, from its third to fifth arguments.</summary>
	private static async ValueTask<Func<string, bool>> LocalizedRegistersAsync(IMUSHCodeParser parser, OrderedArguments args)
	{
		if (args.Count < 3) return _ => true;

		var omit = args.Count > 3 && args["3"].Message.Length > 0
			&& RhostNumbers.LeadingInteger((await args["3"].GetParsedResultAsync())?.Message.ToPlainText() ?? string.Empty) != 0;
		var multi = args.Count > 4 && RhostNumbers.LeadingInteger(args["4"].Message.ToPlainText()) != 0;
		var list = (await args["2"].GetParsedResultAsync())?.Message.ToPlainText() ?? string.Empty;

		if (list.Length == 0) return _ => !omit;

		var named = multi
			? list.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Select(name => name.ToUpperInvariant()).ToHashSet()
			: list.Where(char.IsAsciiLetterOrDigit).Select(letter => char.ToUpperInvariant(letter).ToString()).ToHashSet();

		return name => named.Contains(name.ToUpperInvariant()) != omit;
	}

	/// <summary>
	/// Evaluates <paramref name="code"/> over a copy of the q-registers, emptied of the localized ones
	/// first when <paramref name="empty"/>. Afterwards the caller keeps its own values of the localized
	/// registers and takes whatever the code left in the others.
	/// </summary>
	private static async ValueTask<CallState> EvaluatePrivatelyAsync(IMUSHCodeParser parser, MString code, bool empty,
		Func<string, bool> isLocalized)
	{
		var frames = parser.CurrentState.Registers.Reverse()
			.Select(frame => new Dictionary<string, MString>(frame, frame.Comparer))
			.ToList();
		if (frames.Count == 0) frames.Add([]);

		var outer = parser.CurrentState.Registers.TryPeek(out var top) ? top : null;
		var inner = frames[^1];
		if (empty)
		{
			foreach (var name in inner.Keys.Where(isLocalized).ToList()) inner.Remove(name);
		}

		var result = await parser.With(state => state with { Registers = new(frames) },
			async evaluating => await evaluating.FunctionParse(code)) ?? CallState.Empty;

		if (outer is not null)
		{
			foreach (var name in outer.Keys.Concat(inner.Keys).Where(name => !isLocalized(name)).Distinct().ToList())
			{
				if (inner.TryGetValue(name, out var value)) outer[name] = value;
				else outer.Remove(name);
			}
		}

		return result;
	}
}
