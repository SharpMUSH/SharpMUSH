using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public partial class CommunicationService
{
	public async ValueTask<CallState> EmitAsync(IMUSHCodeParser parser, EmitRequest request)
		=> (await EmitWithOutcomeAsync(parser, request)).Result;

	public async ValueTask<EmitOutcome> EmitWithOutcomeAsync(IMUSHCodeParser parser, EmitRequest request)
	{
		var admitted = false;
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var speaker = executor;
		if (request.Spoof)
		{
			var enactor = await parser.CurrentState.KnownEnactorObject(mediator);
			if (await permissionService.CanSpoofAs(executor, enactor)) speaker = enactor;
		}
		var noSpoof = request.NoSpoof && await permissionService.CanNoSpoof(executor);
		var type = request.Scope switch
		{
			EmitScope.Private when request.PortTargets => noSpoof ? INotifyService.NotificationType.NSAnnounce : INotifyService.NotificationType.Announce,
			EmitScope.Private or EmitScope.Prompt => noSpoof ? INotifyService.NotificationType.NSPrivateEmit : INotifyService.NotificationType.PrivateEmit,
			_ => noSpoof ? INotifyService.NotificationType.NSEmit : INotifyService.NotificationType.Emit
		};

		switch (request.Scope)
		{
			case EmitScope.Private:
			case EmitScope.Prompt:
				return await EmitPrivateAsync(parser, executor, speaker, request, type);
			case EmitScope.Immediate:
			case EmitScope.Outermost:
				{
					if (request.Scope == EmitScope.Outermost && !executor.IsPlayer && !executor.IsThing) break;
					var location = request.Scope == EmitScope.Immediate ? await executor.Where() : await executor.OutermostWhere();
					if (request.Scope == EmitScope.Outermost && !location.IsRoom)
					{
						await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.LemitTooManyContainers), executor);
						break;
					}
					if (!await MayEmitInAsync(parser, executor, speaker, location, request.Scope == EmitScope.Immediate)) break;
					admitted = true;
					await EmitLocationAsync(executor, speaker, location, request.Message, type);
					if (request.Scope == EmitScope.Outermost && !request.Silent && (await executor.Where()).Object().DBRef != location.Object().DBRef)
						await notifyService.NotifyLocalizedMarkup(executor, nameof(ErrorMessages.Notifications.YouLemitFormat), executor, request.Message);
					break;
				}
			case EmitScope.Room:
				foreach (var name in request.Targets)
				{
					if (await locateService.LocateAndNotifyIfInvalid(parser, executor, executor, name, LocateFlags.All) is not AnySharpObject target) continue;
					if (!target.IsContainer)
					{
						await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ThereCantBeAnythingInThat), executor);
						continue;
					}
					if (!await MayPemitAsync(parser, speaker, target) || !await MayEmitInAsync(parser, executor, speaker, target.AsContainer)) continue;
					admitted = true;
					await EmitLocationAsync(executor, speaker, target.AsContainer, request.Message, type);
					if (!request.Silent && (await executor.Where()).Object().DBRef != target.Object().DBRef)
						await notifyService.NotifyLocalizedMarkup(executor, nameof(ErrorMessages.Notifications.YouRemitInFormat), executor,
							request.Message, MString.Plain($"{target.Object().Name}(#{target.Object().DBRef.Number})"));
				}
				break;
			case EmitScope.Omit:
				return await EmitOmitAsync(parser, executor, speaker, request, type);
			case EmitScope.Zone:
				foreach (var name in request.Targets)
				{
					if (await locateService.LocateAndNotifyIfInvalid(parser, executor, executor, name, LocateFlags.All) is not AnySharpObject zone) continue;
					if (!await permissionService.Controls(executor, zone))
					{
						await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
						continue;
					}
					var heardHere = false;
					var here = (await executor.Where()).Object().DBRef;
					await foreach (var item in mediator.CreateStream(new GetObjectsByZoneQuery(zone), ExecutionBudget.CurrentToken))
					{
						if (item.Type != "ROOM" || await mediator.Send(new GetObjectNodeQuery(item.DBRef), ExecutionBudget.CurrentToken) is not AnySharpObject room) continue;
						if (!await MayEmitInAsync(parser, executor, speaker, room.AsContainer, reportFailure: false)) continue;
						admitted = true;
						heardHere |= await EmitLocationAsync(executor, speaker, room.AsContainer, request.Message, type, observe: here);
					}
					if (!request.Silent && !heardHere)
						await notifyService.NotifyLocalizedMarkup(executor, nameof(ErrorMessages.Notifications.YouZemitInZoneFormat), executor,
							request.Message, MString.Plain($"{zone.Object().Name}(#{zone.Object().DBRef.Number})"));
				}
				break;
		}
		return new EmitOutcome(admitted, CallState.Empty);
	}

	private async ValueTask<bool> MayEmitInAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject speaker, AnySharpContainer location, bool here = false, bool reportFailure = true)
	{
		if (await speaker.IsLoud() || await lockService.Evaluate(LockType.Speech, location.WithExitOption(), speaker)) return true;
		if (reportFailure)
			await didItService.Value.FailLockLocalized(parser, executor, location.WithExitOption(), LockType.Speech,
				new LocalizedNotification(here ? nameof(ErrorMessages.Notifications.MayNotSpeakHere) : nameof(ErrorMessages.Notifications.MayNotSpeakThere)));
		return false;
	}

	private async ValueTask<bool> MayPemitAsync(IMUSHCodeParser parser, AnySharpObject speaker, AnySharpObject target, bool showDefault = true)
	{
		if (await speaker.IsWizard() || await speaker.HasPower("Pemit_All")) return true;
		var refusal = new LocalizedNotification(nameof(ErrorMessages.Notifications.PemitTargetWishesAlone), target.Object().Name);
		if (target.IsPlayer && await target.HasFlag("HAVEN"))
		{
			if (showDefault) await notifyService.NotifyLocalized(speaker, refusal.Key, speaker, refusal.Arguments);
			return false;
		}
		if (await lockService.Evaluate(LockType.Page, target, speaker)) return true;
		await didItService.Value.FailLockLocalized(parser, speaker, target, LockType.Page, showDefault ? refusal : null);
		return false;
	}

	/// <summary>Penn na_loc includes the location object, unlike the contents-only delivery used
	/// by movement. Compare omissions by identity even if a lookup created another object wrapper.</summary>
	private async ValueTask<bool> EmitLocationAsync(AnySharpObject executor, AnySharpObject speaker, AnySharpContainer location,
		MString message, INotifyService.NotificationType type, HashSet<DBRef>? omitted = null, DBRef? observe = null)
	{
		var observed = false;
		await Deliver(location.WithExitOption());
		await foreach (var content in location.Content(mediator)) await Deliver(content.WithRoomOption());
		return observed;
		async ValueTask Deliver(AnySharpObject target)
		{
			if (omitted?.Contains(target.Object().DBRef) == true) return;
			// na_zemit records that the location was enumerated before notify's hearing filters.
			observed |= target.Object().DBRef == observe;
			if (!await permissionService.CanInteract(executor, target, IPermissionService.InteractType.Hear, speaker)) return;
			await notifyService.Notify(target, message, speaker, type);
		}
	}

	private async ValueTask<EmitOutcome> EmitOmitAsync(IMUSHCodeParser parser, AnySharpObject executor, AnySharpObject speaker,
		EmitRequest request, INotifyService.NotificationType type)
	{
		if (request.Message.Length == 0 || request.Targets.Count == 0 && request.OmitLocation is null) return new EmitOutcome(false, CallState.Empty);
		var locations = new Dictionary<DBRef, AnySharpContainer>();
		var omitted = new HashSet<DBRef>();
		AnySharpObject looker = executor;
		if (request.OmitLocation is { } locationName)
		{
			if (await locateService.LocateAndNotifyIfInvalid(parser, executor, executor, locationName, LocateFlags.All) is not AnySharpObject target)
				return new EmitOutcome(false, CallState.Empty) { TargetFailure = new CallState(ErrorMessages.Returns.InvalidRoom) };
			if (!target.IsContainer)
			{
				await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.InvalidRoomSpecifiedDetail), executor);
				return new EmitOutcome(false, new CallState(ErrorMessages.Returns.InvalidRoom) { HadErrors = true });
			}
			if (!await MayEmitInAsync(parser, executor, speaker, target.AsContainer)) return new EmitOutcome(false, CallState.Empty);
			looker = target;
			locations[target.Object().DBRef] = target.AsContainer;
		}
		var matched = 0;
		foreach (var name in request.Targets)
		{
			var flags = request.OmitLocation is null ? LocateFlags.All : LocateFlags.MatchObjectsInLookerInventory
				| LocateFlags.AbsoluteMatch | LocateFlags.EnglishStyleMatching | LocateFlags.MatchWildCardForPlayerName;
			if (await locateService.Locate(parser, looker, executor, name, flags) is not AnySharpObject target) continue;
			if (request.OmitLocation is not null)
			{
				if ((await target.Where()).Object().DBRef != looker.Object().DBRef) continue;
				if (matched++ == 10)
				{
					await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.OemitTooManyRecipients), executor);
					break;
				}
				omitted.Add(target.Object().DBRef);
				continue;
			}
			if (target.IsRoom || target.IsExit) continue;
			var room = await target.Where();
			if (!await MayEmitInAsync(parser, executor, speaker, room, reportFailure: false)) continue;
			if (matched++ == 10)
			{
				await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.OemitTooManyRecipients), executor);
				break;
			}
			omitted.Add(target.Object().DBRef);
			locations[room.Object().DBRef] = room;
		}
		if (locations.Count == 0) await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NoMatchingObjects), executor);
		foreach (var room in locations.Values) await EmitLocationAsync(executor, speaker, room, request.Message, type, omitted);
		return new EmitOutcome(locations.Count > 0, CallState.Empty);
	}

	private async ValueTask<EmitOutcome> EmitPrivateAsync(IMUSHCodeParser parser, AnySharpObject executor, AnySharpObject speaker,
		EmitRequest request, INotifyService.NotificationType type)
	{
		if (request.Scope == EmitScope.Private && request.Message.Length == 0) return new EmitOutcome(false, CallState.Empty);
		var admitted = false;
		Option<CallState> targetFailure = new None();
		var notified = new List<AnySharpObject>();
		var portsNotified = 0;
		long lastPort = 0;
		foreach (var name in request.Targets)
		{
			if (string.IsNullOrWhiteSpace(name)) continue;
			if (request.Scope == EmitScope.Private && request.PortTargets)
			{
				if (!await executor.IsPriv())
				{
					await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
					continue;
				}
				if (!long.TryParse(name, out var port))
				{
					await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.InvalidPortNumber), executor, name);
					continue;
				}
				if (port <= 0 || connectionService.Get(port) is null)
				{
					await notifyService.NotifyLocalized(executor, port <= 0 ? nameof(ErrorMessages.Notifications.InvalidPortNumber)
						: nameof(ErrorMessages.Notifications.PortNotActive), executor, name);
					continue;
				}
				if (!await CanHearOnPortAsync(executor, port)) continue;
				admitted = true;
				await notifyService.Notify(port, request.Message, speaker, type);
				portsNotified++;
				lastPort = port;
				continue;
			}
			var located = await locateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, name, LocateFlags.All);
			if (located is not AnySharpObject target)
			{
				if (targetFailure is None && located is Error<CallState> error) targetFailure = error.Value;
				continue;
			}
			if (!await MayPemitAsync(parser, speaker, target, !request.List) || !await permissionService.CanInteract(executor, target, IPermissionService.InteractType.Hear, speaker)) continue;
			admitted = true;
			if (request.Scope == EmitScope.Prompt) await notifyService.Prompt(target, request.Message, speaker, type);
			else await notifyService.Notify(target, request.Message, speaker, type);
			notified.Add(target);
		}
		var outcome = new EmitOutcome(admitted, CallState.Empty) { TargetFailure = targetFailure };
		if (request.Silent) return outcome;
		if (portsNotified == 1)
		{
			var recipientName = connectionService.Get(lastPort)?.Ref is { } reference
				&& await mediator.Send(new GetObjectNodeQuery(reference), ExecutionBudget.CurrentToken) is AnySharpObject recipient
				? recipient.Object().Name : "a connecting player";
			await notifyService.NotifyLocalizedMarkup(executor, nameof(ErrorMessages.Notifications.YouPemitToObjectFormat), executor,
				request.Message, MString.Plain(recipientName));
		}
		else if (portsNotified > 1)
			await notifyService.NotifyLocalizedMarkup(executor, nameof(ErrorMessages.Notifications.YouPemitToConnectionsFormat), executor,
				request.Message, MString.Plain(portsNotified.ToString()));
		if (notified.Count == 0) return outcome;
		if (notified.Count == 1 && notified[0].Object().DBRef == executor.Object().DBRef) return outcome;
		await notifyService.NotifyLocalizedMarkup(executor, notified.Count > 1
			? nameof(ErrorMessages.Notifications.YouPemitToCountFormat) : nameof(ErrorMessages.Notifications.YouPemitToObjectFormat),
			executor, request.Message, MString.Plain(notified.Count > 1 ? notified.Count.ToString() : notified[0].Object().Name));
		return outcome;
	}
}
