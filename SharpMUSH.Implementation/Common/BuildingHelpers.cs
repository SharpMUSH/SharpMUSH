using Microsoft.Extensions.Logging;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Common;

/// <summary>
/// The building work <c>@create</c> and <c>create()</c> share.
/// </summary>
/// <remarks>
/// PennMUSH's <c>fun_create</c> is a one-line call to <c>do_create</c> (<c>src/fundb.c</c>), so
/// every step of the command is a step of the function: the default-home check, the name
/// validation, the object landing in the creator's inventory, the zone inherited from the creator,
/// the report, the <c>OBJECT`CREATE</c> event and the C# object-lifecycle hook. Written out twice,
/// the function had none of the last four and put the new object in the creator's <em>room</em>.
/// </remarks>
public static class BuildingHelpers
{
	/// <inheritdoc cref="BuildingHelpers"/>
	public static async ValueTask<Result<DBRef>> CreateThingAsync(
		IMUSHCodeParser parser,
		IMediator mediator,
		IObjectStore database,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		IValidateService validateService,
		INotifyService notifyService,
		IEventService eventService,
		IPermissionService permissionService,
		AnySharpObject executor,
		MString name,
		MString? requestedDbref = null)
	{
		if (await HomeForNewObjectAsync(mediator, configuration, permissionService, executor) is not AnySharpContainer home)
		{
			await notifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.DefaultHomeLocationInvalid), executor);
			return new Error<string>(ErrorMessages.Returns.NotARoom);
		}

		if (!await validateService.Valid(IValidateService.ValidationType.Name, name, new None()))
		{
			await notifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.InvalidNameThing), executor);
			return new Error<string>(ErrorMessages.Returns.BadObjectName);
		}

		// PennMUSH do_create hands the new object to the executor (src/create.c). An exit cannot
		// hold anything — AnySharpObject.AsContainer throws for one — so code owned by an exit
		// builds into the room the exit is in. @CREATE threw outright in that case; create() had
		// the fallback and lost it when the two were merged.
		var into = executor.IsContainer ? executor.AsContainer : await executor.Where();
		var owner = await executor.Object().Owner.WithCancellation(CancellationToken.None);

		var requested = requestedDbref?.ToPlainText();
		if (string.IsNullOrWhiteSpace(requested))
		{
			return await CreatedAsync(parser, mediator, database, notifyService, eventService, executor, name,
				await mediator.Send(new CreateThingCommand(name.ToPlainText(), into, owner, home)));
		}

		// make_first_free_wrapper (src/destroy.c:930-943), in its own order: the power first, then
		// whether the id can be had at all. Both refuse outright — Penn returns NOTHING from do_create
		// and never falls back to the next free dbref, and neither does this.
		if (!await executor.IsWizard() && !await executor.Object().HasPower("Pick_DBRefs"))
		{
			await notifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new Error<string>(ErrorMessages.Returns.PermissionDenied);
		}

		if (ParseDbref(requested) is not { } wanted)
		{
			await notifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.CreateDbrefUnavailable), executor);
			return new Error<string>(ErrorMessages.Returns.InvalidDbref);
		}

		if (await mediator.Send(new CreateThingAtCommand(wanted, name.ToPlainText(), into, owner, home))
				is not DBRef at)
		{
			await notifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.CreateDbrefUnavailable), executor);
			return new Error<string>(ErrorMessages.Returns.InvalidDbref);
		}

		return await CreatedAsync(parser, mediator, database, notifyService, eventService, executor, name, at);
	}

	/// <summary>
	/// Everything <c>do_create</c> does once the object exists, whichever dbref it landed on: the
	/// creator's zone, the report, the <c>OBJECT`CREATE</c> event and the object-lifecycle hook.
	/// </summary>
	private static async ValueTask<Result<DBRef>> CreatedAsync(
		IMUSHCodeParser parser,
		IMediator mediator,
		IObjectStore database,
		INotifyService notifyService,
		IEventService eventService,
		AnySharpObject executor,
		MString name,
		DBRef thing)
	{
		// A new object inherits its creator's zone, once the cycle guard allows it.
		if (await executor.Object().Zone.WithCancellation(CancellationToken.None) is AnySharpObject zone &&
			await mediator.Send(new GetObjectNodeQuery(thing)) is AnySharpObject created &&
			await HelperFunctions.SafeToAddZone(mediator, database, created, zone))
		{
			await mediator.Send(new SetObjectZoneCommand(created, zone));
		}

		await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.Created), executor, name, thing);

		await eventService.TriggerEventAsync(parser, "OBJECT`CREATE", executor.Object().DBRef,
			thing.ToString(),
			""); // null for cloned-from (not a clone)

		// Phase 2b: C# object-lifecycle hooks fire alongside the softcode OBJECT`CREATE event.
		if (parser.ServiceProvider.GetService<IPluginHookDispatcher>() is { } createHooks)
		{
			await createHooks.ObjectCreatedAsync(thing, executor.Object().DBRef);
		}

		return thing;
	}

	/// <summary>
	/// The whole of PennMUSH's <c>do_clone</c> (<c>src/create.c:679-812</c>) and the
	/// <c>clone_object</c> (<c>:614-668</c>) it calls. <c>fun_clone</c> is one line of <c>do_clone</c>
	/// (<c>src/fundb.c</c>), so <c>@clone</c> and <c>clone()</c> are the same body with two different
	/// ways of naming the target; written out twice, the function was the weaker of the two and the
	/// command was missing what neither had.
	/// </summary>
	/// <remarks>
	/// Two places this deliberately parts company with Penn's implementation, both because the
	/// difference there is an artifact of which C function does the work rather than a contract:
	/// <list type="bullet">
	/// <item><c>OBJECT`CREATE</c> names the cloned-from object for every type. Penn's exit branch
	/// reaches the event through <c>do_real_open</c>, which knows nothing about cloning and passes the
	/// new exit alone (<c>:177</c>), while the thing and room branches pass both (<c>:663</c>).</item>
	/// <item><c>ACLONE</c> is <em>not</em> queued for an exit, which does match Penn: its branch returns
	/// at <c>:806</c> without ever reaching a <c>real_did_it</c>, unlike <c>:727</c> and <c>:742</c>.
	/// Queueing one there would be an invention.</item>
	/// </list>
	/// </remarks>
	public static async ValueTask<Result<DBRef>> CloneAsync(
		IMUSHCodeParser parser,
		IMediator mediator,
		INotifyService notifyService,
		IPermissionService permissionService,
		IAttributeService attributeService,
		IManipulateSharpObjectService manipulateSharpObjectService,
		IDidItService didItService,
		IEventService eventService,
		ILogger? logger,
		AnySharpObject executor,
		AnySharpObject target,
		MString? newName,
		bool preserve)
	{
		if (!await permissionService.Controls(executor, target))
		{
			await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new Error<string>(ErrorMessages.Returns.PermissionDenied);
		}

		if (target.IsPlayer)
		{
			await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CannotClonePlayers), executor);
			return new Error<string>(ErrorMessages.Returns.InvalidObjectType);
		}

		// create.c:711-714. Penn refuses the switch itself rather than quietly downgrading to a plain
		// clone, and refuses it before anything is built. Being allowed to set WIZARD is not the same
		// permission as being allowed to carry one across on someone else's object, which is why this
		// gate is wizardry and not whatever the flag setter would have accepted.
		if (preserve && !await executor.IsWizard())
		{
			await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ClonePreserveWizardOnly), executor);
			return new Error<string>(ErrorMessages.Returns.PermissionDenied);
		}

		var name = newName?.ToPlainText() is { } given && !string.IsNullOrWhiteSpace(given)
			? given
			: target.Object().Name;
		var owner = await executor.Object().Owner.WithCancellation(CancellationToken.None);
		var into = executor.IsContainer ? executor.AsContainer : await executor.Where();

		// "We give the clone the same modification time that its other clone has, but update the
		// creation time" (create.c:653-655). A null creation time is now; the modification time is the
		// original's.
		var modified = target.Object().ModifiedTime;

		DBRef cloneDbRef;
		switch (target)
		{
			case SharpThing thing:
				cloneDbRef = await mediator.Send(new CreateThingCommand(name, into, owner,
					// create.c:657 — Home(clone) = Home(thing), not the configured default home.
					await thing.Home.WithCancellation(CancellationToken.None), ModifiedTime: modified));
				break;
			case SharpRoom:
				// create.c:656 leaves a cloned room with no exits and, since a room's home slot is its
				// exit list, nothing to carry. Its drop-to is Location, cleared at :656 alongside them.
				cloneDbRef = await mediator.Send(new CreateRoomCommand(name, owner, ModifiedTime: modified));
				break;
			case SharpExit exit:
				var nameParts = name.Split(';');
				cloneDbRef = await mediator.Send(new CreateExitCommand(nameParts[0], nameParts[1..], into, owner,
					ModifiedTime: modified));

				// create.c:765-780 hands do_real_open the original's destination, so the clone leads
				// where the original leads. An unlinked original clones to an unlinked exit.
				if (await exit.Home.WithCancellation(CancellationToken.None) is AnySharpContainer destination
					&& await mediator.Send(new GetObjectNodeQuery(cloneDbRef)) is AnySharpObject and SharpExit clonedExit)
				{
					await mediator.Send(new LinkExitCommand(clonedExit, destination));
				}

				break;
			default:
				await notifyService.NotifyLocalized(executor,
					nameof(ErrorMessages.Notifications.CannotCloneThisObjectType), executor);
				return new Error<string>(ErrorMessages.Returns.InvalidObjectType);
		}

		if (await mediator.Send(new GetObjectNodeQuery(cloneDbRef)) is not AnySharpObject clonedObj)
		{
			throw new InvalidOperationException("The clone just created must exist.");
		}

		await CopyAttributesAsync(mediator, attributeService, logger, executor, target, clonedObj, owner);

		foreach (var (lockName, data) in target.Object().Locks)
		{
			if (data.Flags.HasFlag(Library.Services.LockService.LockFlags.NoClone)) continue;
			var copied = await mediator.Send(new CopyLockCommand(target.Object(), clonedObj.Object(), lockName, executor));
			if (copied is Error<string> failure)
			{
				await notifyService.Notify(executor, $"Unable to clone {lockName} lock: {failure.Value}", executor);
			}
		}

		await CopyFlagsAsync(manipulateSharpObjectService, executor, target, clonedObj, preserve);
		await CopyPrivilegesAsync(mediator, manipulateSharpObjectService, notifyService, executor, target, clonedObj,
			preserve);

		// create.c:636-637 — the clone inherits the original's zone and parent, and not the cloner's.
		if (await target.Object().Zone.WithCancellation(CancellationToken.None) is AnySharpObject zone)
		{
			await mediator.Send(new SetObjectZoneCommand(clonedObj, zone));
		}

		if (await target.Object().Parent.WithCancellation(CancellationToken.None) is AnySharpObject parent)
		{
			await mediator.Send(new SetObjectParentCommand(clonedObj, parent));
		}

		await eventService.TriggerEventAsync(parser, "OBJECT`CREATE", executor.Object().DBRef,
			cloneDbRef.ToString(),
			target.Object().DBRef.ToString()); // cloned-from

		if (parser.ServiceProvider.GetService<IPluginHookDispatcher>() is { } createHooks)
		{
			await createHooks.ObjectCreatedAsync(cloneDbRef, executor.Object().DBRef);
		}

		await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ClonedNewObjectFormat),
			executor, cloneDbRef.Number);

		// real_did_it(player, clone, NULL, NULL, NULL, NULL, "ACLONE", …) — create.c:727 and :742. The
		// attribute has just been copied onto the clone, and it runs there with the cloner as enactor.
		// See the remarks above for why an exit gets none.
		if (!target.IsExit)
		{
			await didItService.DidIt(parser, new DidItRequest(executor, clonedObj, AWhat: "ACLONE"));
		}

		return cloneDbRef;
	}

	/// <summary>
	/// Penn's <c>atr_cpy</c> (<c>attrib.c:1692-1710</c>) walks the source's flat, sorted attribute
	/// list — branch vs. leaf is purely a naming convention over one namespace — and for each attribute
	/// checks AF_Nocopy, then calls <c>atr_new_add(..., makeroots: false)</c>. With makeroots false,
	/// <c>atr_new_add</c> (<c>attrib.c:756-820</c>) silently aborts without adding when the immediate
	/// parent isn't already on the destination (<c>:804-806</c>). Because the list is sorted with parent
	/// before child, a no_clone BRANCH is itself skipped by atr_cpy, and its leaves then find no parent
	/// on the clone either and are dropped too — incidentally, via the missing-root abort, not via any
	/// permission walk of their own. <c>GetAttributesByRegexAsync</c> (via <c>GetAttributesQuery</c> in
	/// Regex mode) is used here rather than a depth-1 enumeration (or the unsorted
	/// <c>GetAttributesAsync</c>) because it walks the whole tree and sorts LongName ascending — parent
	/// before child — which this skip-propagation depends on.
	/// </summary>
	private static async ValueTask CopyAttributesAsync(
		IMediator mediator,
		IAttributeService attributeService,
		ILogger? logger,
		AnySharpObject executor,
		AnySharpObject target,
		AnySharpObject clonedObj,
		SharpPlayer owner)
	{
		var skippedAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		await foreach (var sourceAttribute in mediator.CreateStream(
			new GetAttributesQuery(target.Object().DBRef, ".*", false, IAttributeService.AttributePatternMode.Regex)))
		{
			var attr = sourceAttribute.Attribute;
			var longName = attr.LongName!;
			var attrPath = longName.Split('`');
			var lastSeparator = longName.LastIndexOf('`');
			var parentLongName = lastSeparator < 0 ? null : longName[..lastSeparator];
			var parentSkipped = parentLongName is not null && skippedAttributes.Contains(parentLongName);

			// The "_"-prefix skip is a pre-existing SharpMUSH-only filter, orthogonal to Penn's
			// no_clone. It folds into the same skip set so that a "_"-prefixed branch's children don't
			// get silently auto-vivified a stripped-down parent — the missing-root hazard the no_clone
			// propagation above exists to avoid.
			if (attr.IsNoCopy() || attr.Name.StartsWith('_') || parentSkipped)
			{
				skippedAttributes.Add(longName);
				continue;
			}

			// AL_CREATOR(ptr) is passed through unchanged in atr_cpy (attrib.c:1706) — a cloned
			// attribute keeps its original creator, not the cloner.
			var creator = await attr.Owner.WithCancellation(CancellationToken.None) ?? owner;
			var setResult = await attributeService.SetAttributeAsync(executor, clonedObj, longName, attr.Value, creator);

			// A failed set means the branch was NOT actually copied. Treating it as skipped keeps the
			// invariant this whole loop depends on: a LongName only avoids the skip set if it genuinely
			// landed on the clone. Without this, a child under a branch that failed to set would still
			// see its parent as "not skipped" and auto-vivify a stripped-down stand-in — the exact
			// hazard this propagation exists to prevent. Unreachable today (the clone's owner always
			// controls the freshly-created destination), but one permission change away from live.
			if (setResult is Error<string>)
			{
				skippedAttributes.Add(longName);
				continue;
			}

			// AL_FLAGS(ptr) is assigned directly alongside AL_CREATOR on the very same atr_new_add call
			// (attrib.c:1706-1707) — Penn copies the flags too, with no permission gate at all:
			// atr_new_add is a deliberately "dangerous", bypass-everything helper reserved for database
			// load and atr_cpy (its own doc comment, attrib.c:750-754). SetAttributeAsync only just
			// created the destination attribute with whatever SharpAttributeEntry.DefaultFlags applies
			// (AttributeService.cs, applied inside SetAttributeCommand's handler) — a SharpMUSH-only
			// mechanism Penn has no equivalent of — so the destination flag set is forced to match the
			// source's exactly, mirroring Penn's unconditional overwrite rather than a union. Goes
			// straight through SetAttributeFlagCommand/UnsetAttributeFlagCommand (no permission checks
			// in either handler) rather than AttributeService.SetAttributeFlagsAsync, for the same
			// bypass reason atr_new_add itself bypasses can_write_attr.
			var destAttribute = await mediator.CreateStream(new GetAttributeQuery(clonedObj.Object().DBRef, attrPath))
				.LastOrDefaultAsync();

			if (destAttribute is null)
			{
				// SetAttributeAsync above reported success, so the destination attribute should exist —
				// this re-fetch failing is not the "copy failed" case handled above (that one still owns
				// skippedAttributes so children don't auto-vivify a stripped parent). Here the value
				// genuinely landed; only the flag sync had nothing to attach to. Surface it instead of
				// silently leaving the clone's flags at SetAttributeAsync's defaults.
				logger?.LogWarning(
					"Clone flag sync skipped for {LongName} on {CloneDbRef}: destination attribute was not found immediately after a successful set",
					longName, clonedObj.Object().DBRef);
				continue;
			}

			var sourceFlagNames = attr.Flags.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
			var destFlagNames = destAttribute.Flags.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

			foreach (var flag in destAttribute.Flags.Where(f => !sourceFlagNames.Contains(f.Name)))
			{
				await mediator.Send(new UnsetAttributeFlagCommand(clonedObj.Object().DBRef, destAttribute, flag));
			}

			foreach (var flag in attr.Flags.Where(f => !destFlagNames.Contains(f.Name)))
			{
				await mediator.Send(new SetAttributeFlagCommand(clonedObj.Object().DBRef, destAttribute, flag));
			}
		}
	}

	/// <summary>
	/// <c>Flags(clone) = clone_flag_bitmask("FLAG", Flags(thing))</c> (create.c:638), with WIZARD and
	/// ROYALTY cleared again unless preserving (<c>:640-644</c>).
	/// </summary>
	/// <remarks>
	/// Synchronised to the source, not unioned with it. The clone is created through the same path as
	/// any other object and therefore arrives carrying the configured creation defaults, so copying only
	/// what the source has would leave a NO_COMMAND that the source had deliberately cleared — and the
	/// $-commands just copied onto the clone would not run. The attribute-flag sync works the same way,
	/// for the same reason.
	/// </remarks>
	private static async ValueTask CopyFlagsAsync(
		IManipulateSharpObjectService manipulateSharpObjectService,
		AnySharpObject executor,
		AnySharpObject target,
		AnySharpObject clonedObj,
		bool preserve)
	{
		var copyable = await target.Object().Flags.Value
			.Where(flag => preserve || (!flag.Name.Contains("WIZARD") && !flag.Name.Contains("ROYALTY")))
			.Select(flag => flag.Name)
			.ToHashSetAsync(StringComparer.OrdinalIgnoreCase);

		// Materialized: the clone's flags are unset while this list is walked.
		var clonedObjectFlags = await clonedObj.Object().Flags.Value.ToArrayAsync();
		foreach (var flag in clonedObjectFlags.Where(flag => !copyable.Contains(flag.Name)))
		{
			await manipulateSharpObjectService.SetOrUnsetFlag(executor, clonedObj, $"!{flag.Name}", false);
		}

		foreach (var flagName in copyable)
		{
			await manipulateSharpObjectService.SetOrUnsetFlag(executor, clonedObj, flagName, false);
		}
	}

	/// <summary>
	/// The half of <c>clone_object</c> that <c>/PRESERVE</c> exists for (create.c:643-652): without it
	/// the clone's powers and warnings are zapped, which is what a freshly created object already has;
	/// with it they come across and the cloner is warned that they did. Only a wizard reaches this — the
	/// gate is in <see cref="CloneAsync"/> — so the copy goes through the ordinary setter rather than
	/// around it.
	/// </summary>
	private static async ValueTask CopyPrivilegesAsync(
		IMediator mediator,
		IManipulateSharpObjectService manipulateSharpObjectService,
		INotifyService notifyService,
		AnySharpObject executor,
		AnySharpObject target,
		AnySharpObject clonedObj,
		bool preserve)
	{
		if (!preserve)
		{
			return;
		}

		await foreach (var power in target.Object().Powers.Value)
		{
			await manipulateSharpObjectService.SetPower(executor, clonedObj, power.Name, false);
		}

		if (target.Object().Warnings != WarningType.None)
		{
			await mediator.Send(new SetObjectWarningsCommand(clonedObj, target.Object().Warnings));
		}

		// create.c:652-656 — the notice fires on what the clone ended up with, not on what was asked for.
		if (await clonedObj.HasFlag("WIZARD") || await clonedObj.HasFlag("ROYALTY")
			|| clonedObj.Object().Warnings != WarningType.None
			|| await clonedObj.Object().Powers.Value.AnyAsync())
		{
			await notifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.ClonePreserveCarriedPrivileges), executor);
		}
	}

	/// <summary>
	/// PennMUSH <c>parse_dbref</c> (<c>src/parse.c:120-138</c>) — strictly <c>#nnn</c>, because
	/// anything looser would swallow a possessive. The <c>GoodObject</c> half of its check is the
	/// provider's to answer, and <c>CreateThingAtCommand</c> answers it.
	/// </summary>
	private static DBRef? ParseDbref(string requested)
	{
		var trimmed = requested.Trim();
		return trimmed.Length > 1 && trimmed[0] == '#' && int.TryParse(trimmed[1..], out var number) && number >= 0
			? new DBRef(number)
			: null;
	}

	/// <summary>
	/// PennMUSH <c>do_create</c> (<c>src/create.c:589-597</c>) derives the new object's home from the
	/// creator and never from a configured constant:
	/// <code>
	/// if ((loc = Location(player)) != NOTHING &amp;&amp; (controls(player, loc) || Abode(loc)))
	///   Home(thing) = loc;
	/// else
	///   Home(thing) = Home(player);
	/// </code>
	/// <c>Database.DefaultHome</c> survives only as the last resort, for a creator whose own home is
	/// unset — a room without a drop-to, or an exit that has never been linked. Penn cannot reach that
	/// branch, because every one of its objects always carries a <c>home</c>.
	/// </summary>
	private static async ValueTask<AnyOptionalSharpContainer> HomeForNewObjectAsync(
		IMediator mediator,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		IPermissionService permissionService,
		AnySharpObject executor)
	{
		var where = await executor.Where();
		if (await permissionService.Controls(executor, where.WithExitOption()) || await where.Object().HasFlag("ABODE"))
		{
			return new AnyOptionalSharpContainer(where);
		}

		if (await CreatorHomeAsync(executor) is AnySharpContainer own)
		{
			return new AnyOptionalSharpContainer(own);
		}

		var configured = new DBRef((int)configuration.CurrentValue.Database.DefaultHome);
		return await mediator.Send(new GetObjectNodeQuery(configured)) is AnySharpObject { IsContainer: true } fallback
			? new AnyOptionalSharpContainer(fallback.AsContainer)
			: new AnyOptionalSharpContainer(new None());
	}

	/// <summary>
	/// PennMUSH's <c>home</c> field is one slot read differently per type, and <c>Home(player)</c> at
	/// create.c:595 reads whichever applies to the creator: a player's or thing's home, an exit's
	/// destination (<c>src/db.h</c> aliases <c>Destination</c> to it), or a room's drop-to.
	/// </summary>
	private static async ValueTask<AnyOptionalSharpContainer> CreatorHomeAsync(AnySharpObject executor) => executor switch
	{
		SharpPlayer player => new AnyOptionalSharpContainer(
			await player.Home.WithCancellation(CancellationToken.None)),
		SharpThing thing => new AnyOptionalSharpContainer(
			await thing.Home.WithCancellation(CancellationToken.None)),
		SharpExit exit => await exit.Home.WithCancellation(CancellationToken.None),
		SharpRoom room => await room.Location.WithCancellation(CancellationToken.None)
	};
}
