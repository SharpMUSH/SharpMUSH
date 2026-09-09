using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Snapshots;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Snapshots;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@SNAPSHOT", Switches = ["CAPTURE", "LIST", "PREVIEW", "RESTORE", "RESOLVE", "LOCKS", "FLAGS", "NAME"],
		Behavior = CB.Default | CB.EqSplit | CB.Switches | CB.NoGagged, MinArgs = 1, MaxArgs = 2,
		ParameterNames = ["object", "description or snapshot-id,preview-token"])]
	public async ValueTask<Option<CallState>> Snapshot(IMUSHCodeParser parser, SharpCommandAttribute command)
	{
		if (await RejectIfTooFewArguments(parser, command) is { } invalid) return invalid;
		var cancellationToken = ExecutionBudget.CurrentToken;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var capabilities = parser.ServiceProvider.GetRequiredService<IAdministrativeCapabilityService>();
		var snapshots = parser.ServiceProvider.GetRequiredService<IObjectSnapshotService>();
		var actor = await capabilities.GetGameActorAsync(executor.Object().DBRef, cancellationToken);
		if (actor is null) return await NotifyService.NotifyAndReturn(executor.Object().DBRef, "#-1 PERMISSION DENIED", "A linked player executor is required.", true);
		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(), LocateFlags.All, async obj =>
			{
				string output;
				try
				{
					var switches = parser.CurrentState.Switches;
					var rhs = parser.CurrentState.Arguments.TryGetValue("1", out var argument) ? argument.Message?.ToPlainText() ?? "" : "";
					var operations = switches.Where(s => s is "CAPTURE" or "LIST" or "PREVIEW" or "RESTORE" or "RESOLVE").ToArray();
					if (operations.Length != 1) throw new SnapshotOperationException("invalid", "Choose one of /capture, /list, /preview, /restore or /resolve.");
					if (operations[0] == "CAPTURE") output = "Captured snapshot " + (await snapshots.CaptureAsync(actor, obj.Object().DBRef, rhs, ct: cancellationToken)).Id;
					else if (operations[0] == "RESOLVE")
					{
						await snapshots.ResolveRecoveryAsync(actor, obj.Object().DBRef, rhs, cancellationToken);
						output = "Recovery marker acknowledged; current object kept.";
					}
					else
					{
						var history = await snapshots.ListAsync(actor, obj.Object().DBRef, cancellationToken);
						if (operations[0] == "LIST") output = string.Join('\n', history.Snapshots.Select(s => $"{s.Id} {s.Description} ({s.CreatedAt})")) +
							(history.PendingRecoveryId is { } recovery ? "\nPending recovery: " + recovery : "");
						else
						{
							var parts = rhs.Split(',', 2);
							var saved = history.Snapshots.SingleOrDefault(s => s.Id == parts[0]) ?? throw new SnapshotOperationException("missing", "Snapshot not found.");
							var selection = new SnapshotSelection(saved.DefaultAttributes(), switches.Contains("LOCKS"), switches.Contains("FLAGS"), switches.Contains("NAME"));
							if (operations[0] == "PREVIEW")
							{
								var preview = await snapshots.PreviewAsync(actor, obj.Object().DBRef, saved.Id, selection, cancellationToken);
								output = string.Join('\n', preview.Changes.Select(c => c.Field + ": " + c.Before + " -> " + c.After)) + "\nPreview token: " + preview.Token;
							}
							else
							{
								if (parts.Length != 2) throw new SnapshotOperationException("invalid", "Restore requires snapshot-id,preview-token.");
								var result = await snapshots.RestoreAsync(actor, obj.Object().DBRef, saved.Id, selection, parts[1], cancellationToken);
								output = result.Completed ? "Restored. Recovery snapshot: " + result.RecoverySnapshotId : result.Error!;
							}
						}
					}
				}
				catch (SnapshotOperationException ex) { output = "#-1 " + ex.Message; }
				await NotifyService.Notify(executor, output);
				return new CallState(output);
			});
	}
}
