using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>Settings for <c>POST api/commands</c>, bound from the <c>PortalCommands</c> section.</summary>
public class PortalCommandOptions
{
	public const string Section = "PortalCommands";

	/// <summary>
	/// How many commands one account may have waiting in the queue or running at once. A request over
	/// it is answered 429 without being queued.
	/// </summary>
	public int MaxPendingPerAccount { get; set; } = 4;
}

/// <summary>The account already has <see cref="Limit"/> commands waiting or running.</summary>
public readonly record struct TooManyCommands(int Limit);

/// <summary>What a portal command came to: its answer, a refusal for the account's limit, or why it did not run.</summary>
public union PortalCommandOutcome(PortalCommandResponse, TooManyCommands, Error<string>);

/// <summary>
/// Runs a command the web portal sends as the account session's character, and answers with what the
/// character was told while it ran — the route behind <c>POST api/commands</c>.
/// </summary>
public interface IPortalCommandService
{
	/// <summary>
	/// Queues <paramref name="request"/>'s command as a line <paramref name="character"/> typed, waits for
	/// its queue entry to run, and returns the output it produced and the value of its result expression.
	/// </summary>
	/// <param name="account">The account the request came from, whose pending commands are bounded.</param>
	/// <returns>The answer, a refusal when <paramref name="account"/> already has as many commands pending
	/// as it may, or why the command did not run to completion.</returns>
	ValueTask<PortalCommandOutcome> RunAsync(string account, SharpPlayer character, PortalCommandRequest request,
		CancellationToken ct = default);

	/// <summary>
	/// Queues an evaluation of <paramref name="request"/>'s expression, with its arguments as <c>%0</c>-<c>%9</c>,
	/// waits for it to run, and returns its value and whatever <paramref name="character"/> was told meanwhile.
	/// It runs as <paramref name="self"/> when given, which the caller has already checked
	/// <paramref name="character"/> controls; <c>%#</c> and <c>%@</c> stay <paramref name="character"/>.
	/// </summary>
	/// <param name="account">The account the request came from, whose pending commands are bounded.</param>
	ValueTask<PortalCommandOutcome> EvaluateAsync(string account, SharpPlayer character, DBRef? self,
		PortalEvalRequest request, CancellationToken ct = default);
}

/// <inheritdoc />
/// <remarks>
/// <para>The command is a queue entry of its own, admitted as socket input is: like a typed line it is
/// not charged to the character's queue quota (PennMUSH's <c>QUEUE_SOCKET</c>), and it runs on the
/// queue's single consumer, never beside other softcode. The result expression is evaluated in that same
/// entry, so what it reads is what the command left.</para>
///
/// <para>Each account may have <see cref="PortalCommandOptions.MaxPendingPerAccount"/> commands in the
/// queue at once. A slot is held from admission until the entry leaves the queue — not until the request
/// stops waiting — so a client that abandons its requests cannot pile entries up behind them.</para>
///
/// <para>It needs no connection. The character's output is copied for the answer while it is still
/// delivered to every connection the character has, so a player watching a terminal sees what the
/// portal did on their behalf.</para>
/// </remarks>
public sealed class PortalCommandService(
	IMUSHCodeParser parser,
	ITaskScheduler scheduler,
	ICommandOutputCapture outputCapture,
	IMediator mediator,
	IOptionsWrapper<SharpMUSHOptions> gameOptions,
	IOptions<PortalCommandOptions> options,
	ILogger<PortalCommandService> logger) : IPortalCommandService
{
	/// <summary>The answer when the command's queue entry left the queue without running.</summary>
	public const string Halted = "#-1 QUEUE ENTRY HALTED";

	private readonly Lock _pendingLock = new();
	private readonly Dictionary<string, int> _pending = new(StringComparer.Ordinal);

	/// <inheritdoc />
	public async ValueTask<PortalCommandOutcome> RunAsync(string account, SharpPlayer character, PortalCommandRequest request,
		CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		return await HoldingSlotAsync(account, character.Object.DBRef,
			queued => QueueAsync(account, character.Object.DBRef, () => ExecuteAsync(character.Object.DBRef, request), queued, ct));
	}

	/// <inheritdoc />
	public async ValueTask<PortalCommandOutcome> EvaluateAsync(string account, SharpPlayer character, DBRef? self,
		PortalEvalRequest request, CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		var actor = character.Object.DBRef;
		return await HoldingSlotAsync(account, actor,
			queued => QueueAsync(account, actor, () => EvaluateCoreAsync(actor, self ?? actor, request), queued, ct));
	}

	private async ValueTask<PortalCommandOutcome> HoldingSlotAsync(string account, DBRef actor,
		Func<Action, ValueTask<PortalCommandOutcome>> queue)
	{
		var limit = Math.Max(1, options.Value.MaxPendingPerAccount);
		if (!TryHoldSlot(account, limit))
		{
			logger.LogInformation("Portal command for {Character} refused: the account has {Limit} pending.", actor, limit);
			return new TooManyCommands(limit);
		}

		var queued = false;
		try
		{
			return await queue(() => queued = true);
		}
		finally
		{
			// Once the entry is queued its release frees the slot; until then nothing else will.
			if (!queued) ReleaseSlot(account);
		}
	}

	private async ValueTask<PortalCommandOutcome> QueueAsync(string account, DBRef actor,
		Func<ValueTask<Result<PortalCommandResponse>>> execute, Action queued, CancellationToken ct)
	{
		var completion = new TaskCompletionSource<Result<PortalCommandResponse>>(TaskCreationOptions.RunContinuationsAsynchronously);
		var abandoned = false;
		var admission = await scheduler.AdmitSocketWork(async () =>
		{
			// A caller that gave up while the command waited its turn has nobody to answer, and a command
			// nobody is waiting on is one the player no longer asked for.
			if (Volatile.Read(ref abandoned)) return null;
			var execution = execute().AsTask();
			// The outcome, a fault included, belongs to the request waiting on it, not to the queue.
			await ((Task)execution).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
			completion.TrySetFromTask(execution);
			return null;
		}, "portal-command", "portal",
			// An entry halted before it ran, or dropped at shutdown, still answers the request. Either way
			// the entry is out of the queue, so the account's slot is free again.
			onReleased: () =>
			{
				completion.TrySetResult(new Error<string>(Halted));
				ReleaseSlot(account);
			});
		if (!admission.Accepted)
		{
			logger.LogWarning("Portal command for {Character} refused by the queue: {Reason}.", actor, admission.Reason);
			return new Error<string>(admission.Error);
		}

		queued();
		try
		{
			return await completion.Task.WaitAsync(ct) switch
			{
				PortalCommandResponse response => response,
				Error<string> error => error,
			};
		}
		finally { Volatile.Write(ref abandoned, true); }
	}

	private bool TryHoldSlot(string account, int limit)
	{
		lock (_pendingLock)
		{
			var held = _pending.GetValueOrDefault(account);
			if (held >= limit) return false;
			_pending[account] = held + 1;
			return true;
		}
	}

	private void ReleaseSlot(string account)
	{
		lock (_pendingLock)
		{
			var held = _pending.GetValueOrDefault(account);
			if (held <= 1) _pending.Remove(account);
			else _pending[account] = held - 1;
		}
	}

	private async ValueTask<Result<PortalCommandResponse>> ExecuteAsync(DBRef actor, PortalCommandRequest request)
	{
		var transcript = new CommandTranscript(FunctionLimits.MaxOutputCodeUnits);
		string? result = null;
		try
		{
			using (outputCapture.BeginCapture(actor.Number, transcript))
			{
				await parser.CommandParse(actor, MarkupText.Plain(request.Command));
			}

			if (!string.IsNullOrWhiteSpace(request.Result) && !BudgetExpired())
			{
				// The command ran under the character's own output ceiling (a guest's is guest_output_limit);
				// the expression read after it is the character's code too, and gets the same one.
				var value = await parser.Push(ParserState.RootFor(actor) with { OutputLimit = await OutputLimitAsync(actor) })
					.FunctionParse(MarkupText.Plain(request.Result));
				result = value?.Message?.ToPlainText() ?? string.Empty;
			}
		}
		catch (OperationCanceledException) when (BudgetExpired())
		{
			// The entry's CPU limit fired mid-await. Answered below, the same as a limit the parser
			// reported by returning its error instead of throwing.
		}

		return BudgetExpired()
			? new Error<string>(ExecutionBudget.Error)
			: new PortalCommandResponse(transcript.Lines, result, transcript.Truncated);
	}

	/// <summary>
	/// Evaluates the expression as <paramref name="self"/> on <paramref name="actor"/>'s behalf: the arguments
	/// first, as <paramref name="actor"/>, then the expression with them as <c>%0</c>-<c>%9</c> — what
	/// <c>u(self/ATTR, ...)</c> typed by <paramref name="actor"/> does with the attribute's text.
	/// </summary>
	private async ValueTask<Result<PortalCommandResponse>> EvaluateCoreAsync(DBRef actor, DBRef self, PortalEvalRequest request)
	{
		var transcript = new CommandTranscript(FunctionLimits.MaxOutputCodeUnits);
		var result = string.Empty;
		try
		{
			using (outputCapture.BeginCapture(actor.Number, transcript))
			{
				// The character's output ceiling, whoever the code runs as: it is the character's request.
				var asCharacter = ParserState.RootFor(actor) with { OutputLimit = await OutputLimitAsync(actor) };
				var arguments = new Dictionary<string, CallState>();
				foreach (var argument in request.Arguments ?? [])
				{
					if (BudgetExpired()) break;
					arguments[arguments.Count.ToString()] =
						await parser.Push(asCharacter).FunctionParse(MarkupText.Plain(argument)) ?? CallState.Empty;
				}

				if (!BudgetExpired())
				{
					var value = await parser.Push(asCharacter with
					{
						Executor = self,
						Arguments = arguments,
						EnvironmentRegisters = arguments
					}).FunctionParse(MarkupText.Plain(request.Expression));
					result = value?.Message?.ToPlainText() ?? string.Empty;
				}
			}
		}
		catch (OperationCanceledException) when (BudgetExpired())
		{
			// As in ExecuteAsync: the entry's CPU limit fired mid-await.
		}

		return BudgetExpired()
			? new Error<string>(ExecutionBudget.Error)
			: new PortalCommandResponse(transcript.Lines, result, transcript.Truncated);
	}

	private async ValueTask<int> OutputLimitAsync(DBRef actor) =>
		await FunctionLimits.OutputLimitForAsync(
			await mediator.Send(new GetObjectNodeQuery(actor)) is AnySharpObject actorObject ? actorObject : null,
			gameOptions.CurrentValue.Limit.GuestOutputLimit);

	private static bool BudgetExpired() => ExecutionBudget.Current?.IsExpired == true;
}
