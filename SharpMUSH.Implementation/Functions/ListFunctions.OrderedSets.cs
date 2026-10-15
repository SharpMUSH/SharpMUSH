using SharpMUSH.Implementation.Common;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// RhostMUSH's set operations that keep the first list's order rather than sorting, as setdiff(),
/// setinter() and setunion() do: <c>listdiff()</c>, <c>listinter()</c> and <c>listunion()</c>. Also
/// <c>elementpos()</c>, which picks characters out of a string by position.
/// </summary>
public partial class Functions
{
	[SharpFunction(Name = "listdiff", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular, ParameterNames = ["list1", "list2", "delimiter", "output-separator", "type"])]
	public ValueTask<CallState> ListDifference(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(OrderedSetOperation(parser, (first, second, keepDuplicates) =>
			Distinct(first.Where(item => !second.Contains(item.ToPlainText())), keepDuplicates)));

	[SharpFunction(Name = "listinter", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular, ParameterNames = ["list1", "list2", "delimiter", "output-separator", "type"])]
	public ValueTask<CallState> ListIntersection(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(OrderedSetOperation(parser, (first, second, keepDuplicates) =>
			Distinct(first.Where(item => second.Contains(item.ToPlainText())), keepDuplicates)));

	[SharpFunction(Name = "listunion", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular, ParameterNames = ["list1", "list2", "delimiter", "output-separator", "type"])]
	public ValueTask<CallState> ListUnion(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(OrderedSetOperation(parser, (first, second, keepDuplicates) =>
		{
			var inFirst = first.Select(item => item.ToPlainText()).ToHashSet(StringComparer.Ordinal);
			var added = Distinct(second.Items.Where(item => !inFirst.Contains(item.ToPlainText())), keepDuplicates: false);
			return keepDuplicates
				? first.Concat(added)
				: Distinct(first.Concat(added), keepDuplicates: false);
		}));

	/// <summary>
	/// The second list of a listdiff()/listinter()/listunion(): its items, and the set of their texts
	/// for matching. Matching is exact, case included.
	/// </summary>
	private sealed class SecondList(MString[] items)
	{
		private readonly HashSet<string> _texts = items.Select(item => item.ToPlainText()).ToHashSet(StringComparer.Ordinal);
		public MString[] Items { get; } = items;
		public bool Contains(string text) => _texts.Contains(text);
	}

	/// <summary>
	/// The shared body of the ordered set operations: the two lists split on the delimiter (a space if
	/// none or empty), empty items left out; the result joined with the output separator (the delimiter
	/// if none or empty). A type of 1 keeps the first list's duplicates; anything else drops them.
	/// </summary>
	private static CallState OrderedSetOperation(IMUSHCodeParser parser,
		Func<MString[], SecondList, bool, IEnumerable<MString>> operation)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var delimiter = OptionalArgument(args, 2, MarkupText.Space);
		var separator = OptionalArgument(args, 3, delimiter);
		var keepDuplicates = args.Count > 4 && RhostNumbers.LeadingInteger(args["4"].Message.ToPlainText()) == 1;

		var first = NonEmptyItems(delimiter, args["0"].Message);
		var second = new SecondList(NonEmptyItems(delimiter, args["1"].Message));

		return new CallState(MarkupText.Join(separator, operation(first, second, keepDuplicates)));
	}

	private static MString[] NonEmptyItems(MString delimiter, MString list)
		=> Array.FindAll(MushText.SplitList(delimiter, list), item => item.Length > 0);

	/// <summary>The items in order, each text only the first time unless <paramref name="keepDuplicates"/>.</summary>
	private static IEnumerable<MString> Distinct(IEnumerable<MString> items, bool keepDuplicates)
		=> keepDuplicates ? items : items.DistinctBy(item => item.ToPlainText(), StringComparer.Ordinal);

	/// <summary>
	/// <c>elementpos(&lt;string&gt;, &lt;positions&gt;[, &lt;output separator&gt;])</c>: the characters of
	/// <c>&lt;string&gt;</c> at each of the space-separated <c>&lt;positions&gt;</c>, counting from 1, in the
	/// order asked for and as often as asked for. A position before the first character or past the
	/// last is skipped. The output separator is a space unless given, and may be empty.
	/// </summary>
	[SharpFunction(Name = "elementpos", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["string", "positions", "output-separator"])]
	public ValueTask<CallState> ElementPositions(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var characters = args["0"].Message.EnumerateGraphemes().ToArray();
		var separator = args.Count > 2 ? args["2"].Message : MarkupText.Space;

		var picked = args["1"].Message.ToPlainText()
			.Split(' ', StringSplitOptions.RemoveEmptyEntries)
			.Select(position => RhostNumbers.LeadingInteger(position))
			.Where(position => position >= 1 && position <= characters.Length)
			.Select(position => characters[position - 1]);

		return ValueTask.FromResult(new CallState(MarkupText.Join(separator, picked)));
	}
}
