using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Utilities;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.ParserInterfaces;

/// <summary>
/// Bitfield flags for the parser state, consolidating individual boolean properties into a single
/// compact field.  Adding a new flag here (instead of a new <see langword="bool"/> property) keeps
/// <see cref="ParserState"/> compact and avoids record-copy overhead for each new flag.
///
/// <para>
/// Mapping to PennMUSH <c>QUEUE_*</c> constants (<c>mque.queue_type</c>):
/// <list type="table">
///   <listheader><term>SharpMUSH flag</term><description>PennMUSH equivalent</description></listheader>
///   <item><term><see cref="DirectInput"/></term><description><c>QUEUE_NOLIST</c> (0x0200) — don't split on semicolons; don't evaluate the RHS of the <c>&amp;</c> command</description></item>
///   <item><term><see cref="Debug"/></term><description><c>QUEUE_DEBUG</c> (0x1000) — attribute carries the DEBUG flag; force debug output on</description></item>
///   <item><term><see cref="NoDebug"/></term><description><c>QUEUE_NODEBUG</c> (0x2000) — attribute carries the NO_DEBUG flag; suppress debug output</description></item>
/// </list>
/// Other notable PennMUSH flags not (yet) represented here:
/// <c>QUEUE_SOCKET</c> (0x0004, socket input) is covered by <see cref="ParserState.Handle"/> being non-null;
/// <c>QUEUE_BREAK</c> (0x0400) maps to <see cref="Execution.CommandListBreak"/>;
/// <c>QUEUE_INPLACE</c>, <c>QUEUE_PRESERVE_QREG</c>, etc. are not yet implemented.
/// </para>
/// </summary>
[Flags]
public enum ParserStateFlags
{
	/// <summary>No flags set.</summary>
	None = 0,

	/// <summary>
	/// Equivalent to PennMUSH's <c>QUEUE_NOLIST</c> flag (0x0200).
	/// Set when a command originates directly from a player's network connection (typed at the prompt).
	/// When set, commands like <c>&amp;</c> store their value argument as literal code without evaluation,
	/// and the command string is not split on semicolons.
	/// Cleared by <c>CommandListParse</c> / <c>CommandListParseVisitor</c> so that all queue and
	/// callback contexts (e.g., <c>@wait</c>, <c>@force</c>, triggered attributes) evaluate the RHS.
	/// </summary>
	DirectInput = 1 << 0,

	/// <summary>
	/// Equivalent to PennMUSH's <c>QUEUE_DEBUG</c> flag (0x1000).
	/// Set when the attribute being evaluated carries the DEBUG flag.
	/// Forces debug output on for this evaluation, regardless of the executor's DEBUG object flag.
	/// Takes lower precedence than <see cref="NoDebug"/>: if both are set, <c>NoDebug</c> wins.
	/// </summary>
	Debug = 1 << 1,

	/// <summary>
	/// Equivalent to PennMUSH's <c>QUEUE_NODEBUG</c> flag (0x2000).
	/// Set when the attribute being evaluated carries the NO_DEBUG flag.
	/// Suppresses debug output for this evaluation, regardless of the executor's DEBUG object flag.
	/// Takes precedence over <see cref="Debug"/>: if both are set, this wins.
	/// </summary>
	NoDebug = 1 << 2,

	/// <summary>
	/// Set during argument parsing when the command has <see cref="CommandBehavior.RSBrace"/>.
	/// Causes <c>VisitBracePattern</c> to preserve outer braces in the argument text instead of
	/// stripping them. This implements PennMUSH's <c>CS_BRACES</c> flag behavior, where brace
	/// stripping is deferred to the command handler (via <see cref="HelperFunctions.StripOuterBraces"/>)
	/// rather than happening during argument tokenization.
	/// </summary>
	PreserveBraces = 1 << 3,

	/// <summary>
	/// Equivalent to prefixing a command with <c>~</c>.
	/// When set, command argument parsers run in strict mode: ANTLR syntax errors are surfaced
	/// as <c>#-1 PARSER FAILURE</c> instead of using error-recovery (lenient) parsing.
	/// By default, command argument parsing is lenient. This flag overrides that default.
	/// </summary>
	StrictParse = 1 << 4,

	/// <summary>
	/// A command redispatched by <c>TEACH</c>: it keeps <see cref="DirectInput"/>'s <c>QUEUE_NOLIST</c>
	/// meaning, but not the socket's, so a <c>$</c>-command it reaches is queued rather than run in place.
	/// PennMUSH's <c>do_teach</c> queues the lesson without <c>QUEUE_SOCKET</c> (<c>src/speech.c:130-163</c>).
	/// </summary>
	QueueMatches = 1 << 5,
}

/// <summary>
/// What parsing mode the parser should consider.
/// </summary>
public enum ParseMode
{
	/// <summary>
	/// Normal parsing. Parse everything.
	/// </summary>
	Default,
	/// <summary>
	/// Do not parse argument-splits (commas). This is usually called by {}s. 
	/// </summary>
	NoParse,
	/// <summary>
	/// Do not evaluate any parameters.
	/// </summary>
	NoEval
}

/// <summary>
/// Sets execution values pertinent to the parser.
/// </summary>
/// <param name="CommandListBreak">Sets whether to stop executing the remainder of the CommandList. This is reset in the SharpMUSHParserVisitor</param>
public record Execution(bool CommandListBreak = false);

/// <summary>
/// Shared counter for function invocations. Mutable reference type to share across parser states.
/// </summary>
public class InvocationCounter
{
	/// <summary>
	/// Total number of function calls made during this evaluation.
	/// </summary>
	public int Count { get; private set; }

	/// <summary>
	/// Increment the counter and return the new value.
	/// </summary>
	public int Increment() => ++Count;

	/// <summary>
	/// Decrement the counter and return the new value.
	/// </summary>
	public int Decrement() => --Count;
}

/// <summary>
/// Shared flag for tracking when a limit (invocation, recursion, depth, call) has been exceeded.
/// Must be a reference type (class) to enable sharing the same flag across all immutable ParserState records.
/// </summary>
public class LimitExceededFlag
{
	/// <summary>
	/// Indicates whether a limit has been exceeded during this evaluation.
	/// </summary>
	public bool IsExceeded { get; set; }

	/// <summary>
	/// The MUSH error string of the limit that first tripped — invocation, recursion/call, or the
	/// output-size ceiling. Set once (first trip wins) alongside <see cref="IsExceeded"/> so that a
	/// limit hit deep in argument evaluation surfaces the error that actually occurred, rather than
	/// an outer frame reporting every limit as a generic invocation-limit error.
	/// </summary>
	public string? ErrorMessage { get; set; }
}

/// <summary>
/// HTTP response context for building HTTP responses
/// </summary>
public class HttpResponseContext
{
	/// <summary>
	/// HTTP status line (e.g., "200 OK", "404 Not Found")
	/// </summary>
	public string? StatusLine { get; set; }

	/// <summary>
	/// Content-Type header value
	/// </summary>
	public string? ContentType { get; set; }

	/// <summary>
	/// Additional HTTP headers
	/// </summary>
	public List<(string Name, string Value)> Headers { get; } = new();

	/// <summary>
	/// Response body content
	/// </summary>
	public StringBuilder Body { get; } = new();

	/// <summary>
	/// Whether captured output exceeded the shared UTF-16 output limit.
	/// </summary>
	public bool OutputLimitExceeded { get; internal set; }
}

public class IterationWrapper<T>
{
	/// <summary>
	/// The iteration value.
	/// </summary>
	public required T Value { get; set; }

	/// <summary>
	/// Iteration number.
	/// </summary>
	public required uint Iteration { get; set; }

	/// <summary>
	/// This is for the break() function iterator.
	/// </summary>
	public required bool Break { get; set; }

	/// <summary>
	/// NoBreak indicator is to ensure that a CommandListBreak does not also break the Iteration.
	/// </summary>
	public required bool NoBreak { get; set; }
}

/// <summary>
/// Lets one command list hand its <c>@break</c>/<c>@assert</c> up to the list that ran it.
///
/// <para><c>VisitCommandList</c> pops the break marker when its list stops, which is right at the
/// top of a queue entry and wrong for a list a command chose to run: <c>@include</c> inserts the
/// included actions into the CALLING list, so a guard inside it must stop the caller too, and
/// <c>/nobreak</c> exists to suppress that (<c>help @include</c>).</para>
///
/// <para>Reference type, so the flags are shared across the immutable ParserState records made
/// while the nested list runs. <see cref="PreserveNext"/> is a ONE-SHOT consumed by the next
/// command list to begin, so lists nested deeper keep containing their own breaks.</para>
/// </summary>
public class BreakPropagation
{
	/// <summary>Set by the caller before running a nested list; cleared by the first list that starts.</summary>
	public bool PreserveNext { get; set; }

	/// <summary>Set by that list if it stopped on a break, so the caller can re-raise it.</summary>
	public bool Broke { get; set; }
}

/// <summary>
/// The text behind <c>%c</c> and <c>%u</c>: PennMUSH's <c>pe_info-&gt;cmd_raw</c> and
/// <c>cmd_evaled</c>, one pair per queue entry.
///
/// <para><see cref="Raw"/> is the command that is running, as written. <see cref="Evaluated"/> is the
/// last command whose arguments were parsed, rebuilt from those arguments — so while a command's own
/// arguments evaluate it still holds the previous command's, and only hooks, the command body and
/// whatever runs after see the current one (command.c <c>command_parse</c>, game.c
/// <c>process_command</c>). Both are empty when an entry starts.</para>
///
/// <para>Reference type, shared the way PennMUSH shares a <c>pe_info</c>: every state copied within a
/// queue entry, and an in-place list it runs (<c>@include</c>, <c>@ifelse</c>; PE_INFO_SHARE), write the
/// same pair, so the last command of an included list is what the caller's next command reads. A queued
/// entry or a <c>$</c>-command body gets a new pair (PE_INFO_DEFAULT / PE_INFO_CLONE).</para>
///
/// <para>It also holds <see cref="Output"/>, <c>%></c>: the logical output of the last command run in
/// the entry. Unlike <c>%c</c>/<c>%u</c>, a queued entry starts with a copy of the value its submitter
/// had when it queued it, the way q-registers are copied.</para>
///
/// <para><see cref="Printed"/>, <c>%|</c>, is what the command before a <c>;|</c> printed, and is copied
/// to a queued entry the same way.</para>
/// </summary>
public sealed class CommandText(MString? output = null, MString? printed = null)
{
	private MString? _redispatchedRaw;
	private MString _evaluated = MarkupText.Empty;
	private Func<MString>? _rebuildEvaluated;

	/// <summary>The running command before evaluation: <c>%c</c>.</summary>
	public MString Raw { get; private set; } = MarkupText.Empty;

	/// <summary>The last parsed command after evaluation: <c>%u</c>.</summary>
	public MString Evaluated
	{
		get
		{
			if (_rebuildEvaluated is { } rebuild)
			{
				_evaluated = rebuild();
				_rebuildEvaluated = null;
			}

			return _evaluated;
		}
		set
		{
			_evaluated = value;
			_rebuildEvaluated = null;
		}
	}

	/// <summary>
	/// Sets <see cref="Evaluated"/> from text built on first read. Every command sets it and few read
	/// it, so the rebuild is only paid for by a <c>%u</c>. <paramref name="rebuild"/> must depend only on
	/// values already evaluated: it never evaluates anything itself.
	/// </summary>
	public void EvaluatedFrom(Func<MString> rebuild) => _rebuildEvaluated = rebuild;

	/// <summary>
	/// A command starts: <paramref name="raw"/> becomes <see cref="Raw"/>, unless the command it
	/// replaces asked to keep its own text with <see cref="KeepRawThroughRedispatch"/>.
	/// </summary>
	public void Begin(MString raw)
	{
		Raw = _redispatchedRaw ?? raw;
		_redispatchedRaw = null;
	}

	/// <summary>
	/// The next command to begin is a rewrite of this one — a speech token becoming <c>SAY</c> — and
	/// PennMUSH's <c>%c</c> is still the line as typed. One-shot; <see cref="EndRedispatch"/> discards it
	/// if the rewrite never began.
	/// </summary>
	public void KeepRawThroughRedispatch() => _redispatchedRaw = Raw;

	/// <inheritdoc cref="KeepRawThroughRedispatch"/>
	public void EndRedispatch() => _redispatchedRaw = null;

	/// <summary><c>%></c>: the logical output of the last command run in this entry.</summary>
	public MString Output { get; private set; } = output ?? MarkupText.Empty;

	/// <summary>
	/// Counts every <see cref="SetOutput"/> and <see cref="KeepOutput"/>, so a command can tell whether
	/// anything recorded an output while it ran — an in-place list it ran, or the command a modifier wraps.
	/// </summary>
	public long OutputVersion { get; private set; }

	/// <summary>A command finished with <paramref name="value"/> as its output.</summary>
	public void SetOutput(MString value)
	{
		Output = value;
		OutputVersion++;
	}

	/// <summary>A command finished and leaves <see cref="Output"/> as it was.</summary>
	public void KeepOutput() => OutputVersion++;

	/// <summary>
	/// <c>%|</c>: what the command piped into this one (<c>look ;| say %|</c>) printed for its executor.
	/// Set only while the command after a <c>;|</c> runs; every other command reads what its list had
	/// before, which is empty unless the entry was queued with a copy.
	/// </summary>
	public MString Printed { get; set; } = printed ?? MarkupText.Empty;
}

/// <summary>
/// What every frame of one evaluation shares: the register stacks, the invocation counters and limits,
/// the connection, <c>%c</c>/<c>%u</c>/<c>%></c>/<c>%|</c> and the output limit. A function frame forked
/// with <see cref="ParserState.ForFunction"/> points at its caller's, so a call copies none of it.
/// </summary>
/// <remarks>
/// Immutable like <see cref="ParserState"/>: a frame that replaces one of these (<c>with { Registers = … }</c>)
/// gets a copy of its own, which the frames forked from it then share. The values themselves are the same
/// shared, mutable objects they always were.
/// </remarks>
public sealed record EvaluationContext
{
	/// <summary>The current standard registers (%q-registers), top frame first.</summary>
	public required ConcurrentStack<Dictionary<string, MString>> Registers { get; init; }

	/// <summary>The current iteration registers: %i0, ##, #@, etc.</summary>
	public required ConcurrentStack<IterationWrapper<MString>> IterationRegisters { get; init; }

	/// <summary>
	/// The regexp capture frames that <c>$0</c>-<c>$9</c> and <c>$&lt;name&gt;</c> read, innermost on top
	/// (see <see cref="RegexpCaptureFrame"/>). Not <c>%$0</c>, which is the switch text in <see cref="SwitchStack"/>.
	/// </summary>
	public required ConcurrentStack<Dictionary<string, MString>> RegexRegisters { get; init; }

	/// <summary>The switch context stack for stext() and slev(): the string matched in each nested switch.</summary>
	public required ConcurrentStack<MString> SwitchStack { get; init; }

	/// <summary>Execution control for the running command list (<see cref="Execution.CommandListBreak"/>).</summary>
	public required ConcurrentStack<Execution> ExecutionStack { get; init; }

	/// <summary>The environment registers.</summary>
	public required Dictionary<string, CallState> EnvironmentRegisters { get; init; }

	/// <summary>The telnet handle running the command.</summary>
	public long? Handle { get; init; }

	/// <summary>HTTP response context for building HTTP responses.</summary>
	public HttpResponseContext? HttpResponse { get; init; }

	/// <summary>Overall function call nesting depth.</summary>
	public InvocationCounter? CallDepth { get; init; }

	/// <summary>Per-function recursion depths.</summary>
	public Dictionary<string, int>? FunctionRecursionDepths { get; init; }

	/// <summary>Total function invocations.</summary>
	public InvocationCounter? TotalInvocations { get; init; }

	/// <summary>Set once a limit has been exceeded.</summary>
	public LimitExceededFlag? LimitExceeded { get; init; }

	/// <inheritdoc cref="ParserState.MoveDepth"/>
	public InvocationCounter? MoveDepth { get; init; }

	/// <inheritdoc cref="ParserState.CommandText"/>
	public CommandText? CommandText { get; init; }

	/// <inheritdoc cref="ParserState.QueuedOutput"/>
	public MString? QueuedOutput { get; init; }

	/// <inheritdoc cref="ParserState.QueuedPrinted"/>
	public MString? QueuedPrinted { get; init; }

	/// <inheritdoc cref="ParserState.OutputLimit"/>
	public int OutputLimit { get; init; } = OutputCeiling.CurrentLimit;
}

/// <summary>
/// One frame of an evaluation: who is running what, with which arguments, over the
/// <see cref="EvaluationContext"/> the whole evaluation shares.
/// </summary>
/// <remarks>
/// The shared values read through to <see cref="Context"/> under their old names, and setting one in a
/// <c>with</c> gives this frame a context of its own. A frame is made only by the factories below
/// (<see cref="RootFor"/>, <see cref="ForTypedLine"/>, <see cref="ForTrackedEvaluation"/>,
/// <see cref="ForFunction"/>, <see cref="SnapshotForQueuedAction"/>) plus <c>with</c> for any extras.
/// </remarks>
public partial record ParserState
{
	private ParserState() { }

	/// <summary>What this frame shares with the rest of its evaluation.</summary>
	public required EvaluationContext Context { get; init; }

	/// <inheritdoc cref="EvaluationContext.Registers"/>
	public ConcurrentStack<Dictionary<string, MString>> Registers
	{
		get => Context.Registers;
		init => Context = Context with { Registers = value };
	}

	/// <inheritdoc cref="EvaluationContext.IterationRegisters"/>
	public ConcurrentStack<IterationWrapper<MString>> IterationRegisters
	{
		get => Context.IterationRegisters;
		init => Context = Context with { IterationRegisters = value };
	}

	/// <inheritdoc cref="EvaluationContext.RegexRegisters"/>
	public ConcurrentStack<Dictionary<string, MString>> RegexRegisters
	{
		get => Context.RegexRegisters;
		init => Context = Context with { RegexRegisters = value };
	}

	/// <inheritdoc cref="EvaluationContext.SwitchStack"/>
	public ConcurrentStack<MString> SwitchStack
	{
		get => Context.SwitchStack;
		init => Context = Context with { SwitchStack = value };
	}

	/// <inheritdoc cref="EvaluationContext.ExecutionStack"/>
	public ConcurrentStack<Execution> ExecutionStack
	{
		get => Context.ExecutionStack;
		init => Context = Context with { ExecutionStack = value };
	}

	/// <inheritdoc cref="EvaluationContext.EnvironmentRegisters"/>
	public Dictionary<string, CallState> EnvironmentRegisters
	{
		get => Context.EnvironmentRegisters;
		init => Context = Context with { EnvironmentRegisters = value };
	}

	/// <inheritdoc cref="EvaluationContext.Handle"/>
	public long? Handle
	{
		get => Context.Handle;
		init => Context = Context with { Handle = value };
	}

	/// <inheritdoc cref="EvaluationContext.HttpResponse"/>
	public HttpResponseContext? HttpResponse
	{
		get => Context.HttpResponse;
		init => Context = Context with { HttpResponse = value };
	}

	/// <inheritdoc cref="EvaluationContext.CallDepth"/>
	public InvocationCounter? CallDepth
	{
		get => Context.CallDepth;
		init => Context = Context with { CallDepth = value };
	}

	/// <inheritdoc cref="EvaluationContext.FunctionRecursionDepths"/>
	public Dictionary<string, int>? FunctionRecursionDepths
	{
		get => Context.FunctionRecursionDepths;
		init => Context = Context with { FunctionRecursionDepths = value };
	}

	/// <inheritdoc cref="EvaluationContext.TotalInvocations"/>
	public InvocationCounter? TotalInvocations
	{
		get => Context.TotalInvocations;
		init => Context = Context with { TotalInvocations = value };
	}

	/// <inheritdoc cref="EvaluationContext.LimitExceeded"/>
	public LimitExceededFlag? LimitExceeded
	{
		get => Context.LimitExceeded;
		init => Context = Context with { LimitExceeded = value };
	}

	/// <summary>
	/// Shared counter bounding recursive movement — <c>enter_room</c> reached through
	/// <c>safe_tel</c>'s HOME case or through a container's drop-to. PennMUSH caps the equivalent at
	/// 15 (<c>src/move.c:232</c>) with a process-global counter, which is only safe under its
	/// single-threaded queue; here it is per evaluation, like <see cref="CallDepth"/>.
	/// </summary>
	public InvocationCounter? MoveDepth
	{
		get => Context.MoveDepth;
		init => Context = Context with { MoveDepth = value };
	}

	/// <summary>
	/// <c>%c</c> and <c>%u</c> for the queue entry this state belongs to; null outside a command.
	/// Shared by reference like <see cref="CommandHistory"/>.
	/// </summary>
	public CommandText? CommandText
	{
		get => Context.CommandText;
		init => Context = Context with { CommandText = value };
	}

	/// <summary>
	/// <c>%></c> as the list that queued this state had it when it did. A queued entry has no
	/// <see cref="CommandText"/> of its own until its list starts, and starts it with this output.
	/// </summary>
	public MString? QueuedOutput
	{
		get => Context.QueuedOutput;
		init => Context = Context with { QueuedOutput = value };
	}

	/// <summary>
	/// <c>%|</c> as the list that queued this state had it when it did, started the same way as
	/// <see cref="QueuedOutput"/>.
	/// </summary>
	public MString? QueuedPrinted
	{
		get => Context.QueuedPrinted;
		init => Context = Context with { QueuedPrinted = value };
	}

	/// <summary>
	/// Most UTF-16 code units one function may produce in this evaluation. Lowered for a guest's
	/// input, and carried by every copy of the state, including the snapshots of queued actions.
	/// A new state starts from the <see cref="OutputCeiling"/> of the evaluation that creates it.
	/// </summary>
	public int OutputLimit
	{
		get => Context.OutputLimit;
		init => Context = Context with { OutputLimit = value };
	}

	/// <summary>The attribute being evaluated.</summary>
	public DBAttribute? CurrentEvaluation { get; init; }

	/// <summary>The function depth.</summary>
	public int? ParserFunctionDepth { get; init; }

	/// <summary>Function name being evaluated.</summary>
	public string? Function { get; init; }

	/// <summary>Name of the command being evaluated; its text is <see cref="CommandText"/>.</summary>
	public string? Command { get; init; }

	/// <summary>Runs the command being evaluated again (<c>@retry</c>).</summary>
	public Func<IMUSHCodeParser, ValueTask<Option<CallState>>> CommandInvoker { get; init; } = NoCommand;

	private static readonly Func<IMUSHCodeParser, ValueTask<Option<CallState>>> NoCommand
		= _ => ValueTask.FromResult(new Option<CallState>(new None()));

	/// <summary>Switches for the command being evaluated.</summary>
	public IEnumerable<string> Switches { get; init; } = [];

	/// <summary>The arguments to the command or function: %0-%9 by number, and named arguments.</summary>
	public required Dictionary<string, CallState> Arguments { get; init; }

	/// <summary>The executor of a command is the object actually carrying out the command or running the code: %!</summary>
	public DBRef? Executor { get; init; }

	/// <summary>The enactor is the object which causes something to happen: %# or %:</summary>
	public DBRef? Enactor { get; init; }

	/// <summary>The caller is the object which causes an attribute to be evaluated (for instance, by using ufun() or a similar function): %@</summary>
	public DBRef? Caller { get; init; }

	/// <summary>Parse mode, in case we need to NoParse.</summary>
	public ParseMode ParseMode { get; init; } = ParseMode.Default;

	/// <summary>
	/// Shared mutable stack tracking command invocations (invoker + args) for @retry support. Null outside
	/// CommandListParse context.
	/// </summary>
	public ConcurrentStack<(Func<IMUSHCodeParser, ValueTask<Option<CallState>>> Invoker, Dictionary<string, CallState> Args)>? CommandHistory { get; init; }

	/// <summary>
	/// Bitfield of <see cref="ParserStateFlags"/> values controlling parser behavior.
	/// Use <see cref="ParserStateFlags.DirectInput"/> (≙ <c>QUEUE_NOLIST</c>),
	/// <see cref="ParserStateFlags.Debug"/> (≙ <c>QUEUE_DEBUG</c>), and
	/// <see cref="ParserStateFlags.NoDebug"/> (≙ <c>QUEUE_NODEBUG</c>).
	/// </summary>
	public ParserStateFlags Flags { get; init; }

	/// <summary>
	/// Saves the caller's numbered arguments (%0-%9) from the enclosing scope before a command
	/// overwrites Arguments with its own parsed args. Used by @wait/@force to preserve pattern-match
	/// variables in queued callbacks. Equivalent to PennMUSH's wenv (wild environment).
	/// </summary>
	public Dictionary<string, CallState>? CallerArguments { get; init; }

	/// <summary>
	/// Shared, one-shot channel letting a nested command list hand its break to the list that ran it.
	/// Null everywhere except around a run that wants it (<c>@include</c>).
	/// </summary>
	public BreakPropagation? BreakPropagation { get; init; }

	/// <summary>The connection session the evaluation runs for, when there is one.</summary>
	public string? ConnectionSessionId { get; init; }

	/// <summary>Synchronous command-modifier nesting, bounded by MaxDepth and reset for independent queued actions.</summary>
	public uint CommandModifierDepth { get; init; }

	/// <summary>
	/// How many in-place action lists — <c>@include</c>, <c>@trigger</c>, <c>@switch/inplace</c>,
	/// <c>@dolist/inplace</c>, <c>@break</c>'s action and the like — enclose this one inside its queue
	/// entry. A list that starts a queue entry of its own starts again from zero.
	/// </summary>
	public int InplaceDepth { get; init; }

	/// <summary>
	/// The deepest an in-place action list may nest. PennMUSH's <c>do_entry</c> runs a nested in-place
	/// entry only while <c>include_recurses &lt; 50</c> (<c>src/cque.c:1182</c>), and drops one deeper
	/// without a word; the list that queued it carries on. It is not configurable there either.
	/// </summary>
	public const int MaxInplaceDepth = 50;

	/// <summary>Shared execution lifetime, retained when a nested parser copies this state.</summary>
	public ExecutionBudget? ExecutionBudget { get; init; }

	/// <summary>Restricted evaluation policy retained by nested parser state copies.</summary>
	public EvaluationRestrictions? Restrictions { get; init; }

	/// <summary>
	/// The line a player typed, as plain text, carried by the states that run it; null for a state that
	/// did not start from one. Text drawn from it is never kept by the parse cache, since a typed line can
	/// carry a password.
	/// </summary>
	public string? TypedLine { get; init; }

	/// <summary><c>%></c>: the logical output of the last command run in this queue entry.</summary>
	public MString PipedOutput => CommandText?.Output ?? QueuedOutput ?? MarkupText.Empty;

	/// <summary><c>%|</c>: what the command piped into the running one printed.</summary>
	public MString PrintedOutput => CommandText?.Printed ?? QueuedPrinted ?? MarkupText.Empty;

	/// <summary>A <see cref="CommandText"/> for a list this state starts, carrying its queued <c>%></c> and <c>%|</c>.</summary>
	public CommandText NewCommandText() => new(QueuedOutput, QueuedPrinted);

	/// <summary>This state, about to be queued: it keeps the values <c>%></c> and <c>%|</c> have now.</summary>
	public ParserState WithQueuedOutput() => this with
	{
		Context = Context with { QueuedOutput = PipedOutput, QueuedPrinted = PrintedOutput }
	};

	/// <summary>
	/// Captures the register environment for an independent queued action. Like PE_INFO_CLONE,
	/// only the active q-register frame is inherited; iteration, regex and switch contexts retain
	/// their nesting order. Mutable frames belong to the new action, not the submitting list.
	/// Execution control is fresh, and the scheduler supplies the action's budget when it runs. So is
	/// <see cref="CommandText"/>: PE_INFO_CLONE copies no <c>%c</c>/<c>%u</c>.
	/// </summary>
	public ParserState SnapshotForQueuedAction() => this with
	{
		Context = Context with
		{
			Registers = new([Registers.TryPeek(out var registers)
				? new Dictionary<string, MString>(registers, registers.Comparer)
				: []]),
			// ConcurrentStack enumerates top-first, but its constructor pushes each item in order.
			IterationRegisters = new(IterationRegisters.Reverse().Select(frame => new IterationWrapper<MString>
			{
				Value = frame.Value,
				Iteration = frame.Iteration,
				Break = frame.Break,
				NoBreak = frame.NoBreak
			})),
			RegexRegisters = new(RegexRegisters.Reverse().Select(frame => frame is RegexpCaptureFrame owned
				? owned.Clone()
				: new Dictionary<string, MString>(frame, frame.Comparer))),
			SwitchStack = new(SwitchStack.Reverse()),
			EnvironmentRegisters = new(EnvironmentRegisters, EnvironmentRegisters.Comparer),
			ExecutionStack = [],
			CallDepth = new(),
			FunctionRecursionDepths = new(StringComparer.OrdinalIgnoreCase),
			TotalInvocations = new(),
			LimitExceeded = new(),
			MoveDepth = new(),
			CommandText = null,
			QueuedOutput = PipedOutput,
			QueuedPrinted = PrintedOutput
		},
		CommandHistory = null,
		BreakPropagation = null,
		CommandModifierDepth = 0,
		InplaceDepth = 0,
		ExecutionBudget = null
	};

	private AnyOptionalSharpObject? _executorObject;
	private AnyOptionalSharpObject? _enactorObject;
	private AnyOptionalSharpObject? _callerObject;

	/// <summary>
	/// Validates that a cached object is the one the expected DBRef names (a bare dbref names it by number) and clears it if not.
	/// This handles the case where ParserState is copied with a new DBRef but the cached object is stale.
	/// </summary>
	private static void ValidateAndClearCacheIfNeeded(
		ref AnyOptionalSharpObject? cachedObject,
		DBRef? expectedDBRef)
	{
		if (cachedObject is AnySharpObject cached && (expectedDBRef is not { } expected || !cached.Object().DBRef.Matches(expected)))
		{
			cachedObject = null;
		}
	}

	public static ParserState Empty => new()
	{
		Context = new EvaluationContext
		{
			Registers = [],
			IterationRegisters = [],
			RegexRegisters = [],
			SwitchStack = [],
			ExecutionStack = [],
			EnvironmentRegisters = [],
			CallDepth = new InvocationCounter(),
			FunctionRecursionDepths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
			TotalInvocations = new InvocationCounter(),
			LimitExceeded = new LimitExceededFlag(),
			MoveDepth = new InvocationCounter()
		},
		Arguments = new Dictionary<string, CallState>()
	};

	// Construction. Every state is made one of three ways, and these are the only places that build one
	// (ParserStateConstructionTests holds every other file to them):
	//  - fresh: RootFor, ForTypedLine and ForTrackedEvaluation start an evaluation with a new context:
	//    new registers and new invocation counters (PennMUSH's PE_INFO_DEFAULT);
	//  - forked: ForFunction opens a function frame over the caller's context;
	//  - cloned: SnapshotForQueuedAction copies what a queued action keeps and owns (PE_INFO_CLONE).
	// A state copied with `with` shares its context and every mutable value it does not replace, so new
	// work that starts an independent evaluation picks one of these rather than copying the caller.

	/// <summary>
	/// A fresh root state for code that runs as <paramref name="actor"/> with no ambient parser:
	/// the boot @STARTUP pass, package lifecycle attributes, HTTP handler dispatch, attribute lock
	/// evaluation, and the portal's typed object API. The actor is executor, enactor and caller at once, because there
	/// is no outer frame for those to differ in.
	/// </summary>
	/// <remarks>
	/// Unlike <see cref="Empty"/>, the register stack is seeded with one frame — code invoked from
	/// a root state must be able to <c>setq()</c> without pushing a frame of its own. Callers
	/// needing extras (seeded q-registers, %0-%9, an HTTP response context) apply them with a
	/// <c>with</c> expression rather than growing this signature.
	/// </remarks>
	public static ParserState RootFor(DBRef actor)
		=> Fresh(actor, actor, actor, handle: null, ParserStateFlags.None, session: null,
			budget: null, restrictions: null, commandText: null, OutputCeiling.CurrentLimit);

	/// <summary>
	/// The fresh state a line typed by <paramref name="player"/> starts from: the player is executor,
	/// enactor and caller, the line is <see cref="ParserStateFlags.DirectInput"/>, and it begins its
	/// own <c>%c</c>/<c>%u</c>. <paramref name="handle"/> is the connection it was typed at, if any, and
	/// <paramref name="line"/> is the line, kept as <see cref="TypedLine"/>.
	/// </summary>
	public static ParserState ForTypedLine(DBRef? player, long? handle, string? session, int outputLimit, string line)
		=> Fresh(player, player, player, handle, ParserStateFlags.DirectInput, session,
			budget: null, restrictions: null, new CommandText(), outputLimit, line);

	/// <summary>
	/// A fresh state carrying invocation and recursion tracking for an evaluation entered without it.
	/// It keeps <paramref name="caller"/>'s actors, execution budget, restrictions and <c>%c</c>/<c>%u</c>,
	/// and nothing else: registers and counters start empty. With no caller it has no actors.
	/// </summary>
	public static ParserState ForTrackedEvaluation(ParserState? caller)
		=> Fresh(caller?.Executor, caller?.Enactor, caller?.Caller, handle: null, ParserStateFlags.None,
			session: null, caller?.ExecutionBudget, caller?.Restrictions, caller?.CommandText,
			OutputCeiling.CurrentLimit);

	private static ParserState Fresh(DBRef? executor, DBRef? enactor, DBRef? caller, long? handle,
		ParserStateFlags flags, string? session, ExecutionBudget? budget, EvaluationRestrictions? restrictions,
		CommandText? commandText, int outputLimit, string? typedLine = null) => new()
		{
			Context = new EvaluationContext
			{
				Registers = new([[]]),
				IterationRegisters = [],
				RegexRegisters = [],
				SwitchStack = [],
				ExecutionStack = [],
				EnvironmentRegisters = new Dictionary<string, CallState>(),
				Handle = handle,
				CallDepth = new InvocationCounter(),
				FunctionRecursionDepths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
				TotalInvocations = new InvocationCounter(),
				LimitExceeded = new LimitExceededFlag(),
				MoveDepth = new InvocationCounter(),
				CommandText = commandText,
				OutputLimit = outputLimit
			},
			ParserFunctionDepth = 0,
			Arguments = new Dictionary<string, CallState>(),
			Executor = executor,
			Enactor = enactor,
			Caller = caller,
			Flags = flags,
			ConnectionSessionId = session,
			ExecutionBudget = budget,
			Restrictions = restrictions,
			TypedLine = typedLine
		};

	/// <summary>
	/// The frame a built-in or <c>@function</c> call runs in, forked from this state. It runs over this
	/// state's <see cref="Context"/> — registers, iteration, regexp and switch contexts, environment and
	/// invocation counters — since a function runs inside its caller's evaluation, and binds
	/// <paramref name="arguments"/> as <c>%0</c>-<c>%9</c>. It starts with no command, switches,
	/// command history, caller arguments or break propagation, and leaves the execution budget and
	/// restrictions to the ambient scopes already entered for the evaluation. It keeps the typed line
	/// and connection session it was called from.
	/// </summary>
	/// <param name="function">The function's name as called.</param>
	/// <param name="arguments">The call's arguments, keyed <c>"0"</c>, <c>"1"</c>, ….</param>
	public ParserState ForFunction(string function, Dictionary<string, CallState> arguments) => new()
	{
		Context = Context,
		CurrentEvaluation = CurrentEvaluation,
		ParserFunctionDepth = ParserFunctionDepth + 1,
		Function = function,
		Arguments = arguments,
		Executor = Executor,
		Enactor = Enactor,
		Caller = Caller,
		ParseMode = ParseMode,
		Flags = Flags,
		// Still the same typed line, so its text stays out of the parse cache when the function parses
		// part of it, and the same connection's session for @input.
		TypedLine = TypedLine,
		ConnectionSessionId = ConnectionSessionId,
		// Same actors, so the same objects: a function body that asks for its executor reuses the
		// caller's rather than fetching it again.
		_executorObject = _executorObject,
		_enactorObject = _enactorObject,
		_callerObject = _callerObject
	};

	/// <summary>
	/// The executor of a command is the object actually carrying out the command or running the code: %!
	/// </summary>
	/// <param name="mediator">Mediator to get the object node with.</param>
	/// <returns>A ValueTask containing either a SharpObject, or None.</returns>
	public async ValueTask<AnyOptionalSharpObject> ExecutorObject(IMediator mediator)
	{
		ValidateAndClearCacheIfNeeded(ref _executorObject, Executor);
		return _executorObject ??= Executor is null ? new None() : await mediator.Send(new GetObjectNodeQuery(Executor.Value), ExecutionBudget.CurrentToken);
	}

	/// <summary>
	/// The enactor is the object which causes something to happen: %# or %:
	/// </summary>
	/// <param name="mediator">Mediator to get the object node with.</param>
	/// <returns>A ValueTask containing either a SharpObject, or None.</returns>
	public async ValueTask<AnyOptionalSharpObject> EnactorObject(IMediator mediator)
	{
		ValidateAndClearCacheIfNeeded(ref _enactorObject, Enactor);
		return _enactorObject ??= Enactor is null ? new None() : await mediator.Send(new GetObjectNodeQuery(Enactor.Value), ExecutionBudget.CurrentToken);
	}

	/// <summary>
	/// The caller is the object which causes an attribute to be evaluated (for instance, by using ufun() or a similar function): %@
	/// </summary>
	/// <param name="mediator">Mediator to get the object node with.</param>
	/// <returns>A ValueTask containing either a SharpObject, or None.</returns>
	public async ValueTask<AnyOptionalSharpObject> CallerObject(IMediator mediator)
	{
		ValidateAndClearCacheIfNeeded(ref _callerObject, Caller);
		return _callerObject ??= Caller is null ? new None() : await mediator.Send(new GetObjectNodeQuery(Caller.Value), ExecutionBudget.CurrentToken);
	}

	/// <summary>
	/// The executor of a command is the object actually carrying out the command or running the code: %!
	/// </summary>
	/// <param name="mediator">Mediator to get the object node with.</param>
	/// <returns>A ValueTask containing either a SharpObject, or it will throw.</returns>
	public async ValueTask<AnySharpObject> KnownExecutorObject(IMediator mediator)
		=> await ExecutorObject(mediator) is AnySharpObject executor
			? executor
			: throw new InvalidOperationException("The executor does not exist.");

	/// <summary>
	/// The enactor is the object which causes something to happen: %# or %:
	/// </summary>
	/// <param name="mediator">Mediator to get the object node with.</param>
	/// <returns>A ValueTask containing either a SharpObject, or it will throw.</returns>
	public async ValueTask<AnySharpObject> KnownEnactorObject(IMediator mediator)
		=> await EnactorObject(mediator) is AnySharpObject enactor
			? enactor
			: throw new InvalidOperationException("The enactor does not exist.");

	/// <summary>
	/// The caller is the object which causes an attribute to be evaluated (for instance, by using ufun() or a similar function): %@
	/// </summary>
	/// <param name="mediator">Mediator to get the object node with.</param>
	/// <returns>A ValueTask containing either a SharpObject, or it will throw.</returns>
	public async ValueTask<AnySharpObject> KnownCallerObject(IMediator mediator)
		=> await CallerObject(mediator) is AnySharpObject caller
			? caller
			: throw new InvalidOperationException("The caller does not exist.");

	/// <summary>
	/// Just the numbered arguments, %0-%9 etc., in numerical order. This excludes named arguments.
	/// </summary>
	public OrderedArguments ArgumentsOrdered
	{
		get
		{
			// Cache the result. Invalidate if Arguments reference changed (e.g., after `with` expression).
			if (_argumentsOrdered is not null && ReferenceEquals(_argumentsOrderedSource, Arguments))
				return _argumentsOrdered;

			_argumentsOrderedSource = Arguments;
			_argumentsOrdered = OrderedArguments.From(Arguments);
			return _argumentsOrdered;
		}
	}

	private OrderedArguments? _argumentsOrdered;
	private Dictionary<string, CallState>? _argumentsOrderedSource;

	private static readonly string[] ArgumentKeys = [.. Enumerable.Range(0, 64).Select(position => position.ToString())];

	/// <summary>
	/// The key <c>%<paramref name="position"/></c> is bound under in <see cref="Arguments"/>; the first 64
	/// are shared strings, so binding a call's arguments allocates no key.
	/// </summary>
	public static string ArgumentKey(int position)
		=> (uint)position < (uint)ArgumentKeys.Length ? ArgumentKeys[position] : position.ToString();

	/// <summary>
	/// Add a register value to the Register stack.
	/// </summary>
	/// <param name="register">Register string.</param>
	/// <param name="value">Value.</param>
	/// <returns>Success if it was a valid register.</returns>
	public bool AddRegister(string register, MString value)
	{
		// Validate register pattern: alphanumeric characters, underscores, hyphens, and dots.
		// Register names should be uppercase and match pattern: [A-Z0-9_.-]+
		// Dots are required for the HTTP handler's %q<hdr.name> header registers (see help sharphttp).
		if (string.IsNullOrEmpty(register) || !RegisterNameRegex().IsMatch(register))
		{
			return false;
		}

		var canPeek = Registers.TryPeek(out var top);
		if (!canPeek)
		{
			return false;
		}

		if (!top!.TryAdd(register, value))
		{
			top[register] = value;
		}

		return true;
	}

	[GeneratedRegex(@"^[A-Z0-9_.\-]+$")]
	private static partial Regex RegisterNameRegex();

	/// <summary>
	/// The regexp capture frames this evaluation can see, innermost first. A frame opened while another
	/// attribute was being evaluated is out of sight: PennMUSH marks a called attribute
	/// <c>PE_REGS_NEWATTR</c>, and <c>pi_regs_get_rx</c> (<c>src/parse.c</c>) stops walking there.
	/// </summary>
	private IEnumerable<Dictionary<string, MString>> VisibleRegexpFrames
		=> RegexRegisters.TakeWhile(frame => frame is not RegexpCaptureFrame owned
			|| ReferenceEquals(owned.Evaluation, CurrentEvaluation));

	/// <summary>
	/// Whether <c>$&lt;digit&gt;</c> and <c>$&lt;name&gt;</c> are substitutions here rather than text:
	/// a regexp context is visible, even one holding no captures (PennMUSH's
	/// <c>PE_HAS_REGTYPE(pe_info, PE_REGS_REGEXP)</c>; <c>fun_reswitch</c> and <c>fun_switch</c> localize the frame
	/// before matching, so a default branch sees it empty and <c>$0</c> there is empty, not literal).
	/// </summary>
	public bool HasRegexpContext => VisibleRegexpFrames.Any();

	/// <summary>
	/// The innermost visible regexp context, which is the only one <c>PE_Get_re</c> reads; null when
	/// there is none. What <c>$n</c>, <c>r(&lt;n&gt;, regexp)</c> and <c>registers(, regexp)</c> see.
	/// </summary>
	public IReadOnlyDictionary<string, MString>? RegexpCaptures => VisibleRegexpFrames.FirstOrDefault();

	/// <summary>
	/// The capture <paramref name="name"/> — a group number or a group name, in any case — from the
	/// innermost visible regexp context only, as <c>PE_Get_re</c> reads it. Empty when that context
	/// has no such capture, even if an outer one does.
	/// </summary>
	public MString RegexpCapture(string name)
		=> RegexpCaptures is { } frame && frame.TryGetValue(name, out var value)
			? value
			: MarkupText.Empty;
}

/// <summary>
/// One regexp capture context: PennMUSH's <c>PE_REGS_REGEXP</c> frame, which <c>reswitch()</c> opens
/// around its bodies and <c>$&lt;digit&gt;</c> / <c>$&lt;name&gt;</c> read while they are evaluated.
/// Keys are case-insensitive, as <c>pe_regs_get</c> upper-cases them.
/// </summary>
/// <param name="evaluation">
/// The attribute being evaluated when the frame was opened, compared by reference: every attribute
/// evaluation creates its own <see cref="DBAttribute"/>, so a <c>u()</c> of the same attribute is still
/// a different one.
/// </param>
public sealed class RegexpCaptureFrame(DBAttribute? evaluation)
	: Dictionary<string, MString>(StringComparer.OrdinalIgnoreCase)
{
	public DBAttribute? Evaluation { get; } = evaluation;

	/// <summary>
	/// Replaces the frame's captures with <paramref name="match"/>'s, as <c>pe_regs_set_rx_context</c>
	/// does: every numbered group, numbered as PCRE numbers them, and each named group that took part.
	/// Values are slices of <paramref name="subject"/>, so they keep its markup.
	/// </summary>
	public void Fill(Regex regex, Match match, MString subject)
	{
		Clear();
		foreach (var (number, dotNetNumber) in SoftcodeRegex.PcreGroupNumbers(regex).Index())
		{
			var group = match.Groups[dotNetNumber];
			this[number.ToString()] = group.Success ? subject.Substring(group.Index, group.Length) : MarkupText.Empty;
		}

		foreach (var name in regex.GetGroupNames().Where(name => !int.TryParse(name, out _)))
		{
			var group = match.Groups[name];
			if (group.Success)
				this[name] = subject.Substring(group.Index, group.Length);
		}
	}

	/// <summary>
	/// Replaces the frame's captures with a wildcard match's: one per <c>*</c> or <c>?</c>, numbered
	/// from 0, as <c>local_wild_match</c> stores them (<c>src/wild.c</c>).
	/// </summary>
	public void FillWildcard(Match match, MString subject)
	{
		Clear();
		for (var number = 1; number < match.Groups.Count; number++)
		{
			var group = match.Groups[number];
			this[(number - 1).ToString()] = group.Success ? subject.Substring(group.Index, group.Length) : MarkupText.Empty;
		}
	}

	public RegexpCaptureFrame Clone()
	{
		var copy = new RegexpCaptureFrame(Evaluation);
		foreach (var (name, value) in this)
			copy[name] = value;
		return copy;
	}
}
