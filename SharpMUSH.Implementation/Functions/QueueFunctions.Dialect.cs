using System.Collections.Frozen;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>RhostMUSH's <c>pid()</c> and <c>sandbox()</c>.</summary>
public partial class Functions
{
	/// <summary>The fields <c>pid()</c>'s display mask can ask for, in the order they are joined.</summary>
	[Flags]
	private enum PidFields
	{
		Pid = 1,
		Executor = 2,
		Enactor = 4,
		Remaining = 8,
		Status = 16,
		Command = 32,
		CurrentPid = 64,
		CurrentCommand = 128
	}

	/// <summary>
	/// <c>pid([&lt;display&gt;[, &lt;separator&gt;[, &lt;type&gt;[, &lt;target&gt;[, &lt;owned&gt;]]]]])</c>: the queue
	/// entries the caller may see, one per <c>&lt;separator&gt;</c>, each the fields <c>&lt;display&gt;</c> asks for
	/// joined by <c>|</c>. Display 64 and 128 stand alone: the running entry's pid and its action list.
	/// </summary>
	[SharpFunction(Name = "pid", MinArgs = 0, MaxArgs = 5, Flags = FunctionFlags.Regular | FunctionFlags.NonDeterministic | FunctionFlags.StripAnsi,
		ParameterNames = ["display", "separator", "type", "target", "owned"])]
	public async ValueTask<CallState> Pid(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		string Arg(int index) => ArgHelpers.NoParseDefaultNoParseArgument(args, index, string.Empty).ToPlainText();

		var display = (PidFields)Math.Clamp(LeadingInteger(Arg(0)), 0, int.MaxValue);
		if (display.HasFlag(PidFields.CurrentPid))
			return RunningQueueEntry.Pid is { } running ? new CallState(running.ToString(CultureInfo.InvariantCulture)) : new CallState("#-1");
		if (display.HasFlag(PidFields.CurrentCommand))
			return new CallState(await RunningCommandFor(await parser.CurrentState.KnownExecutorObject(Mediator)));
		if ((display & (PidFields.Pid | PidFields.Executor | PidFields.Enactor | PidFields.Remaining | PidFields.Status | PidFields.Command)) == 0)
			display = PidFields.Pid;

		var separator = Arg(1) is [var first, ..] ? first.ToString() : " ";
		var type = Arg(2) is [var typeChar, ..] ? char.ToLowerInvariant(typeChar) : 'q';
		if (type is not ('q' or 'w' or 's' or 'p' or 'o' or 'c'))
			return new CallState(ErrorMessages.Returns.InvalidArgument);

		IReadOnlyList<QueueEntrySnapshot> entries;
		try
		{
			entries = parser.ServiceProvider.GetRequiredService<ITaskQueueReader>().GetQueueEntries();
		}
		catch (NotSupportedException)
		{
			return new CallState(ErrorMessages.Returns.ErrorNotSupported);
		}

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		return await PidScope(parser, executor, Arg(3), Arg(4)) switch
		{
			Func<QueueEntrySnapshot, bool> wanted => await PidListingAsync(entries.Where(entry => entry.Source is not null && wanted(entry)),
				type, display, separator),
			CallState refusal => refusal
		};
	}

	/// <summary>
	/// The running entry's action list, when <paramref name="executor"/> may see that entry as <c>pid()</c>'s listing
	/// would show it; otherwise nothing, so code an entry calls does not learn what someone else queued.
	/// </summary>
	private static async ValueTask<string> RunningCommandFor(AnySharpObject executor)
	{
		if (RunningQueueEntry.Current is not { Command: { } command } running) return string.Empty;
		var executorOwner = (await executor.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken)).Object.DBRef;
		var visible = running.Executor?.Number == executor.Object().DBRef.Number
			|| running.Owner?.Number == executorOwner.Number
			|| await executor.IsPriv() || await executor.HasPower("SEE_QUEUE");
		return visible ? command.ToPlainText() : string.Empty;
	}

	/// <summary>Which entries <c>pid()</c> lists, or why it lists none.</summary>
	private union PidScopeOrRefusal(Func<QueueEntrySnapshot, bool>, CallState);

	/// <summary>
	/// Which entries <c>pid()</c> lists: one pid, the entries a located object runs (or, with <c>&lt;owned&gt;</c> on and
	/// a player, everything that player owns), or with no target every entry the caller may see.
	/// </summary>
	private async ValueTask<PidScopeOrRefusal> PidScope(IMUSHCodeParser parser,
		AnySharpObject executor, string target, string owned)
	{
		var seesAll = await executor.IsPriv() || await executor.HasPower("SEE_QUEUE");
		var executorRef = executor.Object().DBRef;
		var executorOwner = (await executor.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken)).Object.DBRef;
		bool Visible(QueueEntrySnapshot entry)
			=> seesAll || entry.Source?.Number == executorRef.Number || entry.Owner?.Number == executorOwner.Number;

		if (target.Length == 0)
			return new Func<QueueEntrySnapshot, bool>(Visible);

		if (long.TryParse(target, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var pid))
			return new Func<QueueEntrySnapshot, bool>(entry => entry.Pid == pid && Visible(entry));

		if (await LocateService.Locate(parser, executor, executor, target, LocateFlags.All) is not AnySharpObject found)
			return new CallState(ErrorMessages.Returns.NoMatch);
		if (!seesAll && !await PermissionService.Controls(executor, found))
			return PermissionRefusal.NotControlled(found, SeeQueueScopes).Return;

		var foundRef = found.Object().DBRef;
		return LeadingInteger(owned) != 0 && found.IsPlayer
			? new Func<QueueEntrySnapshot, bool>(entry => entry.Owner?.Number == foundRef.Number)
			: new Func<QueueEntrySnapshot, bool>(entry => entry.Source?.Number == foundRef.Number);
	}

	private async ValueTask<CallState> PidListingAsync(IEnumerable<QueueEntrySnapshot> visible, char type, PidFields display, string separator)
	{
		var entries = visible.ToArray();
		var waiting = entries.Where(entry => entry.Kind == "delay" && entry.State is QueueEntryState.Pending or QueueEntryState.Paused)
			.OrderBy(entry => entry.RemainingDelay ?? TimeSpan.MaxValue).ThenBy(entry => entry.Pid);
		var semaphores = entries.Where(entry => entry.Kind == "semaphore" && entry.State is QueueEntryState.Pending or QueueEntryState.Paused);
		var running = entries.Where(entry => entry.State is QueueEntryState.Ready or QueueEntryState.Running);

		IEnumerable<QueueEntrySnapshot> listed = type switch
		{
			'w' => waiting,
			's' => semaphores,
			'p' => await RunByAsync(running, players: true),
			'o' => await RunByAsync(running, players: false),
			_ => waiting.Concat(semaphores)
		};

		return type == 'c'
			? new CallState(listed.Count().ToString(CultureInfo.InvariantCulture))
			: new CallState(string.Join(separator, listed.Select(entry => PidRow(entry, display))));
	}

	/// <summary>The running entries whose executor is a player, or those whose executor is not.</summary>
	private async ValueTask<IEnumerable<QueueEntrySnapshot>> RunByAsync(IEnumerable<QueueEntrySnapshot> running, bool players)
	{
		var kept = new List<QueueEntrySnapshot>();
		foreach (var entry in running)
		{
			var isPlayer = await Mediator.Send(new GetObjectNodeQuery(entry.Source!.Value)) is AnySharpObject { IsPlayer: true };
			if (isPlayer == players) kept.Add(entry);
		}

		return kept;
	}

	private static string PidRow(QueueEntrySnapshot entry, PidFields display)
	{
		IEnumerable<string?> fields =
		[
			display.HasFlag(PidFields.Pid) ? entry.Pid.ToString(CultureInfo.InvariantCulture) : null,
			display.HasFlag(PidFields.Executor) ? $"#{entry.Source!.Value.Number}" : null,
			display.HasFlag(PidFields.Enactor) ? $"#{(entry.Enactor ?? entry.Source!.Value).Number}" : null,
			display.HasFlag(PidFields.Remaining)
				? (entry is { Timed: true, RemainingDelay: { } remaining } ? (long)remaining.TotalSeconds : 0).ToString(CultureInfo.InvariantCulture)
				: null,
			display.HasFlag(PidFields.Status) ? entry.State == QueueEntryState.Paused ? "STOPPED" : "RUNNING" : null,
			display.HasFlag(PidFields.Command) ? entry.Command?.ToPlainText() ?? string.Empty : null
		];
		return string.Join('|', fields.OfType<string>());
	}

	/// <summary>
	/// <c>sandbox(&lt;expression&gt;, &lt;functions&gt;[, &lt;unknown&gt;[, &lt;user functions&gt;[, &lt;allow&gt;]]])</c>:
	/// <c>&lt;expression&gt;</c> evaluated with the functions in the space-separated <c>&lt;functions&gt;</c> refused,
	/// or, with <c>&lt;allow&gt;</c> on, with every other function refused.
	/// </summary>
	[SharpFunction(Name = "sandbox", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.NoParse,
		ParameterNames = ["expression", "functions", "unknown", "user functions", "allow"])]
	public async ValueTask<CallState> Sandbox(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		async ValueTask<string> Evaluated(int index)
			=> args.TryGetValue(index.ToString(CultureInfo.InvariantCulture), out var raw)
				? (await ParseRawArgument(parser, raw.Message!))?.Message?.ToPlainText() ?? string.Empty
				: string.Empty;

		var expression = args["0"].Message!;
		var names = (await Evaluated(1)).Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
		if (names.Length == 0)
			return await ParseRawArgument(parser, expression) ?? CallState.Empty;

		var layer = new FunctionSandbox.Layer(
			names.ToFrozenSet(StringComparer.OrdinalIgnoreCase),
			AllowList: LeadingInteger(await Evaluated(4)) != 0,
			AsUnknown: LeadingInteger(await Evaluated(2)) != 0,
			CoversUserFunctions: (LeadingInteger(await Evaluated(3)) & 1) != 0);
		using var scope = FunctionSandbox.Enter(layer);
		return await ParseRawArgument(parser, expression) ?? CallState.Empty;
	}
}
