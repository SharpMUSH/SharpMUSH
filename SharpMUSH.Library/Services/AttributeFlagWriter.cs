using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// PennMUSH's <c>do_attrib_flags</c>/<c>af_helper</c> (<c>src/set.c:483-535</c>): the flag half of
/// <c>@set obj/attr=&lt;flaglist&gt;</c>, from parsing the whole argument into two bitmasks through
/// the privilege and write gates to the one-line-per-direction report.
/// </summary>
/// <remarks>
/// Held apart from <see cref="AttributeService"/> because it shares nothing with attribute storage
/// beyond the already-resolved chain: the caller does the read and the mode gate, and hands the
/// result here. Everything in this class is flag semantics.
/// </remarks>
internal sealed class AttributeFlagWriter(
	IMediator mediator,
	IPermissionService permissionService,
	INotifyService notifyService)
{
	/// <summary>
	/// Applies a whole list of attribute-flag tokens (each optionally <c>!</c>-prefixed to
	/// unset) to <paramref name="chain"/>'s leaf as ONE operation: one permission check against
	/// the pre-batch state, then every mutation applied together.
	/// </summary>
	/// <param name="chain">
	/// The resolved attribute path, already read in <see cref="IAttributeService.AttributeMode.SystemSet"/>
	/// mode and without parent inheritance — Penn's <c>af_helper</c> only ever iterates the target
	/// object's own attributes (<c>atr_iter_get</c>), never a parent's.
	/// </param>
	public async ValueTask<Result<Success>> ApplyAsync(AnySharpObject executor,
		AnySharpObject obj, SharpAttribute[] chain, IReadOnlyList<string> flagTokens)
	{
		var flagList = await mediator.CreateStream(new GetAttributeFlagsQuery()).ToArrayAsync();

		var resolved = new List<(SharpAttributeFlag Flag, bool Unset)>(flagTokens.Count);
		foreach (var token in flagTokens)
		{
			var unset = token.StartsWith('!');

			// A bare "!" survives MushText.SplitList (which only drops empty items), leaving an empty
			// name, which Named() refuses rather than letting `@set obj/attr=!` unset an arbitrary flag.
			var flag = flagList.Named(unset ? token[1..] : token);

			if (flag is null)
			{
				// Mirrors Penn: string_to_atrflagsets fails the WHOLE argument on one bad flag
				// name, before any flag in the batch is ever applied.
				return new Error<string>(ErrorMessages.Returns.UnrecognizedAttributeFlag);
			}

			resolved.Add((flag, unset));
		}

		// PennMUSH's string_to_atrflagsets (src/attrib.c:248-252) rejects the WHOLE argument -
		// before a single flag is applied, and with the same "unrecognized flag" wording, so the
		// player learns nothing about the flag's existence - when an unprivileged player so much
		// as NAMES a privileged flag, in either direction: Hasprivs (wizard or royalty) for
		// mortal_dark, See_All for wizard. CanSet below is a different gate entirely: it tests
		// the flags the attribute ALREADY carries, so on its own it lets a mortal set `wizard`
		// on an unflagged attribute of their own (and then locks them out of it).
		if (resolved.Any(r => r.Flag.Name.Equals("MORTAL_DARK", StringComparison.OrdinalIgnoreCase))
				&& !await executor.IsPriv())
		{
			return new Error<string>(ErrorMessages.Returns.UnrecognizedAttributeFlag);
		}

		if (resolved.Any(r => r.Flag.Name.Equals("WIZARD", StringComparison.OrdinalIgnoreCase))
				&& !await executor.IsSee_All())
		{
			return new Error<string>(ErrorMessages.Returns.UnrecognizedAttributeFlag);
		}

		var target = chain.Last();

		// Penn's af_helper (src/set.c:509-511) requires the normal, safe-obeying Can_Write_Attr
		// UNLESS the batch clears SAFE itself, in which case it falls back to
		// Can_Write_Attr_Ignore_Safe for the WHOLE batch - the one safe=0 call site in the
		// codebase. Every other batch (including one that sets/clears other flags on an
		// attribute that happens to carry safe) still obeys it.
		var clearingSafe = resolved.Any(r => r.Unset && r.Flag.Name.Equals("SAFE", StringComparison.OrdinalIgnoreCase));
		var permitted = clearingSafe
			? await permissionService.CanSetIgnoringSafe(executor, obj, chain)
			: await permissionService.CanSet(executor, obj, chain);

		if (!permitted)
		{
			return new Error<string>(ErrorMessages.Returns.AttrSetPermissions);
		}

		// Tracked as the loops run, not snapshotted: af_helper applies `AL_FLAGS(atr) &= ~clrf`
		// and then `AL_FLAGS(atr) |= setf` to the SAME live bitmask, so the set pass sees the
		// clear pass's result. Against a frozen snapshot, `!wizard wizard` would clear the flag
		// and then skip the re-set as "already set", ending unset - the exact opposite of Penn.
		var currentFlags = target.Flags
			.Select(f => f.Name)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		// Clear first, then set - af_helper's own `AL_FLAGS(atr) &= ~clrf;` before
		// `AL_FLAGS(atr) |= setf;`, so the same flag appearing in both directions in one batch
		// ends up set, and the outcome never depends on the order flags were typed in.
		//
		// The HashSet's return value is the "did this actually change anything" test, so a flag
		// already in the requested state costs no command round-trip. It deliberately does NOT
		// gate the report: Penn notifies from the REQUESTED bitmask (`if (af->clrf)`), not from
		// what changed, so `@set obj/attr=!wizard` on an attribute without wizard still says
		// "wizard reset." - there is no "is not set" case in Penn to report.
		foreach (var (flag, _) in resolved.Where(r => r.Unset).DistinctBy(r => r.Flag.Name, StringComparer.OrdinalIgnoreCase))
		{
			if (currentFlags.Remove(flag.Name))
			{
				await mediator.Send(new UnsetAttributeFlagCommand(obj.Object().DBRef, target, flag));
			}
		}

		foreach (var (flag, _) in resolved.Where(r => !r.Unset).DistinctBy(r => r.Flag.Name, StringComparer.OrdinalIgnoreCase))
		{
			if (currentFlags.Add(flag.Name))
			{
				await mediator.Send(new SetAttributeFlagCommand(obj.Object().DBRef, target, flag));
			}
		}

		await ReportAsync(executor, obj, target, resolved, flagList);

		return new Success();
	}

	/// <summary>
	/// af_helper reports each half of the batch as ONE line naming the whole flag list -
	/// <c>notify_format(player, T("%s/%s - %s reset."), AName(thing), AL_NAME(atr), af-&gt;clrflags)</c>
	/// and the <c>set.</c> counterpart (<c>src/set.c:522-535</c>). It has no per-flag "already set"
	/// or "is not set" line at all: those were a SharpMUSH invention that turned one <c>@set</c>
	/// into up to N messages and leaked whether a flag the player may not even see was present.
	/// </summary>
	private async ValueTask ReportAsync(AnySharpObject executor, AnySharpObject obj, SharpAttribute target,
		List<(SharpAttributeFlag Flag, bool Unset)> resolved, SharpAttributeFlag[] flagList)
	{
		if (await IsQuietForAsync(executor, obj, target))
		{
			return;
		}

		// The flag names are ordered by the flag table rather than by how they were typed, because
		// Penn builds this string with atrflag_to_string -> privs_to_string, which walks
		// attr_privs_view in table order.
		var tableOrder = flagList
			.Select((f, i) => (f.Name, i))
			.ToDictionary(x => x.Name, x => x.i, StringComparer.OrdinalIgnoreCase);

		string Render(IEnumerable<(SharpAttributeFlag Flag, bool Unset)> half) => string.Join(' ', half
			.Select(r => r.Flag.Name)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(name => tableOrder.TryGetValue(name, out var i) ? i : int.MaxValue, Comparer<int>.Default));

		var objectName = obj.Object().Name;

		if (resolved.Any(r => r.Unset))
		{
			await notifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.AttributeFlagsResetFormat), obj,
				objectName, target.LongName!, Render(resolved.Where(r => r.Unset)));
		}

		if (resolved.Any(r => !r.Unset))
		{
			await notifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.AttributeFlagsSetFormat), obj,
				objectName, target.LongName!, Render(resolved.Where(r => !r.Unset)));
		}
	}

	/// <summary>
	/// PennMUSH's <c>af_helper</c> suppression test: <c>AreQuiet(player, thing) || AF_Quiet(atr)</c>.
	/// <c>AreQuiet(x, y)</c> is <c>Quiet(x) || (Quiet(y) &amp;&amp; Owner(y) == x)</c>
	/// (<c>hdrs/dbdefs.h:198</c>) - note the second term needs the player to BE the object's owner,
	/// not merely to share one with it.
	/// </summary>
	/// <remarks>
	/// The object half is <c>SharpObjectExtensions.AreQuietAsync</c> now that it reads the flag
	/// through <c>HelperFunctions.HasFlag</c> (#1175); only the attribute's own <c>AF_QUIET</c>,
	/// which the extension has no counterpart for, is left here.
	/// </remarks>
	private static async ValueTask<bool> IsQuietForAsync(AnySharpObject executor, AnySharpObject obj,
		SharpAttribute attribute)
		=> attribute.IsQuiet() || await obj.Object().AreQuietAsync(executor);
}
