using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using System.Globalization;

namespace SharpMUSH.Library.ParserInterfaces;

public record CallState(MString? Message, int Depth, MString[]? Arguments, Func<ValueTask<MString?>> ParsedMessage, bool PreserveSpaces = false)
{
	public static implicit operator CallState(MString? m) => new(m);
	public static implicit operator CallState(DBRef m) => new(m);
	public static implicit operator CallState(AnySharpObject m) => new(m.Object().DBRef);
	public static implicit operator CallState(bool m) => new(m);
	public static implicit operator CallState(int m) => new(m);
	public static implicit operator CallState(long m) => new(m);
	public static implicit operator CallState(double m) => new(m);
	public static implicit operator CallState(decimal m) => new(m);
	public static implicit operator CallState(string m) => new(m);
	public static implicit operator CallState(Error<string> m) => new(m.Value);

	public CallState(MString? Message, int Depth)
		: this(Message ?? MModule.empty(), Depth, null, () => ValueTask.FromResult(Message)) { }

	public CallState(MString? Message)
		: this(Message ?? MModule.empty(), 0, null, () => ValueTask.FromResult(Message)) { }

	public CallState(int Message) : this(Message.ToString()) { }

	public CallState(long Message) : this(Message.ToString()) { }

	public CallState(Error<string> Message) : this(Message.Value) { }

	public CallState(DBRef Message) : this(Message.ToString()) { }

	public CallState(double Message) : this(
		Message.ToString($"G{Definitions.Configurable.FloatPrecision}", CultureInfo.InvariantCulture))
	{ }

	public CallState(decimal Message) : this(Message.ToString(CultureInfo.InvariantCulture)) { }

	public CallState(string Message)
		: this(
			!string.IsNullOrEmpty(Message)
				? MModule.single(Message)
				: MModule.empty(),
			0, null,
			!string.IsNullOrEmpty(Message)
				? () => ValueTask.FromResult(MModule.single(Message))!
				: () => ValueTask.FromResult(MModule.empty())!)
	{
	}

	public CallState(bool result, string errorIfFalse = "0") :
		this(MModule.single(result ? "1" : errorIfFalse), 0, null,
			() => ValueTask.FromResult(MModule.single(result ? "1" : errorIfFalse))!)
	{
	}

	public CallState(string Message, int Depth)
		: this(!string.IsNullOrEmpty(Message)
				? MModule.single(Message)
				: MModule.empty(), Depth, null,
			!string.IsNullOrEmpty(Message)
				? () => ValueTask.FromResult(MModule.single(Message))!
				: () => ValueTask.FromResult(MModule.empty())!)
	{
	}

	private static readonly MString _emptyMString = MModule.empty();
	private static readonly Func<ValueTask<MString?>> _emptyParsedMessage = () => ValueTask.FromResult<MString?>(_emptyMString);

	public static readonly CallState EmptyArgument = new(_emptyMString, 0, [], _emptyParsedMessage);
	public static readonly CallState Empty = new(_emptyMString, 0, null, _emptyParsedMessage);

	/// <summary>
	/// Parallel to <see cref="Arguments"/>: the retained NoParse-pass parse-tree node for each
	/// command-argument slot (a <c>SharpMUSHParser.EvaluationStringContext</c>, boxed as
	/// <see cref="object"/> so this shared/plugin-packaged contract type does not have to reference
	/// the ANTLR-generated parser assembly — see the <c>PrivateAssets="all"</c> note on the
	/// <c>SharpMUSH.Parser.Generated</c> reference in SharpMUSH.Library.csproj), or <see langword="null"/>
	/// where a slot has no evaluationString (e.g. an empty comma-separated argument).
	/// <para>
	/// Populated only by the command argument-split visitor methods (<c>VisitCommaCommandArgs</c>,
	/// <c>VisitStartEqSplitCommandArgs</c>, <c>VisitStartEqSplitCommand</c>,
	/// <c>VisitStartPlainSingleCommandArg</c> in <c>SharpMUSHParserVisitor</c>), so that
	/// <c>ArgumentSplit</c> can re-visit the already-lexed/parsed subtree directly instead of
	/// re-parsing each argument's raw text a third time (avoiding a redundant lex+parse pass).
	/// </para>
	/// </summary>
	public object?[]? ArgumentContexts { get; init; }

	/// <summary>
	/// True when the parse that produced this <see cref="CallState"/> ran under ANTLR's lenient
	/// error-recovery strategy (<c>ParseInternalCore</c>'s <c>lenient</c> parameter) AND actually
	/// hit a syntax error — i.e. the tree this <see cref="CallState"/> was built from is a
	/// best-effort recovery, not a clean parse.
	/// <para>
	/// The command argument-split entry points (<c>CommandCommaArgsParse</c>,
	/// <c>CommandEqSplitParse</c>, <c>CommandEqSplitArgsParse</c>, <c>CommandSingleArgParse</c>)
	/// always run lenient, so their errors are silently swallowed at that layer by design — the
	/// original design relied on each argument's raw text getting an independent, STRICT re-parse
	/// via <c>FunctionParse</c> afterwards to actually surface a malformed argument as
	/// <c>#-1 PARSER FAILURE</c>. <c>ArgumentSplit</c>/<c>EvaluateArgumentSubtree</c> uses this flag
	/// to fall back to that strict re-parse instead of trusting the retained (possibly
	/// error-recovered) subtree, whenever the split pass that produced <see cref="ArgumentContexts"/>
	/// had errors anywhere in it.
	/// </para>
	/// </summary>
	public bool HadErrors { get; init; }
}