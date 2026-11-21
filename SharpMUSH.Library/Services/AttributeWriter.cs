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
using System.Text;

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
	/// <summary>
	/// <c>do_set_atr</c>: write <paramref name="value"/> to <paramref name="attribute"/> on <paramref name="obj"/>.
	/// </summary>
	/// <param name="creator">Recorded as the attribute's owner; the executor's owner when null.</param>
	/// <param name="isCopy">
	/// PennMUSH's <c>atr_cpy</c> (<c>@clone</c>, a snapshot restore). The control, name, stored-flag and
	/// <c>check_attr_value</c> gates still run; the creation gate, the length and count limits and the ALIAS, LISTEN and
	/// forward-list handling do not, because Penn reaches those only through <c>do_set_atr</c>.
	/// </param>
	/// <param name="storedValue">The stored form to write as it is, for a copy of an attribute's stored markup.</param>
	public async ValueTask<Result<WrittenAttribute>> WriteAsync(AnySharpObject executor, AnySharpObject obj,
		string attribute, MString value, SharpPlayer? creator = null, bool isCopy = false, StoredMarkup? storedValue = null)
	{
		var write = new Write(executor, obj, attribute.Split('`'), value, storedValue,
			creator ?? await executor.Object().Owner.WithCancellation(CancellationToken.None), isCopy,
			IsAlias: !isCopy && PlayerAliases.Applies(obj, attribute));

		// ALIAS reports itself, success or not (src/attrib.c:2418-2423).
		if (write.IsAlias)
		{
			var result = await WriteGuardedAsync(write);
			await ReportPlayerAliasAsync(executor, Succeeded(result), value.ToPlainText().Length > 0
				? nameof(ErrorMessages.Notifications.PlayerAliasSet)
				: nameof(ErrorMessages.Notifications.PlayerAliasRemoved));
			return result;
		}

		if (isCopy || !IsListen(attribute))
		{
			return await WriteGuardedAsync(write);
		}

		// An object that could not hear before its LISTEN was set "grows ears" (src/attrib.c:2424-2443).
		var before = await HearingAsync(obj);
		var written = await WriteGuardedAsync(write);
		if (written is WrittenAttribute && !before.Hearer && !before.Listener)
		{
			await AnnounceListenAsync(obj, nameof(ErrorMessages.Notifications.GrowsEarsFormat));
		}

		return written;
	}

	public static Result<Success> Succeeded(Result<WrittenAttribute> result) => result switch
	{
		WrittenAttribute => new Success(),
		Error<string> error => error
	};

	private sealed record Write(AnySharpObject Executor, AnySharpObject Obj, string[] Path, MString Value,
		StoredMarkup? StoredValue, SharpPlayer Creator, bool IsCopy, bool IsAlias);

	private async ValueTask<Result<WrittenAttribute>> WriteGuardedAsync(Write write)
	{
		try { return await WriteCoreAsync(write); }
		catch (InvalidDataException) { return new Error<string>("Attribute access metadata is unreadable; write refused."); }
	}

	/// <summary>The gates in <c>do_set_atr</c>'s order, then the write.</summary>
	private async ValueTask<Result<WrittenAttribute>> WriteCoreAsync(Write write)
	{
		var (executor, obj, path, value) = (write.Executor, write.Obj, write.Path, write.Value);
		var dbref = obj.Object().DBRef;
		var fullName = string.Join('`', path).ToUpperInvariant();
		var plain = value.ToPlainText();
		var limits = configuration.CurrentValue.Limit;

		if (!await permissionService.Controls(executor, obj))
		{
			return new Error<string>(ErrorMessages.Returns.AttrSetPermissions);
		}

		if (!await validateService.Valid(IValidateService.ValidationType.AttributeName, MarkupText.Plain(string.Join('`', path)), obj))
		{
			return new Error<string>(ErrorMessages.Returns.AttrBadName);
		}

		if (write.IsAlias && await PlayerAliasRestriction.CheckAsync(mediator, validateService, limits.MaxAliases,
				executor, obj, plain) is Error<string> badAlias)
		{
			return badAlias;
		}

		// The levels of the path that already exist must all be writable (can_write_attr_internal).
		var existing = await ExistingLevelsAsync(dbref, path);
		if (existing.Count > 0 && !await permissionService.CanSet(executor, obj, [.. existing]))
		{
			return new Error<string>(ErrorMessages.Returns.AttrSetPermissions);
		}

		// Each level this write creates is gated against the flags the attribute table will give it
		// (can_create_attr, src/attrib.c:446-486), before it exists.
		SharpAttributeEntry? leafEntry = null;
		for (var level = existing.Count; level < path.Length; level++)
		{
			var levelName = string.Join('`', path[..(level + 1)]).ToUpperInvariant();
			if (await mediator.Send(new GetAttributeEntryQuery(levelName)) is not { } entry)
			{
				continue;
			}

			if (level == path.Length - 1)
			{
				leafEntry = entry;
			}

			if (!write.IsCopy && !await permissionService.CanSet(executor, obj, CreatedAttributeFor(entry, path[level], levelName, write.Creator)))
			{
				return new Error<string>(ErrorMessages.Returns.AttrSetPermissions);
			}
		}

		var created = path.Length - existing.Count;
		var leafExists = created == 0;

		if (!write.IsCopy)
		{
			if (Encoding.UTF8.GetByteCount(plain) > limits.MaxAttributeValueLength)
			{
				return new Error<string>(ErrorMessages.Returns.AttrValueTooLong);
			}

			if (created > 0
					&& await mediator.Send(new GetAttributeCountQuery(dbref)) + created > limits.MaxAttributesPerObj
					&& !await obj.HasPower("MANY_ATTRIBS"))
			{
				return new Error<string>(ErrorMessages.Returns.TooManyAttributes);
			}

			if (ForwardListRestriction.Applies(fullName)
					&& await ForwardListRestriction.CheckAsync(mediator, permissionService, obj, fullName, plain) is Error<string> badList)
			{
				return badList;
			}
		}

		// @attribute/limit and /enum (check_attr_value); an enum stores the choice as the enum spells it.
		if ((leafExists ? await mediator.Send(new GetAttributeEntryQuery(fullName)) : leafEntry) is { } restriction)
		{
			switch (AttributeValueRestriction.Check(restriction, plain))
			{
				case Error<string> refused:
					return refused;
				case string stored when !stored.Equals(plain, StringComparison.Ordinal):
					value = MarkupText.Plain(stored);
					break;
			}
		}

		if (write.StoredValue is { } original && value.ToPlainText() != original.Value.ToPlainText())
		{
			return new Error<string>("Stored markup cannot be normalized safely; replace the attribute explicitly.");
		}

		await mediator.Send(new SetAttributeCommand(dbref, path, value, write.Creator, executor.Object().DBRef, write.StoredValue));

		// A value set leaves an existing leaf's flags alone; a new leaf gets its table entry's defaults.
		var leaf = leafExists
			? existing[^1]
			: await mediator.CreateStream(new GetAttributeQuery(dbref, path)).LastOrDefaultAsync();
		await WarnSyntaxErrorsAsync(executor, obj, leaf, value);
		return new WrittenAttribute(leaf?.IsQuiet() == true);
	}

	/// <summary>
	/// The path's existing levels, root first: the whole path when the attribute exists, otherwise its longest
	/// existing prefix (GetAttributeQuery returns nothing unless every level resolves).
	/// </summary>
	private async ValueTask<List<SharpAttribute>> ExistingLevelsAsync(DBRef dbref, string[] path)
	{
		for (var length = path.Length; length >= 1; length--)
		{
			var levels = await mediator.CreateStream(new GetAttributeQuery(dbref, path[..length])).ToListAsync();
			if (levels.Count > 0)
			{
				return levels;
			}
		}

		return [];
	}

	/// <summary>
	/// Set-time syntax check, advisory only: PennMUSH never validates softcode when it is set, so a syntax error
	/// is reported after the value is stored and never refuses it. A pattern half is compiled, not parsed, so only
	/// the code half is checked (<see cref="SoftcodeSource.Validate"/>).
	/// </summary>
	private async ValueTask WarnSyntaxErrorsAsync(AnySharpObject executor, AnySharpObject obj, SharpAttribute? leaf,
		MString value)
	{
		if (leaf?.SyntaxParseType() is not { } parseType)
		{
			return;
		}

		// Resolved here rather than injected: the parser's constructor resolves IAttributeService.
		var mushParser = serviceProvider.GetRequiredService<IMUSHCodeParser>();
		foreach (var error in SoftcodeSource.Validate(mushParser, value, parseType))
		{
			await notifyService.Notify(executor, error.ToMushFailureString(), obj);
		}
	}

	/// <summary>
	/// The attribute a level WOULD be once created: <c>can_create_attr</c>'s synthetic <c>ATTR</c> with the table's
	/// default flags and the creator stamped (src/attrib.c:446-458), so <see cref="IPermissionService.CanSet"/> judges
	/// it as it would the stored one.
	/// </summary>
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

	private static bool IsListen(string attribute) => attribute.Equals("LISTEN", StringComparison.OrdinalIgnoreCase);

	private async ValueTask<(bool Hearer, bool Listener)> HearingAsync(AnySharpObject obj)
		=> (await permissionService.IsHearer(obj), await obj.IsListener());

	/// <summary>
	/// <c>do_set_atr</c>'s LISTEN branch (PennMUSH <c>src/attrib.c:2424-2443</c>): an object that could not
	/// hear before a LISTEN is set "grows ears", and one a cleared LISTEN leaves unable to hear "loses its
	/// ears". Told to where the object is, or to a room itself, and what is there, the object excepted.
	/// </summary>
	private async ValueTask AnnounceListenAsync(AnySharpObject obj, string key)
	{
		var communication = serviceProvider.GetRequiredService<ICommunicationService>();
		var localization = serviceProvider.GetRequiredService<ILocalizationService>();
		var message = MarkupText.Plain(string.Format(localization.Get(key), obj.Object().Name));
		await communication.NotifyExceptAsync(obj, (await obj.Where()).WithExitOption(), obj, message, IPermissionService.InteractType.Presence);
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
		if (patternMode == IAttributeService.AttributePatternMode.Exact && IsListen(attributePattern))
		{
			var before = await HearingAsync(obj);
			var cleared = await ClearMatchingAsync(executor, obj, attributePattern, patternMode);
			if (cleared is Success && before.Hearer && !before.Listener && !await permissionService.IsHearer(obj))
			{
				await AnnounceListenAsync(obj, nameof(ErrorMessages.Notifications.LosesEarsFormat));
			}

			return cleared;
		}

		if (patternMode is IAttributeService.AttributePatternMode.Wildcard or IAttributeService.AttributePatternMode.Regex
			|| !PlayerAliases.Applies(obj, attributePattern))
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
		// @wipe asks for Wildcard, or Regex for RhostMUSH's @wipe/regexp; only @set obj/attr= asks for Exact.
		var isWipe = patternMode is IAttributeService.AttributePatternMode.Wildcard
			or IAttributeService.AttributePatternMode.Regex;

		// PennMUSH's wipe_helper (src/set.c:1503-1504):
		//   if (wildcard(pattern) && AF_Wizard(atr) && !God(player)) return 0;
		// "for added security, only God can modify wiz-only-modifiable attributes using this
		// command and wildcards. Wiping a specific attr still works, though." The guard is keyed
		// on whether the PATTERN TEXT actually contains a wildcard (wildcard(s) is
		// `wildcard_count(s, 0) == -1`, hdrs/externs.h:529 - an unescaped '*' or '?'), not on
		// which pattern mode the caller asked for: @wipe always requests Wildcard mode even for a
		// literal name, and that literal case must stay allowed.
		// A regular expression always counts as a wildcard: it names no attribute by itself.
		var patternIsWildcard = patternMode is IAttributeService.AttributePatternMode.Regex
			|| (isWipe && AttributeService.HasUnescapedWildcard(attributePattern));
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
			await mediator.Send(new ClearAttributeCommand(dbref, attrItem.LongName!.Split('`'), executor.Object().DBRef));
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
			await mediator.Send(new WipeAttributeCommand(dbref, rootName.Split('`'), executor.Object().DBRef));
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
			await mediator.Send(new WipeAttributeCommand(dbref, rootName.Split('`'), executor.Object().DBRef));
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
			await mediator.Send(new ClearAttributeCommand(dbref, descendant.LongName!.Split('`'), executor.Object().DBRef));
			deletedCount++;
		}

		if (rootFullyClearable)
		{
			await mediator.Send(new ClearAttributeCommand(dbref, rootName.Split('`'), executor.Object().DBRef));
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
