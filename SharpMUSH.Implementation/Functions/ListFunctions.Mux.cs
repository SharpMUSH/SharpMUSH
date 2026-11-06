using SharpMUSH.Implementation.Common;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// List functions TinyMUX games call that PennMUSH has no name for. Behaviour follows TinyMUX's documentation.
/// </summary>
public partial class Functions
{
	/// <summary>
	/// The words <c>caplist()</c> leaves in lower case when they are neither the first nor the last of the list.
	/// </summary>
	private static readonly HashSet<string> CaplistSmallWords = new(StringComparer.OrdinalIgnoreCase)
	{
		"a", "an", "the", "and", "or", "but", "for", "nor", "of", "in", "on", "at", "to", "by", "is", "as", "if",
		"it", "so", "vs", "via"
	};

	/// <summary>
	/// TinyMUX's <c>butlast()</c>, also spelled <c>lrest()</c>: every word but the last, joined by the delimiter.
	/// A list of one word or none gives nothing.
	/// </summary>
	[SharpFunction(Name = "butlast", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["list", "delimiter"])]
	[SharpFunction(Name = "lrest", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["list", "delimiter"])]
	public ValueTask<CallState> ButLast(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		if (args.Count == 0) return ValueTask.FromResult(CallState.Empty);

		var delimiter = ArgHelpers.Delimiter(args, 1);
		var words = MushText.SplitList(delimiter, args["0"].Message ?? MarkupText.Empty);
		return ValueTask.FromResult(new CallState(MarkupText.Join(delimiter, words.SkipLast(1))));
	}

	/// <summary>
	/// TinyMUX's <c>delextract()</c>: the list without <c>count</c> words from position <c>first</c>. A first
	/// position below 1 is 1, and a count below 1 gives the list back as it came.
	/// </summary>
	[SharpFunction(Name = "delextract", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular,
		ParameterNames = ["list", "first", "count", "delimiter"])]
	public ValueTask<CallState> DeleteExtract(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var list = args["0"].Message ?? MarkupText.Empty;
		var first = Math.Max(1L, ArgHelpers.ParseInteger(args["1"].Message?.ToPlainText()));
		var count = (long)ArgHelpers.ParseInteger(args["2"].Message?.ToPlainText());
		if (count < 1) return ValueTask.FromResult(new CallState(list));

		var last = first + count - 1;
		var delimiter = ArgHelpers.Delimiter(args, 3);
		var kept = MushText.SplitList(delimiter, list).Where((_, index) => index + 1 < first || index + 1 > last);
		return ValueTask.FromResult(new CallState(MarkupText.Join(delimiter, kept)));
	}

	/// <summary>
	/// TinyMUX's <c>caplist()</c>: each word with its first letter in capitals, except the short words of
	/// <see cref="CaplistSmallWords"/>, which go to lower case unless they open or close the list.
	/// </summary>
	[SharpFunction(Name = "caplist", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular,
		ParameterNames = ["list", "delimiter", "osep"])]
	public ValueTask<CallState> CapList(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var delimiter = ArgHelpers.Delimiter(args, 1);
		var separator = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, delimiter);
		var words = MushText.SplitList(delimiter, args["0"].Message ?? MarkupText.Empty);

		var titled = words.Select((word, index) =>
			index > 0 && index < words.Length - 1 && CaplistSmallWords.Contains(word.ToPlainText())
				? word.Apply(text => text.ToLowerInvariant())
				: CapitaliseFirstLetter(word));
		return ValueTask.FromResult(new CallState(MarkupText.Join(separator, titled)));
	}

	/// <summary>The word with its first character in capitals when that character is a letter.</summary>
	private static MString CapitaliseFirstLetter(MString word)
		=> word.Length > 0 && char.IsLetter(word.ToPlainText()[0])
			? MarkupText.Concat(word.Substring(0, 1).Apply(text => text.ToUpperInvariant()), word.Substring(1, word.Length - 1))
			: word;

	/// <summary>
	/// TinyMUX's <c>lcat()</c>: the words of the list joined by the output separator, which is nothing unless one
	/// is given.
	/// </summary>
	[SharpFunction(Name = "lcat", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular,
		ParameterNames = ["list", "delimiter", "osep"])]
	public ValueTask<CallState> ListConcatenate(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var delimiter = ArgHelpers.Delimiter(args, 1);
		var separator = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, MarkupText.Empty);
		return ValueTask.FromResult(new CallState(MarkupText.Join(separator,
			MushText.SplitList(delimiter, args["0"].Message ?? MarkupText.Empty))));
	}
}
