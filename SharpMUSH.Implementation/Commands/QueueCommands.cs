using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using OneOf;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@QUEUE", Switches = ["LIST", "PAUSE", "RESUME", "OWNER", "OBJECT"],
		Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 2, ParameterNames = ["pid or target", "reason"])]
	public async ValueTask<Option<CallState>> QueueControl(IMUSHCodeParser parser, SharpCommandAttribute _)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches.ToHashSet(StringComparer.Ordinal);
		var pause = switches.Contains("PAUSE");
		var resume = switches.Contains("RESUME");
		var owner = switches.Contains("OWNER");
		var source = switches.Contains("OBJECT");
		var args = parser.CurrentState.Arguments;
		var selection = args.GetValueOrDefault("0")?.Message?.ToPlainText() ?? "";
		var reason = args.GetValueOrDefault("1")?.Message?.ToPlainText() ?? "";
		if ((pause && resume) || ((pause || resume) && switches.Contains("LIST")) || (owner && source)
			|| (!pause && args.Count > 1) || ((pause || resume || owner || source) && string.IsNullOrWhiteSpace(selection)))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QueueControlSyntax), executor);
			return new CallState(ErrorMessages.Returns.InvalidTarget);
		}
		if (reason.Length > 160 || reason.Any(char.IsControl))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QueueControlBadReason), executor);
			return new CallState(ErrorMessages.Returns.InvalidTarget);
		}
		var ct = ExecutionBudget.CurrentToken;
		var actor = await parser.ServiceProvider.GetRequiredService<IAdministrativeCapabilityService>()
			.GetGameActorAsync(executor.Object().DBRef, ct);
		if (actor is null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}
		var service = parser.ServiceProvider.GetRequiredService<IQueueControlService>();
		var scheduler = parser.ServiceProvider.GetRequiredService<ITaskScheduler>();
		IEnumerable<QueueEntrySnapshot> entries = pause || resume ? scheduler.GetQueueEntries() : await service.ListAsync(actor, ct);
		if (owner || source)
		{
			// Bulk selection uses only inspectable records; each mutation independently rechecks control.
			if (pause || resume) entries = await service.ListAsync(actor, ct);
			var found = await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, selection, LocateFlags.All);
			if (!found.IsValid()) return new CallState(ErrorMessages.Returns.InvalidTarget);
			var target = found.WithoutError().WithoutNone().Object().DBRef;
			entries = entries.Where(e => (owner ? e.Owner : e.Source) == target);
			if (pause || resume) entries = entries.Where(e => e.State == (pause ? QueueEntryState.Pending : QueueEntryState.Paused));
		}
		else if (!string.IsNullOrWhiteSpace(selection))
		{
			if (!long.TryParse(selection, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0)
				return new CallState(ErrorMessages.Returns.InvalidPid);
			if (pause || resume)
			{
				var result = await service.ChangeAsync(actor, pid, resume, reason, ct);
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QueueControlOutcome), executor, pid, result);
				return CallState.Empty;
			}
			entries = entries.Where(e => e.Pid == pid);
		}
		var batch = entries.OrderBy(e => e.Pid).Take(201).ToArray();
		foreach (var entry in batch.Take(200))
		{
			ExecutionBudget.Current?.ThrowIfExceeded();
			if (pause || resume)
			{
				var result = await service.ChangeAsync(actor, entry.Pid, resume, reason, ct);
				// An inaccessible record must not disclose its PID through a bulk operation.
				if (result != QueueControlResult.NotFound)
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QueueControlOutcome), executor, entry.Pid, result);
			}
			else
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QueueControlEntry), executor,
					entry.Pid, entry.Source?.ToString() ?? "?", entry.Owner?.ToString() ?? "?", entry.Kind, entry.State,
					entry.RemainingDelay?.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) ?? "-", entry.ReleasePending, entry.PauseReason);
		}
		if (batch.Length > 200)
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QueueControlTruncated), executor);
		else if (batch.Length == 0)
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QueueControlEmpty), executor);
		return CallState.Empty;
	}
}
