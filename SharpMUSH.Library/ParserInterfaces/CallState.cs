using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Library.ParserInterfaces;

public record CallState(MString? Message, int Depth, MString[]? Arguments, Func<ValueTask<MString?>> ParsedMessage, bool PreserveSpaces = false)
{
	/// <summary>
	/// Evaluates the text this state stands for. A state built from text that is already evaluated —
	/// nearly every one — is given <see cref="OwnMessage"/>, and its delegate is made only when something
	/// asks for it rather than for every node of every evaluation.
	/// </summary>
	public Func<ValueTask<MString?>> ParsedMessage
	{
		get => ReferenceEquals(_parsedMessage, OwnMessage) ? () => ValueTask.FromResult(_builtWith) : _parsedMessage;
		init => _parsedMessage = value;
	}

	private readonly Func<ValueTask<MString?>> _parsedMessage = ParsedMessage;

	/// <summary>
	/// The text this state was built with, which <see cref="OwnMessage"/> evaluates to. A copy keeps it,
	/// so a <c>with</c> that changes <see cref="Message"/> (compressing spaces, say) still evaluates to the
	/// text as it was, as the delegate this replaces did.
	/// </summary>
	private readonly MString? _builtWith = Message;

	/// <summary>
	/// The <see cref="ParsedMessage"/> to build a state with when its text is already evaluated: the state
	/// then evaluates to the message it was built with. Never invoked itself.
	/// </summary>
	public static readonly Func<ValueTask<MString?>> OwnMessage = () => ValueTask.FromResult<MString?>(null);

	/// <summary>
	/// The text this state carries; never null — a state built without one (the command argument-split
	/// carriers, whose payload is <see cref="Arguments"/>) carries <see cref="MarkupText.Empty"/>.
	/// </summary>
	public MString Message { get; init; } = Message ?? MarkupText.Empty;

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
		: this(Message, Depth, null, OwnMessage) { }

	public CallState(MString? Message)
		: this(Message, 0, null, OwnMessage) { }

	public CallState(int Message) : this(Message.ToString()) { }

	public CallState(long Message) : this(Message.ToString()) { }

	public CallState(Error<string> Message) : this(Message.Value) { }

	public CallState(DBRef Message) : this(Message.ToString()) { }

	public CallState(double Message) : this(MushNumber.Unparse(Message)) { }

	public CallState(decimal Message) : this(MushNumber.Unparse(Message)) { }

	public CallState(string Message)
		: this(!string.IsNullOrEmpty(Message) ? MarkupText.Plain(Message) : MarkupText.Empty, 0, null, OwnMessage)
	{
	}

	public CallState(bool result, string errorIfFalse = "0") :
		this(MarkupText.Plain(result ? "1" : errorIfFalse), 0, null, OwnMessage)
	{
	}

	public CallState(string Message, int Depth)
		: this(!string.IsNullOrEmpty(Message) ? MarkupText.Plain(Message) : MarkupText.Empty, Depth, null, OwnMessage)
	{
	}

	public static readonly CallState EmptyArgument = new(MarkupText.Empty, 0, [], OwnMessage);
	public static readonly CallState Empty = new(MarkupText.Empty, 0, null, OwnMessage);

	/// <summary>
	/// Parallel to <see cref="Arguments"/>: the retained NoParse-pass parse-tree node for each
	/// command-argument slot (a <c>SharpMUSHParser.IEvaluationStringContext</c>, boxed as
	/// <see cref="object"/> so this shared/plugin-packaged contract type does not have to reference
	/// the ANTLR-generated parser assembly — see the <c>PrivateAssets="all"</c> note on the
	/// <c>SharpMUSH.Parser.Generated</c> reference in SharpMUSH.Library.csproj), or <see langword="null"/>
	/// where a slot has no evaluationString (e.g. an empty comma-separated argument).
	/// <para>
	/// Populated only by the command argument-split visitor methods (<c>VisitCommaCommandArgs</c>,
	/// <c>VisitStartEqSplitCommandArgs</c>, <c>VisitStartEqSplitCommand</c>,
	/// <c>VisitStartPlainSingleCommandArg</c> in <c>SharpMUSHParserVisitor</c>), so that
	/// <c>CommandArgumentSplitter.SplitAsync</c> can re-visit the already-lexed/parsed subtree directly instead of
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
	/// <c>#-1 PARSER FAILURE</c>. <c>CommandArgumentSplitter.EvaluateArgumentSubtree</c> uses this flag
	/// to fall back to that strict re-parse instead of trusting the retained (possibly
	/// error-recovered) subtree, whenever the split pass that produced <see cref="ArgumentContexts"/>
	/// had errors anywhere in it.
	/// </para>
	/// </summary>
	public bool HadErrors { get; init; }

	/// <summary>
	/// True when the text could not be parsed at all, so nothing in it ran; <see cref="Message"/> is the
	/// <c>#-1 PARSER FAILURE</c> saying why. A line a player types that ends this way is answered with it.
	/// </summary>
	public bool IsParseFailure { get; init; }

	/// <summary>
	/// <paramref name="message"/> at <paramref name="depth"/> as a plain evaluated value carrying only this
	/// state's <see cref="HadErrors"/>. This state itself when it already is exactly that, which an
	/// evaluated argument nearly always is, so binding it to a call copies nothing.
	/// </summary>
	public CallState AsValue(MString message, int depth)
		=> ReferenceEquals(message, Message) && Depth == depth && Arguments is null
			&& ReferenceEquals(_parsedMessage, OwnMessage) && ReferenceEquals(_builtWith, message)
			&& !PreserveSpaces && ArgumentContexts is null && !IsParseFailure && ParsedResult is null
				? this
				: new CallState(message, depth) { HadErrors = HadErrors };

	/// <summary>Optional full-result counterpart to the published text-only deferred delegate.</summary>
	public Func<ValueTask<CallState?>>? ParsedResult { get; init; }

	/// <summary>Evaluates this argument without discarding failure metadata, retaining legacy delegates.</summary>
	public async ValueTask<CallState> GetParsedResultAsync()
	{
		var result = ParsedResult is { } evaluate
			? await evaluate() ?? Empty
			: new CallState(await ParsedMessage());
		return HadErrors ? result with { HadErrors = true } : result;
	}
}
