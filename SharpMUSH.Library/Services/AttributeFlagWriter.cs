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
/// PennMUSH's <c>do_attrib_flags</c>/<c>af_helper</c> (<c>src/set.c:483-535</c>): <c>@set obj/attr=&lt;flaglist&gt;</c>.
/// The caller reads the attribute; this parses the flag list, checks the write and reports.
/// </summary>
internal sealed class AttributeFlagWriter(
	IMediator mediator,
	IPermissionService permissionService,
	INotifyService notifyService)
{
	/// <summary>
	/// <c>string_to_atrflagsets</c> (<c>src/attrib.c:221-256</c>): the whole list is parsed before any attribute
	/// is looked at. An unknown name, or a privileged one the executor may not name, refuses all of it with the
	/// same wording. Flags come back in flag table order, the order the report names them in.
	/// </summary>
	public async ValueTask<Result<FlagChange[]>> ParseAsync(AnySharpObject executor, IReadOnlyList<string> flagTokens)
	{
		var table = await mediator.CreateStream(new GetAttributeFlagsQuery()).ToArrayAsync();

		var changes = new List<FlagChange>(flagTokens.Count);
		foreach (var token in flagTokens)
		{
			var unset = token.StartsWith('!');
			// A bare "!" leaves an empty name, which Named() refuses.
			var flag = table.Named(unset ? token[1..] : token);
			if (flag is null || !await MayNameAsync(executor, flag))
			{
				return new Error<string>(ErrorMessages.Returns.UnrecognizedAttributeFlag);
			}

			changes.Add(new FlagChange(flag, unset, Array.IndexOf(table, flag)));
		}

		return changes.OrderBy(c => c.Order).ToArray();
	}

	/// <summary>
	/// CanSet tests the flags the attribute already has, so without this a mortal could set <c>wizard</c> on its
	/// own attribute (<c>src/attrib.c:248-252</c>). <c>wizard_dark</c> is SharpMUSH's and God's alone.
	/// </summary>
	private static async ValueTask<bool> MayNameAsync(AnySharpObject executor, SharpAttributeFlag flag)
		=> flag.Name.ToUpperInvariant() switch
		{
			"MORTAL_DARK" => await executor.IsPriv(),
			"WIZARD" => await executor.IsSee_All(),
			"WIZARD_DARK" => executor.IsGod(),
			_ => true
		};

	/// <summary>
	/// <c>af_helper</c> for one attribute: one write check against the attribute as it was, clears, then sets,
	/// then one report line per direction.
	/// </summary>
	public async ValueTask<Result<Success>> ApplyAsync(AnySharpObject executor,
		AnySharpObject obj, SharpAttribute[] chain, FlagChange[] changes)
	{
		var target = chain.Last();
		var dbref = obj.Object().DBRef;
		var clears = changes.Where(c => c.Unset).Select(c => c.Flag).DistinctBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToArray();
		var sets = changes.Where(c => !c.Unset).Select(c => c.Flag).DistinctBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToArray();

		// Clearing SAFE is the one write that ignores SAFE (src/set.c:509-511).
		var permitted = clears.Any(f => f.Name.Equals("SAFE", StringComparison.OrdinalIgnoreCase))
			? await permissionService.CanSetIgnoringSafe(executor, obj, chain)
			: await permissionService.CanSet(executor, obj, chain);
		if (!permitted)
		{
			return new Error<string>(ErrorMessages.Returns.AttrSetPermissions);
		}

		// Clears run first on the same live set, so a flag named both ways ends set.
		var current = target.Flags.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach (var flag in clears.Where(f => current.Remove(f.Name)))
		{
			await mediator.Send(new UnsetAttributeFlagCommand(dbref, target, flag));
		}

		foreach (var flag in sets.Where(f => current.Add(f.Name)))
		{
			await mediator.Send(new SetAttributeFlagCommand(dbref, target, flag));
		}

		// AreQuiet(player, thing) || AF_Quiet(atr). The report names what was asked for, changed or not.
		if (target.IsQuiet() || await obj.Object().AreQuietAsync(executor))
		{
			return new Success();
		}

		if (clears.Length > 0)
		{
			await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeFlagsResetFormat), obj,
				obj.Object().Name, target.LongName!, string.Join(' ', clears.Select(f => f.Name)));
		}

		if (sets.Length > 0)
		{
			await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeFlagsSetFormat), obj,
				obj.Object().Name, target.LongName!, string.Join(' ', sets.Select(f => f.Name)));
		}

		return new Success();
	}
}

/// <summary>One flag named in a <c>@set obj/attr=</c> list, its direction, and its place in the flag table.</summary>
internal readonly record struct FlagChange(SharpAttributeFlag Flag, bool Unset, int Order);
