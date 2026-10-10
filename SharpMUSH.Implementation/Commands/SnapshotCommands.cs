using System.Globalization;
using MarkupString.Layout;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
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
			parser.CurrentState.Arguments["0"].Message.ToPlainText(), LocateFlags.All, async obj =>
			{
				MString output;
				try
				{
					var switches = parser.CurrentState.Switches;
					var rhs = parser.CurrentState.Arguments.TryGetValue("1", out var argument) ? argument.Message.ToPlainText() : "";
					var operations = switches.Where(s => s is "CAPTURE" or "LIST" or "PREVIEW" or "RESTORE" or "RESOLVE").ToArray();
					if (operations.Length != 1) throw new SnapshotOperationException("invalid", "Choose one of /capture, /list, /preview, /restore or /resolve.");
					if (operations[0] == "CAPTURE") output = MarkupText.Plain("Captured snapshot " + (await snapshots.CaptureAsync(actor, obj.Object().DBRef, rhs, ct: cancellationToken)).Id);
					else if (operations[0] == "RESOLVE")
					{
						await snapshots.ResolveRecoveryAsync(actor, obj.Object().DBRef, rhs, cancellationToken);
						output = MarkupText.Plain("Recovery marker acknowledged; current object kept.");
					}
					else
					{
						var history = await snapshots.ListAsync(actor, obj.Object().DBRef, cancellationToken);
						if (operations[0] == "LIST") output = SnapshotListing(obj.Object(), history);
						else
						{
							var parts = rhs.Split(',', 2);
							var saved = history.Snapshots.SingleOrDefault(s => s.Id == parts[0]) ?? throw new SnapshotOperationException("missing", "Snapshot not found.");
							var selection = new SnapshotSelection(saved.DefaultAttributes(), switches.Contains("LOCKS"), switches.Contains("FLAGS"), switches.Contains("NAME"));
							if (operations[0] == "PREVIEW")
							{
								var preview = await snapshots.PreviewAsync(actor, obj.Object().DBRef, saved.Id, selection, cancellationToken);
								output = SnapshotPreviewListing(preview);
							}
							else
							{
								if (parts.Length != 2) throw new SnapshotOperationException("invalid", "Restore requires snapshot-id,preview-token.");
								var result = await snapshots.RestoreAsync(actor, obj.Object().DBRef, saved.Id, selection, parts[1], cancellationToken);
								output = MarkupText.Plain(result.Completed ? "Restored. Recovery snapshot: " + result.RecoverySnapshotId : result.Error!);
							}
						}
					}
				}
				catch (SnapshotOperationException ex) { output = MarkupText.Plain("#-1 " + ex.Message); }
				await NotifyService.Notify(executor, output);
				return new CallState(output);
			});
	}

	/// <summary>An object's snapshots, newest last, and any restore still waiting to be resolved.</summary>
	private static MString SnapshotListing(SharpObject target, SnapshotHistory history)
	{
		Block snapshots = history.Snapshots.Length == 0
			? new TextBlock(MarkupText.Plain("No snapshots."))
			: ServerLayout.Listing(
				[
					new TableColumn(MarkupText.Plain("Snapshot")) { Wrap = false },
					new TableColumn(MarkupText.Plain("Description")) { Min = 10 },
					new TableColumn(MarkupText.Plain("Taken")) { Wrap = false, Priority = 2 },
				],
				history.Snapshots.Select(snapshot => (IEnumerable<string>)
				[
					snapshot.Id, snapshot.Description,
					DateTimeOffset.FromUnixTimeMilliseconds(snapshot.CreatedAt).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC"
				]));
		var title = MarkupText.Plain($"Snapshots of {target.Name}(#{target.DBRef.Number})");
		return ServerLayout.Build(history.PendingRecoveryId is { } recovery
			? ServerLayout.Panel(title, snapshots, new Rule(),
				ServerLayout.KeyValues([("Pending recovery", MarkupText.Plain(recovery))]))
			: ServerLayout.Panel(title, snapshots), 78);
	}

	/// <summary>What a restore would change, field by field, then the token that confirms it.</summary>
	private static MString SnapshotPreviewListing(SnapshotPreview preview)
	{
		Block changes = preview.Changes.Length == 0
			? new TextBlock(MarkupText.Plain("Nothing would change."))
			: ServerLayout.Listing(
				[
					new TableColumn(MarkupText.Plain("Field")) { Wrap = false },
					new TableColumn(MarkupText.Plain("Now")) { Min = 10 },
					new TableColumn(MarkupText.Plain("Restored")) { Min = 10 },
				],
				preview.Changes.Select(change => (IEnumerable<string>)[change.Field, change.Before, change.After]));
		// The token stays outside the frame, on one line, so it copies whole.
		return MarkupText.Join(MarkupText.NewLine, [
			ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain($"Restore preview of {preview.SnapshotId}"), changes), 78),
			MarkupText.Plain("Preview token: " + preview.Token)]);
	}
}
