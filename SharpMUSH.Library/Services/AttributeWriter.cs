using DotNext.Threading;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Softcode;

namespace SharpMUSH.Library.Services;

/// <summary>
/// PennMUSH's <c>do_set_atr</c> and <c>do_wipe</c>: everything that writes an attribute's value or
/// removes it, and the <c>can_write_attr_internal</c> gate both run first.
/// </summary>
/// <remarks>
/// Reading an attribute and writing one are different gates over different data — Penn keeps
/// <c>can_read_attr_internal</c> and <c>can_write_attr_internal</c> apart for exactly that reason —
/// so the write half lives here and <see cref="AttributeService"/> keeps the read half.
/// </remarks>
internal sealed class AttributeWriter(
	IMediator mediator,
	IPermissionService permissionService,
	INotifyService notifyService,
	IValidateService validateService,
	IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions> configuration,
	IServiceProvider serviceProvider)
{
	public async ValueTask<Result<Success>> SetAttributeAsync(AnySharpObject executor,
		AnySharpObject obj,
		string attribute,
		MString value)
		=> await SetAttributeAsync(executor, obj, attribute, value,
			await executor.Object().Owner.WithCancellation(CancellationToken.None), isAttributeCopy: false);

	/// <summary>
	/// As the four-argument overload, but stamps <paramref name="creator"/> as the attribute's
	/// owner instead of deriving it from <paramref name="executor"/>. Mirrors PennMUSH's
	/// <c>atr_cpy</c> (<c>src/attrib.c:1706</c>), which passes <c>AL_CREATOR(ptr)</c> through
	/// unchanged - a cloned attribute keeps its original creator, not the cloner. <c>@CLONE</c>
	/// is the only caller today.
	/// </summary>
	public async ValueTask<Result<Success>> SetAttributeAsync(AnySharpObject executor,
		AnySharpObject obj,
		string attribute,
		MString value,
		SharpPlayer creator)
		=> await SetAttributeAsync(executor, obj, attribute, value, creator, isAttributeCopy: true);

	/// <param name="isAttributeCopy">
	/// Whether this write is PennMUSH's <c>atr_cpy</c> (<c>src/attrib.c:1706</c>) rather than an
	/// ordinary set. <c>atr_cpy</c> reaches the database through <c>atr_new_add</c>, the deliberately
	/// "dangerous" helper that bypasses both <c>can_create_attr</c>'s default-flag gate and
	/// <c>do_set_atr</c>'s forward-list validation, so a clone carries the source's attributes across
	/// whether or not the cloner could have written them itself. <c>@CLONE</c> is the only caller.
	/// The <c>Controls</c>, stored-flag and <c>check_attr_value</c> gates still run: only the two
	/// checks Penn reaches exclusively through <c>do_set_atr</c>/<c>atr_add</c> are skipped.
	/// </param>
	private async ValueTask<Result<Success>> SetAttributeAsync(AnySharpObject executor,
		AnySharpObject obj,
		string attribute,
		MString value,
		SharpPlayer creator,
		bool isAttributeCopy)
	{
		if (isAttributeCopy || !PlayerAliases.Applies(obj, attribute))
		{
			return await WriteAttributeAsync(executor, obj, attribute, value, creator, isAttributeCopy, isPlayerAlias: false);
		}

		var result = await WriteAttributeAsync(executor, obj, attribute, value, creator, isAttributeCopy, isPlayerAlias: true);
		await ReportPlayerAliasAsync(executor, result, value.ToPlainText().Length > 0
			? nameof(ErrorMessages.Notifications.PlayerAliasSet)
			: nameof(ErrorMessages.Notifications.PlayerAliasRemoved));
		return result;
	}

	/// <summary>
	/// A write of a player's alias list is reported here, whatever became of it, because PennMUSH's ALIAS
	/// branch of <c>do_set_atr</c> notifies the setter itself (<c>src/attrib.c:2268-2316, 2418-2423</c>):
	/// <c>Alias set.</c> or <c>Alias removed.</c> where any other attribute gets the caller's
	/// <c>Set.</c>/<c>Cleared.</c> line. Callers print nothing of their own for one
	/// (<see cref="PlayerAliases.Applies"/>). The provider has already rebuilt the player's lookup names
	/// from the new value, in the same write (<c>reset_player_list</c>).
	/// </summary>
	private async ValueTask ReportPlayerAliasAsync(AnySharpObject executor, Result<Success> result, string successKey)
	{
		switch (result)
		{
			case Success:
				await notifyService.NotifyLocalized(executor, successKey, executor);
				break;
			case Error<string> error:
				await notifyService.Notify(executor, error.Value, executor);
				break;
		}
	}

	private async ValueTask<Result<Success>> WriteAttributeAsync(AnySharpObject executor,
		AnySharpObject obj,
		string attribute,
		MString value,
		SharpPlayer creator,
		bool isAttributeCopy,
		bool isPlayerAlias)
	{
		if (!await permissionService.Controls(executor, obj))
		{
			return new Error<string>(ErrorMessages.Returns.AttrSetPermissions);
		}

		// A player's alias list is validated right after controls(), where do_set_atr validates it
		// (src/attrib.c:2268-2316).
		if (isPlayerAlias
				&& await PlayerAliasRestriction.CheckAsync(mediator, validateService,
					configuration.CurrentValue.Limit.MaxAliases, executor, obj, value.ToPlainText()) is Error<string> refusedAlias)
		{
			return refusedAlias;
		}

		var attrPath = attribute.Split('`');

		// Materialized (not left as a stream) because it is used twice: once below for the
		// permission check, and again after the set for the target attribute's syntax flags. An
		// *existing* attribute's flags never change on a value set, so this pre-set snapshot is
		// exactly what the post-set flag check needs too -- reusing it avoids a second
		// GetAttributeQuery round trip on every overwrite of an already-existing attribute (the
		// overwhelmingly common case). It is NOT reusable for a brand-new attribute: GetAttributeQuery
		// is all-or-nothing, so `existing` comes back empty here, but SetAttributeCommand applies
		// SharpAttributeEntry.DefaultFlags (admin-configurable via @attribute, including cmdsyntax/
		// funsyntax) to the newly-created node during that same call -- see the post-set re-fetch below.
		var existing = await mediator.CreateStream(new GetAttributeQuery(obj.Object().DBRef, attrPath))
			.ToListAsync();

		var write = new AttributeWrite(executor, obj, attrPath, existing, creator, isAttributeCopy);

		return await PlanWriteAsync(write) switch
		{
			WritePlan plan => await WritePlannedAsync(write, plan, value),
			Error<string> refused => refused
		};
	}

	/// <summary>One attribute write: who writes which path on what, and what of that path already exists.</summary>
	private sealed record AttributeWrite(
		AnySharpObject Executor,
		AnySharpObject Obj,
		string[] AttrPath,
		List<SharpAttribute> Existing,
		SharpPlayer Creator,
		bool IsAttributeCopy);

	/// <summary>
	/// What the write gates learned: the level the path stops existing at (every level from there down
	/// is created by this write), and the standard table's entry for the leaf when the leaf is one of them.
	/// </summary>
	private readonly record struct WritePlan(int CreatedFrom, SharpAttributeEntry? LeafEntry);

	/// <summary>
	/// The write gates: the executor may set every level of the path that exists, and may create every
	/// level that does not.
	/// </summary>
	private async ValueTask<Result<WritePlan>> PlanWriteAsync(AttributeWrite write)
	{
		// Check both attribute permissions AND object permissions
		// Attribute permissions: executor must be able to set each attribute in the path
		// Object permissions: executor must control the object
		if (!await CanSetAllAsync(write, write.Existing))
		{
			return new Error<string>(ErrorMessages.Returns.AttrSetPermissions);
		}

		// Where the path stops existing: every level from here down is one this call has to create,
		// and so is gated below against the standard table's flags rather than a stored node's. When
		// `existing` resolved, it IS the whole path and every prefix was already checked above.
		Result<int> createdFrom = write.Existing.Count > 0
			? write.AttrPath.Length
			: await ExistingPrefixLengthAsync(write);

		return createdFrom switch
		{
			int from => await GateCreatedLevelsAsync(write, from),
			Error<string> refused => refused
		};
	}

	/// <summary>
	/// If the target attribute doesn't exist yet (creating new), we still need to check
	/// permissions on the existing ancestor path. The stream in <see cref="WriteAttributeAsync"/> yields
	/// nothing when the full path doesn't exist (count != attribute.Length check in GetAttributeAsync).
	/// Check each existing prefix of the path, longest first, stopping at the first one that
	/// resolves: its own path covers every shorter prefix.
	/// </summary>
	/// <returns>How many levels of the path exist, or the permission error.</returns>
	private async ValueTask<Result<int>> ExistingPrefixLengthAsync(AttributeWrite write)
	{
		for (var i = write.AttrPath.Length - 1; i >= 1; i--)
		{
			var prefix = await mediator.CreateStream(new GetAttributeQuery(write.Obj.Object().DBRef, write.AttrPath[..i]))
				.ToListAsync();

			if (!await CanSetAllAsync(write, prefix))
			{
				return new Error<string>(ErrorMessages.Returns.AttrSetPermissions);
			}

			if (prefix.Count > 0)
			{
				return i;
			}
		}

		return 0;
	}

	private async ValueTask<bool> CanSetAllAsync(AttributeWrite write, List<SharpAttribute> attributes)
	{
		foreach (var x in attributes)
		{
			if (!await permissionService.CanSet(write.Executor, write.Obj, x))
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// PennMUSH's can_create_attr (src/attrib.c:446-486) gates every level of the path that does
	/// not exist yet, one at a time, against the flags the standard attribute table gives that
	/// level - set_default_flags (src/attrib.c:424-432) ORs them onto a synthetic ATTR and
	/// Cannot_Write_This_Attr is asked about THAT, before atr_add writes anything. The provider
	/// here applies SharpAttributeEntry.DefaultFlags to each level it creates
	/// (LightningDatabase.Attributes.cs:330-342), so without this the flags that should have
	/// refused the write only come into existence one line AFTER it happened: a mortal could
	/// create their own MAILQUOTA (lifting their mailbox limit) or AMAIL (code the game runs on
	/// their behalf), neither of which they could have overwritten once it existed. #1217.
	/// </summary>
	private async ValueTask<Result<WritePlan>> GateCreatedLevelsAsync(AttributeWrite write, int createdFrom)
	{
		var attrPath = write.AttrPath;
		SharpAttributeEntry? leafEntry = null;

		for (var level = createdFrom; level < attrPath.Length; level++)
		{
			var levelName = string.Join('`', attrPath[..(level + 1)]).ToUpperInvariant();

			if (await mediator.Send(new GetAttributeEntryQuery(levelName)) is not { } levelEntry)
			{
				continue;
			}

			if (level == attrPath.Length - 1)
			{
				leafEntry = levelEntry;
			}

			if (!write.IsAttributeCopy && !await permissionService.CanSet(write.Executor, write.Obj,
						CreatedAttributeFor(levelEntry, attrPath[level], levelName, write.Creator)))
			{
				return new Error<string>(ErrorMessages.Returns.AttrSetPermissions);
			}
		}

		return new WritePlan(createdFrom, leafEntry);
	}

	/// <summary>The value checks that follow the permission gates, then the write itself.</summary>
	private async ValueTask<Result<Success>> WritePlannedAsync(AttributeWrite write, WritePlan plan, MString value)
	{
		// The forward lists are validated here, four lines ahead of check_attr_value, exactly where
		// Penn's do_set_atr puts them (src/attrib.c:2326-2358): an entry that is not an objid, does not
		// name a live object, or names one unwilling to hear from THIS object refuses the whole set.
		// Delivery re-checks per entry anyway (MailDelivery.MayForwardTo), so without this the player
		// only learns their list is wrong when some sender is told of "a mail forwarding problem". #1218.
		// Not on the @CLONE path: Can_Forward's subject is the object being written, so a list the
		// SOURCE was allowed to hold can be one the clone could not have created - a forward lock
		// naming the source passes for the source and not for the copy - and refusing it there would
		// silently drop the attribute from the clone. Penn never validates a copy at all.
		var fullName = string.Join('`', write.AttrPath).ToUpperInvariant();

		if (!write.IsAttributeCopy && ForwardListRestriction.Applies(fullName)
				&& await ForwardListRestriction.CheckAsync(mediator, permissionService, write.Obj, fullName,
					value.ToPlainText()) is Error<string> badList)
		{
			return badList;
		}

		return await RestrictedValueAsync(write, plan, fullName, value) switch
		{
			MString stored => await StoreAsync(write, stored),
			Error<string> refused => refused
		};
	}

	/// <summary>
	/// check_attr_value runs here in Penn's do_set_atr (src/attrib.c:2363): @attribute/limit and
	/// @attribute/enum refuse the set outright, and an enum stores the choice as the enum spells it.
	/// The leaf's entry was already fetched by <see cref="GateCreatedLevelsAsync"/> whenever the leaf
	/// itself is being created, which is the only case the pre-set `existing` snapshot cannot answer for.
	/// </summary>
	private async ValueTask<Result<MString>> RestrictedValueAsync(AttributeWrite write, WritePlan plan, string fullName,
		MString value)
	{
		if ((plan.CreatedFrom < write.AttrPath.Length
					? plan.LeafEntry
					: await mediator.Send(new GetAttributeEntryQuery(fullName)))
				is not { } entry)
		{
			return value;
		}

		var plain = value.ToPlainText();
		return AttributeValueRestriction.Check(entry, plain) switch
		{
			Error<string> refused => refused,
			string stored when !stored.Equals(plain, StringComparison.Ordinal) => MarkupText.Plain(stored),
			_ => value
		};
	}

	private async ValueTask<Result<Success>> StoreAsync(AttributeWrite write, MString value)
	{
		await mediator.Send(new SetAttributeCommand(write.Obj.Object().DBRef, write.AttrPath, value, write.Creator));
		await WarnSyntaxErrorsAsync(write, value);
		return new Success();
	}

	/// <summary>
	/// Advisory-only set-time validation: PennMUSH never validates softcode at set time, and
	/// parity governs here, so a syntax error must never block the set -- only warn the setter,
	/// after the value is already stored. `existing` (fetched pre-set, above) is reused when the
	/// attribute already existed -- its flags cannot have changed underneath this call. But when
	/// `existing` is empty, this was a first-ever write: DefaultFlags was just applied to the
	/// brand-new node by the SetAttributeCommand handler, so only a fresh fetch can see it. Paying
	/// one extra query here is a one-time cost per attribute, not a per-set cost.
	/// </summary>
	private async ValueTask WarnSyntaxErrorsAsync(AttributeWrite write, MString value)
	{
		var storedAttribute = write.Existing.Count != 0
			? write.Existing.LastOrDefault()
			: await mediator.CreateStream(new GetAttributeQuery(write.Obj.Object().DBRef, write.AttrPath)).LastOrDefaultAsync();

		if (storedAttribute?.SyntaxParseType() is not { } parseType)
		{
			return;
		}

		// IMUSHCodeParser is resolved lazily via the container rather than taken as a constructor
		// parameter: MUSHCodeParser's own constructor eagerly resolves IAttributeService through
		// this same IServiceProvider, so an eager IMUSHCodeParser dependency here would be a
		// circular singleton resolution. Deferring the lookup to call time (long after both
		// singletons are fully constructed) breaks the cycle.
		var mushParser = serviceProvider.GetRequiredService<IMUSHCodeParser>();
		// Only the code half: a $-command's or listen's pattern is compiled to a match regex, never
		// parsed, so validating it would warn about an attribute that works (SoftcodeSource.Validate).
		var errors = SoftcodeSource.Validate(mushParser, value, parseType);

		foreach (var error in errors)
		{
			await notifyService.Notify(write.Executor, error.ToMushFailureString(), write.Obj);
		}
	}

	/// <summary>
	/// The attribute one level of a path WOULD be once created, for the benefit of the one permission
	/// ladder that already implements PennMUSH's <c>Cannot_Write_This_Attr</c>
	/// (<see cref="IPermissionService.CanSet"/>) - exactly the synthetic <c>ATTR</c>
	/// <c>can_create_attr</c> builds and runs <c>set_default_flags</c> over before testing it
	/// (<c>src/attrib.c:446-458</c>). Reusing that ladder is the point: a second copy of the
	/// God/internal/safe/nodump/wizard/locked ordering living here would be one to drift.
	/// </summary>
	/// <remarks>
	/// <c>AL_CREATOR</c> is <paramref name="creator"/> because Penn stamps it before the test
	/// (<c>src/attrib.c:453</c>), so <c>locked</c> on its own never refuses a creation to the very
	/// player being recorded as the creator - only <c>wizard</c>, <c>safe</c>, <c>internal</c> and
	/// <c>nodump</c> do.
	/// </remarks>
	private static SharpAttribute CreatedAttributeFor(SharpAttributeEntry entry, string name, string longName,
		SharpPlayer creator)
		=> new(
			Id: string.Empty,
			Key: string.Empty,
			Name: name,
			Flags: [.. entry.DefaultFlags.Select(flag => new SharpAttributeFlag
			{
				Name = flag,
				Symbol = string.Empty,
				System = true,
				Inheritable = false
			})],
			CommandListIndex: null,
			LongName: longName,
			Leaves: new AsyncLazy<IAsyncEnumerable<SharpAttribute>>(
				_ => Task.FromResult(AsyncEnumerable.Empty<SharpAttribute>())),
			Owner: new AsyncLazy<SharpPlayer?>(_ => Task.FromResult<SharpPlayer?>(creator)),
			SharpAttributeEntry: new AsyncLazy<SharpAttributeEntry?>(_ => Task.FromResult<SharpAttributeEntry?>(entry)));

	/// <summary>
	/// Clears attributes matching <paramref name="attributePattern"/>. In
	/// <see cref="IAttributeService.AttributePatternMode.Wildcard"/> mode this is <c>@wipe</c>
	/// (whole subtrees, per-match reporting, a final tally); in
	/// <see cref="IAttributeService.AttributePatternMode.Exact"/> mode it is <c>@set obj/attr=</c>
	/// (one node, a single aggregated Success/Error).
	/// </summary>
	/// <param name="executor">The object performing the clear.</param>
	/// <param name="obj">The object whose attributes are being cleared.</param>
	/// <param name="attributePattern">Attribute name or pattern to match.</param>
	/// <param name="patternMode">Selects the @wipe or the @set semantics described above.</param>
	public async ValueTask<Result<Success>> ClearAttributeAsync(AnySharpObject executor,
		AnySharpObject obj,
		string attributePattern,
		IAttributeService.AttributePatternMode patternMode)
	{
		// do_set_atr(thing, "ALIAS", NULL, ...) reports a player's alias list itself. @wipe goes through
		// wipe_atr instead, which says nothing of it; the provider still drops the lookup names.
		if (patternMode == IAttributeService.AttributePatternMode.Wildcard || !PlayerAliases.Applies(obj, attributePattern))
		{
			return await ClearMatchingAsync(executor, obj, attributePattern, patternMode);
		}

		var result = await ClearMatchingAsync(executor, obj, attributePattern, patternMode);
		await ReportPlayerAliasAsync(executor, result, nameof(ErrorMessages.Notifications.PlayerAliasRemoved));
		return result;
	}

	private async ValueTask<Result<Success>> ClearMatchingAsync(AnySharpObject executor,
		AnySharpObject obj,
		string attributePattern,
		IAttributeService.AttributePatternMode patternMode)
	{
		if (!await permissionService.Controls(executor, obj))
		{
			return new Error<string>(ErrorMessages.Returns.AttrSetPermissions);
		}

		var attr = mediator.CreateStream(new GetAttributesQuery(obj.Object().DBRef, attributePattern, false, patternMode));

		// checkParents is false above, so every match is sourced from obj itself - the whole
		// write path only ever touches the object's own attributes (Penn's atr_iter_get).
		var attrArr = await attr.Select(x => x.Attribute).ToArrayAsync();
		var isWipe = patternMode == IAttributeService.AttributePatternMode.Wildcard;

		// PennMUSH's wipe_helper (src/set.c:1503-1504):
		//   if (wildcard(pattern) && AF_Wizard(atr) && !God(player)) return 0;
		// "for added security, only God can modify wiz-only-modifiable attributes using this
		// command and wildcards. Wiping a specific attr still works, though." The guard is keyed
		// on whether the PATTERN TEXT actually contains a wildcard (wildcard(s) is
		// `wildcard_count(s, 0) == -1`, hdrs/externs.h:529 - an unescaped '*' or '?'), not on
		// which pattern mode the caller asked for: @wipe always requests Wildcard mode even for a
		// literal name, and that literal case must stay allowed.
		var patternIsWildcard = isWipe && AttributeService.HasUnescapedWildcard(attributePattern);
		var executorIsGod = executor.IsGod();

		if (attrArr.Length == 0)
		{
			return await NothingToClearAsync(executor, obj, attributePattern, isWipe);
		}

		var dbref = obj.Object().DBRef;

		// Gate on each match's FULL ancestor path, not the matched attribute alone: a pattern
		// like "**" (@wipe) can match a leaf several levels under a wizard/safe/locked branch
		// without that branch node itself appearing in attrArr, so checking the leaf in
		// isolation would miss the ancestor's flag entirely (Task 6). Siblings that the
		// pattern also matched are reused as free ancestor data (Task 6 fix round 1, L1)
		// before falling back to a query.
		var matchKnown = AttributeService.IndexByLongName(attrArr, static x => x.LongName!);

		// PennMUSH's wipe_helper (src/set.c:1493-1523) is invoked once per matched attribute
		// via atr_iter_get and notifies each denial/tree-block AS IT'S DISCOVERED, then keeps
		// going - a denied or partially-blocked match never stops the others from being
		// processed, and neither failure class ever displaces the other's report (Task 6 fix
		// round 3). do_wipe (src/set.c:1568-1577) then ALWAYS prints a final tally - "No"/
		// "One"/"N attributes wiped." - regardless of whether anything was blocked, so the
		// player learns both what was refused and what actually happened. Exact-mode
		// (@set obj/attr=, used by callers other than @WIPE) keeps the original single
		// aggregated Success/Error contract those callers already depend on.
		var anyDenied = false;
		var wipedCount = 0;

		foreach (var attrItem in attrArr.Where(attrItem => !patternIsWildcard || !ProtectedFromWildcardWipe(attrItem, executorIsGod)))
		{
			var outcome = await ClearMatchAsync(executor, obj, dbref, attrItem, matchKnown, isWipe);
			anyDenied |= outcome.Denied;
			wipedCount += outcome.Wiped;
		}

		if (!isWipe)
		{
			return anyDenied
				? new Error<string>(ErrorMessages.Returns.AttrSetPermissions)
				: new Success();
		}

		// The unconditional final tally (do_wipe, set.c:1568-1577) - every one of the three
		// states ("wiped everything", "wiped some", "wiped nothing") lands here, since
		// wipedCount already reflects exactly how many attribute nodes were actually removed
		// regardless of how many matches were denied or tree-blocked above.
		await notifyService.NotifyLocalized(executor, wipedCount switch
		{
			0 => nameof(ErrorMessages.Notifications.NoAttributesWiped),
			1 => nameof(ErrorMessages.Notifications.OneAttributeWiped),
			_ => nameof(ErrorMessages.Notifications.AttributesWipedCount)
		}, executor, wipedCount);

		return new Success();
	}

	/// <summary>
	/// If no matching attributes exist, there is nothing to clear. Exact mode
	/// (@set obj/attr=, every caller other than @WIPE) succeeds silently, as before -
	/// PennMUSH does not error when clearing a non-existent attribute. But @wipe's own
	/// do_wipe (set.c:1567-1577) ALWAYS prints its tally, even when atr_iter_get matched
	/// nothing at all: a typo'd pattern still gets "No attributes wiped.", not silence.
	/// Round 3 moved the tally below this early return, which made a zero-match @wipe go
	/// completely silent - a real regression from round 2's (wrong, but at least present)
	/// generic success line (Task 6 fix round 4).
	/// </summary>
	private async ValueTask<Result<Success>> NothingToClearAsync(AnySharpObject executor, AnySharpObject obj,
		string attributePattern, bool isWipe)
	{
		if (isWipe)
		{
			await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NoAttributesWiped), executor, 0);
		}

		// AE_NOTFOUND (src/attrib.c:2411-2412) is reported only where do_set_atr's own reporting is
		// copied: a player's alias list.
		return !isWipe && PlayerAliases.Applies(obj, attributePattern)
			? new Error<string>(ErrorMessages.Notifications.NoSuchAttributeToReset)
			: new Success();
	}

	/// <summary>Whether a wildcarded @wipe steps over this match without a word.</summary>
	private static bool ProtectedFromWildcardWipe(SharpAttribute attrItem, bool executorIsGod)
	{
		// wipe_helper's own guard, ahead of everything wipe_atr does: a wildcarded @wipe
		// never touches a wizard-flagged attribute unless the player is God. It returns 0
		// silently - no notify, and the match contributes nothing to the tally - so a mass
		// wipe simply steps over the protected attributes rather than reporting each.
		// PermissionService.CanSet grants any wizard outright before its own AF_WIZARD test,
		// so without this a non-God wizard's `@wipe someplayer/**` destroyed every
		// wizard-flagged attribute Penn protects.
		if (!executorIsGod && attrItem.IsWizard())
		{
			return true;
		}

		// SharpMUSH keeps engine state in underscore-prefixed attributes: _LINKTYPE is
		// written by @link <exit>=home and @link <exit>=variable (BuildingCommands) and read
		// back by loc() to resolve where the exit actually goes. A wildcarded wipe has to
		// step over those the way it steps over wizard-flagged ones, or clearing a player's
		// attributes silently unlinks their exits. Naming one explicitly still clears it,
		// which is the same rule the wizard guard above follows - the protection is against
		// mass wipes, not against deliberate ones.
		return attrItem.LongName!.Split('`')[0].StartsWith('_');
	}

	/// <summary>What clearing one match did: whether it was refused, and how many attribute nodes it removed.</summary>
	private readonly record struct ClearOutcome(bool Denied, int Wiped);

	/// <summary>Clears one matched attribute, or reports why it could not.</summary>
	private async ValueTask<ClearOutcome> ClearMatchAsync(AnySharpObject executor, AnySharpObject obj, DBRef dbref,
		SharpAttribute attrItem, IReadOnlyDictionary<string, SharpAttribute> matchKnown, bool isWipe)
	{
		// AE_SAFE, not AE_ERROR: real_atr_clr (src/attrib.c:1100-1104) tests AF_Safe on the
		// matched attribute BEFORE Can_Write_Attr and returns a distinct code, which
		// wipe_helper reports with wording that names the remedy (set.c:1507-1509). Ancestor
		// safe flags still surface through CanSet below as the generic AE_ERROR, exactly as
		// they do in Penn (there the ancestor walk lives inside can_write_attr_internal).
		if (isWipe && attrItem.IsSafe())
		{
			await notifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.AttributeIsSafeSetNotSafe), executor, attrItem.LongName!);
			return new ClearOutcome(Denied: true, Wiped: 0);
		}

		var path = await ResolveWriteGatePathAsync(dbref, attrItem.LongName!, matchKnown);

		// A path shorter than the split name is a broken/orphaned chain. PennMUSH's
		// can_write_attr_internal (src/attrib.c:392-393) returns 0 the instant a prefix
		// segment can't be found - denying, not silently permitting on incomplete data
		// (Task 6 fix round 1, M1: CanSet(...) with an empty array returns true, so this
		// must be checked explicitly before ever calling CanSet).
		if (path is null || !await permissionService.CanSet(executor, obj, path))
		{
			if (isWipe)
			{
				// PennMUSH's AE_ERROR wording (set.c:1511-1513), one line per match -
				// never the raw "#-1 NO PERMISSION..." return code.
				await notifyService.NotifyLocalized(executor,
					nameof(ErrorMessages.Notifications.UnableToWipeAttribute), executor, attrItem.LongName!);
			}
			return new ClearOutcome(Denied: true, Wiped: 0);
		}

		// For wildcard patterns (used by @wipe), delete the attribute and its
		// descendants - gated per descendant (WipeSubtreeGatedAsync). For exact patterns
		// (used by @set obj/attr=), use ClearAttributeCommand, which preserves parent
		// nodes that still have children.
		if (!isWipe)
		{
			await mediator.Send(new ClearAttributeCommand(dbref, attrItem.LongName!.Split('`')));
			return new ClearOutcome(Denied: false, Wiped: 0);
		}

		var (fullyWiped, deletedCount) = await WipeSubtreeGatedAsync(executor, obj, attrItem);
		if (!fullyWiped)
		{
			// PennMUSH's AE_TREE wording (set.c:1514-1518), one line per match.
			await notifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.AttributeCannotBeWipedChildBlocked), executor, attrItem.LongName!);
		}
		return new ClearOutcome(Denied: false, Wiped: deletedCount);
	}

	/// <summary>
	/// Resolves the full root..leaf path for a WRITE gate. Unlike <see cref="AttributeAncestry"/>
	/// (built for the read path, where a missing ancestor is simply omitted so a caller can
	/// still evaluate what IS present - see <c>FetchAncestorAsync</c>), a missing ancestor here
	/// must deny the whole write: PennMUSH's <c>can_write_attr_internal</c>
	/// (<c>src/attrib.c:392-393</c>) returns 0 the instant a prefix segment isn't found, rather
	/// than treating a broken/orphaned chain as if the missing levels simply carried no flags.
	/// Ancestors present in <paramref name="known"/> are used without a query - the caller
	/// supplies whatever it already has in memory (sibling pattern matches, or an already-
	/// fetched subtree), so a query only ever happens for a genuinely absent prefix
	/// (Task 6 fix round 1, L1).
	/// </summary>
	/// <returns>The full path, or null if any prefix segment could not be resolved at all.</returns>
	private async ValueTask<SharpAttribute[]?> ResolveWriteGatePathAsync(
		DBRef dbref, string longName, IReadOnlyDictionary<string, SharpAttribute> known)
	{
		var segments = longName.Split('`');
		var result = new SharpAttribute[segments.Length];

		// Each prefix's name is a leading slice of longName ending at the next backtick, so it is
		// cut from the name rather than re-joined from the segments.
		var prefixEnd = -1;
		for (var i = 0; i < segments.Length; i++)
		{
			prefixEnd += segments[i].Length + 1;
			var prefixName = longName[..prefixEnd];

			if (known.TryGetValue(prefixName, out var attribute))
			{
				result[i] = attribute;
				continue;
			}

			var fetched = await mediator.CreateStream(new GetAttributeQuery(dbref, segments[..(i + 1)]))
				.LastOrDefaultAsync();

			if (fetched is null)
			{
				return null;
			}

			result[i] = fetched;
		}

		return result;
	}

	/// <summary>
	/// Deletes <paramref name="root"/> and its full descendant subtree if every one of them is
	/// individually writable. <paramref name="root"/>'s own permission has already passed the
	/// caller's gate.
	/// <para>
	/// PennMUSH's <c>real_atr_clr</c>/<c>atr_clear_children</c> (<c>attrib.c:1027-1145</c>)
	/// computes this bottom-up per node: a node's whole subtree is only removed if the node
	/// itself is writable AND every one of its children's subtrees also qualifies. A node that
	/// fails that test - because it isn't itself writable, or because ANY descendant beneath it
	/// isn't - is left <b>completely untouched: value included, not merely "kept but cleared."</b>
	/// There is no "clear the value but keep the node" middle ground in Penn for a branch that
	/// can't be fully cleared (<c>real_atr_clr</c> either <c>atr_free_one</c>s a node outright or
	/// does nothing to it at all) - a protected node's SIBLINGS are unaffected, though: each one
	/// is still deleted or preserved purely by its own subtree's outcome (Task 6 fix round 2:
	/// round 1's fallback wrongly called <c>ClearAttributeCommand</c>, which blanks the VALUE of
	/// any node that still has a remaining child, on every ancestor above a protected
	/// descendant - silently destroying data on an operation that was supposed to have been
	/// denied for that branch).
	/// </para>
	/// </summary>
	/// <returns>
	/// <c>FullyCleared</c>: <c>true</c> if the whole subtree (root included) was fully
	/// deleted; <c>false</c> if any part of it had to be left untouched because of a
	/// protected descendant. <c>DeletedCount</c>: how many attribute nodes were actually
	/// removed (root plus however many descendants qualified) - PennMUSH's <c>do_wipe</c>
	/// (<c>set.c:1568-1577</c>) always reports this count regardless of <c>FullyCleared</c>,
	/// so the caller needs it even on a partial result.
	/// </returns>
	private async ValueTask<(bool FullyCleared, int DeletedCount)> WipeSubtreeGatedAsync(
		AnySharpObject executor, AnySharpObject obj, SharpAttribute root)
	{
		var dbref = obj.Object().DBRef;
		var rootName = root.LongName!;

		// "root`**" matches every descendant at any depth (double-star crosses backticks) and
		// nothing else - root itself is never matched by this pattern.
		// checkParents: false - a wipe only ever touches the object's own subtree, so every
		// match here is sourced from dbref and the source can be dropped.
		// "?" is a legal attribute-name character (ValidateService.cs), and the wildcard
		// translation maps a literal "?" in the pattern to a single-char regex wildcard - so
		// an attribute literally named e.g. "WHAT?" turns "WHAT?`**" into a pattern that can
		// also match unrelated siblings sharing the "WHAT" prefix. Every extra match would
		// already be sitting in attrArr under its own gate, so this was never an actual
		// over-delete - but a delete path has no business trusting an unescaped
		// string-interpolated wildcard. Guard in memory instead.
		var subtreePrefix = rootName + "`";
		var descendants = await mediator
			.CreateStream(new GetAttributesQuery(dbref, $"{rootName}`**".ToUpper(), false,
				IAttributeService.AttributePatternMode.Wildcard))
			.Select(x => x.Attribute)
			.Where(d => d.LongName!.StartsWith(subtreePrefix, StringComparison.OrdinalIgnoreCase))
			.ToArrayAsync();

		if (descendants.Length == 0)
		{
			await mediator.Send(new WipeAttributeCommand(dbref, rootName.Split('`')));
			return (true, 1);
		}

		// Every ancestor of every descendant here is either root or another member of this
		// same subtree listing (the "**" match reaches all of them at once), so this dict
		// makes ResolveWriteGatePathAsync's per-descendant walk free - no query should ever
		// be needed below (Task 6 fix round 1, L1).
		var known = AttributeService.IndexByLongName(descendants, static x => x.LongName!);
		known[rootName] = root;

		var permitted = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) { [rootName] = true };
		var deniedAny = false;

		foreach (var descendant in descendants)
		{
			var path = await ResolveWriteGatePathAsync(dbref, descendant.LongName!, known);
			var ok = path is not null && await permissionService.CanSet(executor, obj, path);
			permitted[descendant.LongName!] = ok;
			deniedAny |= !ok;
		}

		if (!deniedAny)
		{
			// Common case: nothing in the subtree is protected - one recursive delete, same
			// as before this fix.
			await mediator.Send(new WipeAttributeCommand(dbref, rootName.Split('`')));
			return (true, 1 + descendants.Length);
		}

		// Bottom-up: a node's subtree is fully clearable only if the node itself is permitted
		// AND every one of its direct children's subtrees is also fully clearable - exactly
		// PennMUSH's recursive atr_clear_children definition. Processing deepest-first means a
		// node's children are already resolved (and, if clearable, already deleted) by the time
		// the node itself is evaluated.
		var deepestFirst = descendants
			.OrderByDescending(d => d.LongName!.Count(c => c == '`'))
			.ToArray();

		var fullyClearable = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

		// IsDirectChildOf trusts that a node's immediate parent is either `root` or another
		// entry in `descendants` whenever that node itself is one. That invariant comes from
		// HOW descendants is populated, not from string-parsing LongName: the underlying
		// GraphAttributes FILTER v.LongName =~ @pattern`) is a graph traversal along real
		// parent->child edges, so a node can only appear in the results if every ancestor
		// between the object root and that node has its own vertex and edge - an "FOO`BAR`BAZ
		// exists but FOO`BAR doesn't" gap is not representable by the traversal that produced
		// this list in the first place (the provider stores one node per level).
		// If that ever stopped holding, a node whose immediate parent is missing from this set
		// would be "nobody's child" to IsDirectChildOf and so could never block an ancestor's
		// fullyClearable computation - the mitigation, if it were ever needed, would be to
		// treat any node whose direct parent isn't in `known` as denied up front, the same way
		// ResolveWriteGatePathAsync already fails closed on a missing prefix (M1).
		bool IsFullyClearable(string name)
		{
			var childrenClearable = descendants
				.Where(d => IsDirectChildOf(d.LongName!, name))
				.All(d => fullyClearable[d.LongName!]);
			return permitted[name] && childrenClearable;
		}

		foreach (var descendant in deepestFirst)
		{
			fullyClearable[descendant.LongName!] = IsFullyClearable(descendant.LongName!);
		}

		var rootFullyClearable = IsFullyClearable(rootName);

		// Delete every node whose own subtree is fully clearable, deepest-first, so that by
		// the time a node is reached, any of its children that qualified have already been
		// removed - ClearAttributeCommand's own "no remaining children -> fully remove" path
		// then applies cleanly. A node that ISN'T fully clearable is never touched at all -
		// no ClearAttributeCommand call, no value change, matching real_atr_clr leaving a
		// blocked branch completely alone.
		var deletedCount = 0;
		foreach (var descendant in deepestFirst.Where(descendant => fullyClearable[descendant.LongName!]))
		{
			await mediator.Send(new ClearAttributeCommand(dbref, descendant.LongName!.Split('`')));
			deletedCount++;
		}

		if (rootFullyClearable)
		{
			await mediator.Send(new ClearAttributeCommand(dbref, rootName.Split('`')));
			deletedCount++;
		}

		return (rootFullyClearable, deletedCount);
	}

	/// <summary>
	/// True if <paramref name="childLongName"/> names a direct (one level deeper) child of
	/// <paramref name="parentLongName"/> in the attribute tree - not a grandchild or deeper.
	/// See the invariant this relies on, documented at its call site in
	/// <see cref="WipeSubtreeGatedAsync"/>.
	/// </summary>
	private static bool IsDirectChildOf(string childLongName, string parentLongName)
	{
		var prefix = parentLongName + "`";
		return childLongName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
			&& childLongName.LastIndexOf('`') == parentLongName.Length;
	}
}
