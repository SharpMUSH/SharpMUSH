using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

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
	/// <returns>The answer, or why the command did not run to completion.</returns>
	ValueTask<Result<PortalCommandResponse>> RunAsync(SharpPlayer character, PortalCommandRequest request,
		CancellationToken ct = default);
}

/// <inheritdoc />
/// <remarks>
/// <para>The command is a queue entry of its own, admitted as socket input is: like a typed line it is
/// not charged to the character's queue quota (PennMUSH's <c>QUEUE_SOCKET</c>), and it runs on the
/// queue's single consumer, never beside other softcode. The result expression is evaluated in that same
/// entry, so what it reads is what the command left.</para>
///
/// <para>It needs no connection. The character's output is copied for the answer while it is still
/// delivered to every connection the character has, so a player watching a terminal sees what the
/// portal did on their behalf.</para>
/// </remarks>
public sealed class PortalCommandService(
	IMUSHCodeParser parser,
	ITaskScheduler scheduler,
	ICommandOutputCapture outputCapture,
	ILogger<PortalCommandService> logger) : IPortalCommandService
{
	/// <summary>The answer when the command's queue entry left the queue without running.</summary>
	public const string Halted = "#-1 QUEUE ENTRY HALTED";

	/// <inheritdoc />
	public async ValueTask<Result<PortalCommandResponse>> RunAsync(SharpPlayer character, PortalCommandRequest request,
		CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		var actor = character.Object.DBRef;
		var completion = new TaskCompletionSource<Result<PortalCommandResponse>>(TaskCreationOptions.RunContinuationsAsynchronously);
		var abandoned = false;
		var admission = await scheduler.AdmitSocketWork(async () =>
		{
			// A caller that gave up while the command waited its turn has nobody to answer, and a command
			// nobody is waiting on is one the player no longer asked for.
			if (Volatile.Read(ref abandoned)) return null;
			var execution = ExecuteAsync(actor, request).AsTask();
			// The outcome, a fault included, belongs to the request waiting on it, not to the queue.
			await ((Task)execution).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
			completion.TrySetFromTask(execution);
			return null;
		}, "portal-command", "portal",
			// An entry halted before it ran, or dropped at shutdown, still answers the request.
			onReleased: () => completion.TrySetResult(new Error<string>(Halted)));
		if (!admission.Accepted)
		{
			logger.LogWarning("Portal command for {Character} refused by the queue: {Reason}.", actor, admission.Reason);
			return new Error<string>(admission.Error);
		}

		try { return await completion.Task.WaitAsync(ct); }
		finally { Volatile.Write(ref abandoned, true); }
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
				var value = await parser.Push(ParserState.RootFor(actor)).FunctionParse(MarkupText.Plain(request.Result));
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

	private static bool BudgetExpired() => ExecutionBudget.Current?.IsExpired == true;
}
