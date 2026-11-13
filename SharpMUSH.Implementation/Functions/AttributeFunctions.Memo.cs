using System.Collections.Concurrent;
using System.Text;
using SharpMUSH.Implementation.Parsing;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Softcode;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// <c>memo()</c>: <c>u()</c> whose answer is kept and given again for the same call while nothing it read has
/// changed. The code may not have side effects.
/// </summary>
/// <remarks>
/// A call is the same when it runs the same code on the same object for the same executor and enactor with the same
/// arguments, markup included. The code runs with side-effect functions refused (<see cref="SideEffectFreeEvaluation"/>)
/// and with q-registers of its own, empty at the start and dropped at the end, so a kept answer means what a fresh
/// one would. It is kept until an attribute it read (<see cref="AttributeReadLog"/>) no longer reads the same,
/// <c>memo_window</c> passes, or <c>memo_max_bytes</c> pushes it out.
/// </remarks>
public partial class Functions
{
	/// <summary>What memo() answers when its code called a side-effect function.</summary>
	public const string MemoRefusesSideEffects = "#-1 MEMO REFUSES SIDE EFFECTS";

	/// <summary>What memo() answers when its code called a function whose answer varies between calls.</summary>
	public const string MemoRefusesNonDeterministic = "#-1 MEMO REFUSES NONDETERMINISTIC FUNCTIONS";

	/// <summary>
	/// Functions with a side effect that HasSideFX does not mark, because function_side_effects must not turn them
	/// off; they refuse inside a side-effect-free evaluation themselves.
	/// </summary>
	private static readonly HashSet<string> UnflaggedSideEffects = new(StringComparer.OrdinalIgnoreCase) { "stopevent" };

	/// <summary>The longest answer memo() keeps, as the longest attribute value.</summary>
	private const int MemoMaxAnswerLength = 65536;

	[SharpFunction(Name = "memo", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular,
		ParameterNames = ["object/attribute", "arguments..."])]
	public async ValueTask<CallState> Memo(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arguments = parser.CurrentState.ArgumentsOrdered.Skip(1)
			.Select((value, i) => new KeyValuePair<string, CallState>(i.ToString(), value.Value))
			.ToDictionary();

		return await MemoFunctionAsync(parser, executor, parser.CurrentState.ArgumentList[0].Message) switch
		{
			AttributeFunction function => await MemoizedAsync(parser, executor, function, arguments),
			// u() answers nothing for an attribute that is not there.
			CallState { Message: var message } when message.ToPlainText() == ErrorMessages.Returns.NoSuchAttribute => CallState.Empty,
			CallState answer => answer
		};
	}

	/// <summary>The code memo() runs: a <c>#lambda</c>, or an attribute found and checked as u() finds it.</summary>
	private async ValueTask<AttributeFunctionFetch> MemoFunctionAsync(IMUSHCodeParser parser, AnySharpObject executor,
		MString specifier)
		=> AttributeFunction.OfLambda(executor, specifier, parser.CurrentState.CurrentEvaluation) switch
		{
			AttributeFunction lambda => lambda,
			NotFound => await AttributeService.FetchAttributeFunctionAsync(parser, executor, specifier.ToPlainText())
		};

	private async ValueTask<CallState> MemoizedAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AttributeFunction function, Dictionary<string, CallState> arguments)
	{
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator);
		var key = MemoKey(function, executor, enactor, arguments);

		if (MemoStore.Find(key) is MemoEntry kept)
		{
			if (await MemoReadsHoldAsync(kept.Reads))
			{
				// An enclosing memo() depends on what this answer depended on.
				AttributeReadLog.Add(kept.Reads);
				return new CallState(kept.Value);
			}

			MemoStore.Forget(key);
		}

		return await EvaluateMemoAsync(parser, function, arguments, key);
	}

	private async ValueTask<CallState> EvaluateMemoAsync(IMUSHCodeParser parser, AttributeFunction function,
		Dictionary<string, CallState> arguments, string key)
	{
		if (MemoRefusesCode(parser, function.Code) is { } refused)
		{
			return new CallState(refused);
		}

		using var refusals = SideEffectFreeEvaluation.Track(deterministic: true);
		using var reads = AttributeReadLog.Open();

		var answer = await parser.With(
			// Nothing of the caller's frame reaches the code: no q-registers, no loop (itext(), ibreak()), no switch or
			// regular expression captures. The answer depends on its arguments and the attributes it reads only.
			state => state with
			{
				Registers = OwnRegisters(state.Registers),
				IterationRegisters = new ConcurrentStack<IterationWrapper<MString>>(),
				RegexRegisters = new ConcurrentStack<Dictionary<string, MString>>(),
				SwitchStack = new ConcurrentStack<SwitchFrame>()
			},
			scoped => AttributeService.CallAttributeFunctionAsync(scoped, function, arguments,
				new Dictionary<string, CallState>(arguments)));

		if (refusals.Refused)
		{
			return new CallState(MemoRefusesSideEffects);
		}

		if (refusals.RefusedNonDeterministic)
		{
			return new CallState(MemoRefusesNonDeterministic);
		}

		// An answer a limit cut short is not the answer, so it is given once and not kept.
		var cutShort = parser.CurrentState.LimitExceeded is { IsExceeded: true };
		if (!cutShort && !reads.IsUntracked && answer.Message.Length <= MemoMaxAnswerLength)
		{
			MemoStore.Keep(key, new MemoEntry(answer.Message, reads.Reads));
		}

		return answer;
	}

	/// <summary>
	/// The refusal for code that names a side-effect or non-deterministic function anywhere in it, even in a branch
	/// this call would not take; or null. Code reached through u() or a computed name is only seen as it runs, where the
	/// side-effect-free scope refuses it.
	/// </summary>
	private string? MemoRefusesCode(IMUSHCodeParser parser, MString code)
	{
		var analyzer = new SoftcodeSyntaxAnalyzer(parser.FunctionLibrary, Configuration);
		string? refusal = null;
		foreach (var call in analyzer.Calls(code))
		{
			if (!parser.FunctionLibrary.TryGetValue(call.Name.ToLowerInvariant(), out var found)) continue;
			var declared = found.LibraryInformation.Attribute;
			if (UnflaggedSideEffects.Contains(declared.Name)
				|| (declared.Flags.HasFlag(FunctionFlags.HasSideFX) && call.Arguments.Count >= declared.SideEffectMinArgs))
			{
				return MemoRefusesSideEffects;
			}

			if (declared.Flags.HasFlag(FunctionFlags.NonDeterministic))
			{
				refusal = MemoRefusesNonDeterministic;
			}
		}

		return refusal;
	}

	/// <summary>One empty q-register frame, so the code neither sees the caller's registers nor leaves its own.</summary>
	private static ConcurrentStack<Dictionary<string, MString>> OwnRegisters(
		ConcurrentStack<Dictionary<string, MString>> callers)
	{
		var comparer = callers.TryPeek(out var top) ? top.Comparer : StringComparer.OrdinalIgnoreCase;
		var own = new ConcurrentStack<Dictionary<string, MString>>();
		own.Push(new Dictionary<string, MString>(comparer));
		return own;
	}

	/// <summary>
	/// Whether each read still finds what it found, made again as the same executor. Reads made to check are not
	/// recorded in an enclosing memo(): the caller adds the entry's reads instead.
	/// </summary>
	private async ValueTask<bool> MemoReadsHoldAsync(IReadOnlyList<AttributeReadDependency> reads)
	{
		using var notRecorded = AttributeReadLog.Suspend();
		foreach (var read in reads)
		{
			if (await MemoObjectAsync(read.Executor) is not AnySharpObject reader
				|| await MemoObjectAsync(read.Object) is not AnySharpObject target)
			{
				return false;
			}

			// The dialect the read was made in decides which attribute an alias names, so the read is repeated in it.
			string fingerprint;
			using (SoftcodeDialectScope.Enter(read.Dialect))
			{
				fingerprint = await MemoReadAgainAsync(reader, target, read);
			}

			if (fingerprint != read.Fingerprint)
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>What <paramref name="read"/> finds now, as <see cref="AttributeReadLog"/> fingerprints it.</summary>
	private async ValueTask<string> MemoReadAgainAsync(AnySharpObject reader, AnySharpObject target,
		AttributeReadDependency read)
		=> read.Kind switch
		{
			AttributeReadKind.Visible => AttributeReadLog.Fingerprint(
				await AttributeService.GetVisibleAttributesAsync(reader, target, read.Depth)),
			AttributeReadKind.Pattern => AttributeReadLog.Fingerprint(
				await AttributeService.GetAttributePatternAsync(reader, target, read.Attribute, read.Parent, read.PatternMode)),
			_ => await AttributeService.GetAttributeAsync(reader, target, read.Attribute, read.Mode, read.Parent) switch
			{
				SharpAttribute[] { Length: > 0 } chain => AttributeReadLog.Fingerprint(chain[^1].Value),
				Error<string> error => "E" + error.Value,
				_ => AttributeReadLog.Absent
			}
		};

	/// <summary>The object <paramref name="dbref"/> names, unless it is gone or is now another object.</summary>
	private async ValueTask<AnyOptionalSharpObject> MemoObjectAsync(DBRef dbref)
		=> await Mediator.Send(new GetObjectNodeQuery(dbref)) switch
		{
			AnySharpObject found when found.Object().DBRef.Equals(dbref) => found,
			_ => new None()
		};

	/// <summary>
	/// What makes two calls the same: the object and attribute, the code itself (so changing it starts afresh), how
	/// it runs, who runs it and who caused it, and every argument with its markup.
	/// </summary>
	private static string MemoKey(AttributeFunction function, AnySharpObject executor, AnySharpObject enactor,
		Dictionary<string, CallState> arguments)
	{
		var key = new StringBuilder();
		void Part(string value) => key.Append(value.Length).Append(':').Append(value);

		Part(function.Owner.Object().DBRef.ToString());
		Part(function.Name);
		Part(AttributeReadLog.Fingerprint(function.Code));
		// A #lambda runs in its caller's dialect, which decides what its names mean.
		Part(function.Dialect ?? (function.Lambda is null ? string.Empty : SoftcodeDialectScope.Current?.Key ?? string.Empty));
		Part(function.NoEval ? "1" : "0");
		Part(function.RunAs?.Object().DBRef.ToString() ?? string.Empty);
		Part(executor.Object().DBRef.ToString());
		Part(enactor.Object().DBRef.ToString());
		Part(arguments.Count.ToString());
		foreach (var argument in arguments.OrderBy(pair => int.Parse(pair.Key)))
		{
			Part(AttributeReadLog.Fingerprint(argument.Value.Message));
		}

		return key.ToString();
	}
}
