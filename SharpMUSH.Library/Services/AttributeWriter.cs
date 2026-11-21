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
	/// Wildcard and Regex mode is <c>@wipe</c> (PennMUSH <c>do_wipe</c>); Exact mode is <c>@set obj/attr=</c>
	/// (<c>do_set_atr</c> with no value).
	/// </summary>
	public async ValueTask<Result<Success>> ClearAttributeAsync(AnySharpObject executor,
		AnySharpObject obj,
		string attributePattern,
		IAttributeService.AttributePatternMode patternMode)
	{
		if (patternMode is not IAttributeService.AttributePatternMode.Exact)
		{
			return await WipeAsync(executor, obj, attributePattern, patternMode);
		}

		if (IsListen(attributePattern))
		{
			var before = await HearingAsync(obj);
			var cleared = await ClearExactAsync(executor, obj, attributePattern);
			if (cleared is Success && before.Hearer && !before.Listener && !await permissionService.IsHearer(obj))
			{
				await AnnounceListenAsync(obj, nameof(ErrorMessages.Notifications.LosesEarsFormat));
			}

			return cleared;
		}

		var result = await ClearExactAsync(executor, obj, attributePattern);
		if (PlayerAliases.Applies(obj, attributePattern))
		{
			await ReportPlayerAliasAsync(executor, result, nameof(ErrorMessages.Notifications.PlayerAliasRemoved));
		}

		return result;
	}

	/// <summary>Clears one attribute node, keeping it while it still has children.</summary>
	private async ValueTask<Result<Success>> ClearExactAsync(AnySharpObject executor, AnySharpObject obj, string name)
	{
		if (!await permissionService.Controls(executor, obj))
		{
			return new Error<string>(ErrorMessages.Returns.AttrSetPermissions);
		}

		var dbref = obj.Object().DBRef;
		var matches = await MatchesAsync(dbref, name, IAttributeService.AttributePatternMode.Exact);

		// AE_NOTFOUND (src/attrib.c:2411-2412) is reported only for a player's alias list.
		if (matches.Length == 0)
		{
			return PlayerAliases.Applies(obj, name)
				? new Error<string>(ErrorMessages.Notifications.NoSuchAttributeToReset)
				: new Success();
		}

		var known = AttributeService.IndexByLongName(matches, static x => x.LongName!);
		var denied = false;
		foreach (var match in matches)
		{
			if (!await CanWriteAsync(executor, obj, match.LongName!, known))
			{
				denied = true;
				continue;
			}

			await mediator.Send(new ClearAttributeCommand(dbref, match.LongName!.Split('`'), executor.Object().DBRef));
		}

		return denied
			? new Error<string>(ErrorMessages.Returns.AttrSetPermissions)
			: new Success();
	}

	/// <summary>
	/// <c>do_wipe</c> (src/set.c:1493-1577): each match is wiped or refused with its own line, then a tally is
	/// always printed, even when nothing matched.
	/// </summary>
	private async ValueTask<Result<Success>> WipeAsync(AnySharpObject executor, AnySharpObject obj,
		string pattern, IAttributeService.AttributePatternMode patternMode)
	{
		if (!await permissionService.Controls(executor, obj))
		{
			return new Error<string>(ErrorMessages.Returns.AttrSetPermissions);
		}

		var matches = await MatchesAsync(obj.Object().DBRef, pattern, patternMode);
		var known = AttributeService.IndexByLongName(matches, static x => x.LongName!);

		// wipe_helper skips on a wildcard in the pattern text, not on the mode: @wipe asks for Wildcard
		// even for a literal name. A regular expression names no attribute by itself.
		var wildcard = patternMode is IAttributeService.AttributePatternMode.Regex
			|| AttributeService.HasUnescapedWildcard(pattern);
		var isGod = executor.IsGod();

		var wiped = 0;
		foreach (var match in matches)
		{
			if (wildcard && SkippedByWildcardWipe(match, isGod))
			{
				continue;
			}

			// real_atr_clr tests AF_SAFE on the match itself before the write check (src/attrib.c:1100-1104).
			if (match.IsSafe())
			{
				await notifyService.NotifyLocalized(executor,
					nameof(ErrorMessages.Notifications.AttributeIsSafeSetNotSafe), executor, match.LongName!);
				continue;
			}

			if (!await CanWriteAsync(executor, obj, match.LongName!, known))
			{
				await notifyService.NotifyLocalized(executor,
					nameof(ErrorMessages.Notifications.UnableToWipeAttribute), executor, match.LongName!);
				continue;
			}

			var (whole, count) = await WipeSubtreeAsync(executor, obj, match);
			wiped += count;
			if (!whole)
			{
				await notifyService.NotifyLocalized(executor,
					nameof(ErrorMessages.Notifications.AttributeCannotBeWipedChildBlocked), executor, match.LongName!);
			}
		}

		await notifyService.NotifyLocalized(executor, wiped switch
		{
			0 => nameof(ErrorMessages.Notifications.NoAttributesWiped),
			1 => nameof(ErrorMessages.Notifications.OneAttributeWiped),
			_ => nameof(ErrorMessages.Notifications.AttributesWipedCount)
		}, executor, wiped);

		return new Success();
	}

	/// <summary>The object's own attributes matching the pattern, never its parents'.</summary>
	private async ValueTask<SharpAttribute[]> MatchesAsync(DBRef dbref, string pattern,
		IAttributeService.AttributePatternMode patternMode)
		=> await mediator.CreateStream(new GetAttributesQuery(dbref, pattern, false, patternMode))
			.Select(x => x.Attribute)
			.ToArrayAsync();

	/// <summary>
	/// A wildcarded wipe steps over these without a word. Wizard attributes unless the wiper is God
	/// (wipe_helper, src/set.c:1503-1504); CanSet lets any wizard through, so this is the only guard.
	/// Engine state under a <c>_</c> root, such as an exit's <c>_LINKTYPE</c>. Naming either one clears it.
	/// </summary>
	private static bool SkippedByWildcardWipe(SharpAttribute attribute, bool isGod)
		=> (!isGod && attribute.IsWizard()) || attribute.LongName!.Split('`')[0].StartsWith('_');

	/// <summary>
	/// <c>can_write_attr_internal</c> over the whole path from the root to <paramref name="longName"/>.
	/// A level that does not exist refuses the write (src/attrib.c:392-393). Levels in
	/// <paramref name="known"/> are used without a query.
	/// </summary>
	private async ValueTask<bool> CanWriteAsync(AnySharpObject executor, AnySharpObject obj, string longName,
		IReadOnlyDictionary<string, SharpAttribute> known)
	{
		var dbref = obj.Object().DBRef;
		var segments = longName.Split('`');
		var path = new SharpAttribute[segments.Length];

		var prefixEnd = -1;
		for (var i = 0; i < segments.Length; i++)
		{
			prefixEnd += segments[i].Length + 1;
			if (known.TryGetValue(longName[..prefixEnd], out var attribute))
			{
				path[i] = attribute;
				continue;
			}

			var fetched = await mediator.CreateStream(new GetAttributeQuery(dbref, segments[..(i + 1)])).LastOrDefaultAsync();
			if (fetched is null)
			{
				return false;
			}

			path[i] = fetched;
		}

		return await permissionService.CanSet(executor, obj, path);
	}

	/// <summary>
	/// Deletes <paramref name="root"/> and everything under it that may be written. <c>real_atr_clr</c>
	/// (src/attrib.c:1027-1145) removes a node only when the node and everything under it may be written;
	/// any other node is left as it is, value included. The root passed the caller's check.
	/// </summary>
	/// <returns>Whether the whole subtree went, and how many nodes were deleted.</returns>
	private async ValueTask<(bool Whole, int Deleted)> WipeSubtreeAsync(AnySharpObject executor, AnySharpObject obj,
		SharpAttribute root)
	{
		var dbref = obj.Object().DBRef;
		var rootName = root.LongName!;
		var by = executor.Object().DBRef;

		// "?" is a legal name character and a wildcard in the pattern, so the prefix is checked again here.
		var prefix = rootName + "`";
		var descendants = (await MatchesAsync(dbref, $"{rootName}`**".ToUpper(), IAttributeService.AttributePatternMode.Wildcard))
			.Where(d => d.LongName!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			.ToArray();

		var known = AttributeService.IndexByLongName(descendants, static x => x.LongName!);
		known[rootName] = root;

		var refused = new List<string>();
		foreach (var descendant in descendants)
		{
			if (!await CanWriteAsync(executor, obj, descendant.LongName!, known))
			{
				refused.Add(descendant.LongName!);
			}
		}

		if (refused.Count == 0)
		{
			await mediator.Send(new WipeAttributeCommand(dbref, rootName.Split('`'), by));
			return (true, 1 + descendants.Length);
		}

		// A refused node keeps itself and every level above it, the root included.
		bool Kept(string name) => refused.Any(r => r.Equals(name, StringComparison.OrdinalIgnoreCase)
			|| r.StartsWith(name + "`", StringComparison.OrdinalIgnoreCase));

		var deleted = 0;
		foreach (var descendant in descendants
			.Where(d => !Kept(d.LongName!))
			.OrderByDescending(d => d.LongName!.Count(c => c == '`')))
		{
			await mediator.Send(new ClearAttributeCommand(dbref, descendant.LongName!.Split('`'), by));
			deleted++;
		}

		return (false, deleted);
	}
}
