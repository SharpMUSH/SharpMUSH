using MarkupString;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using static MarkupString.MStringInterpolation;

namespace SharpMUSH.Library.Common;

public static class MessageFormatting
{
	/// <summary>
	/// PennMUSH's <c>unparse_object</c> (<c>src/unparse.c:39</c> into <c>real_unparse</c>): the object's
	/// name, and its dbref with flag symbols after it when the viewer is allowed to see them.
	/// </summary>
	/// <remarks>
	/// <c>unparse_object</c> passes <c>obey_myopic = 0</c>, so MYOPIC does not suppress the dbref here —
	/// that is <c>unparse_object_myopic</c>. The visibility test is <c>real_unparse</c>'s
	/// (<c>src/unparse.c:118-119</c>): examine, link-to, or any of JUMP_OK / CHOWN_OK / DESTROY_OK.
	/// </remarks>
	public static async ValueTask<string> UnparseObjectAsync(IPermissionService permissions,
		AnySharpObject viewer, AnySharpObject target, IConnectionService connections)
		=> await ShowsReferenceAsync(permissions, viewer, target)
			? await FormatObjectWithDbref(target.Object(), await FlagView.ForAsync(viewer, connections))
			: target.Object().Name;

	/// <inheritdoc cref="UnparseObjectAsync"/>
	/// <remarks>The name portion is hilighted, as <see cref="FormatObjectWithDbrefMString"/> does.</remarks>
	public static async ValueTask<MString> UnparseObjectMStringAsync(IPermissionService permissions,
		AnySharpObject viewer, AnySharpObject target, IConnectionService connections)
		=> await ShowsReferenceAsync(permissions, viewer, target)
			? await FormatObjectWithDbrefMString(target.Object(), await FlagView.ForAsync(viewer, connections))
			: target.Object().Name.Hilight();

	/// <summary><c>real_unparse</c>'s dbref visibility test (<c>src/unparse.c:118-119</c>).</summary>
	private static async ValueTask<bool> ShowsReferenceAsync(IPermissionService permissions,
		AnySharpObject viewer, AnySharpObject target)
		=> await permissions.CanExamine(viewer, target)
			|| await permissions.CanLinkToAsync(viewer, target) || await target.HasFlag("JUMP_OK")
			|| await target.HasFlag("CHOWN_OK") || await target.HasFlag("DESTROY_OK");

	/// <summary>
	/// PennMUSH's flag table in bit order: <c>hdrs/flag_tab.h</c>, then the flags <c>src/flags.c</c>
	/// adds at startup. <c>unparse_flags</c> and <c>bits_to_string</c> walk the flags in this order.
	/// </summary>
	private static readonly string[] PennFlagOrder =
	[
		"CHOWN_OK", "DARK", "GOING", "HAVEN", "TRUST", "LINK_OK", "OPAQUE", "QUIET", "STICKY", "UNFINDABLE",
		"VISUAL", "WIZARD", "SAFE", "AUDIBLE", "DEBUG", "NO_WARN", "ENTER_OK", "HALT", "NO_COMMAND", "LIGHT",
		"ROYALTY", "TRANSPARENT", "VERBOSE", "ANSI", "COLOR", "MONITOR", "NOSPOOF", "SHARED", "TRACK_MONEY",
		"CONNECTED", "GAGGED", "MYOPIC", "TERSE", "JURY_OK", "JUDGE", "FIXED", "UNREGISTERED", "ON-VACATION",
		"SUSPECT", "PARANOID", "NOACCENTS", "DESTROY_OK", "PUPPET", "NO_LEAVE", "LISTEN_PARENT", "Z_TEL",
		"ABODE", "FLOATING", "JUMP_OK", "NO_TEL", "UNINSPECTED", "CLOUDY", "GOING_TWICE", "KEEPALIVE", "NO_LOG",
		"OPEN_OK", "MISTRUST", "ORPHAN", "HEAVY", "LOUD", "HEAR_CONNECT", "CHAN_USEFIRSTMATCH", "XTERM256",
		"MONIKER"
	];

	private static readonly Dictionary<string, int> PennFlagRank = PennFlagOrder
		.Select((name, rank) => (name, rank))
		.Append((name: "ON_VACATION", rank: Array.IndexOf(PennFlagOrder, "ON-VACATION")))
		.ToDictionary(x => x.name, x => x.rank, StringComparer.OrdinalIgnoreCase);

	/// <summary>The CONNECTED pseudo-flag, which no object stores: it is the player's connection state.</summary>
	private static readonly SharpObjectFlag Connected = new()
	{
		Name = "CONNECTED",
		Symbol = "c",
		SetPermissions = ["internal"],
		UnsetPermissions = ["internal"],
		TypeRestrictions = ["PLAYER"],
		System = true
	};

	/// <summary>
	/// The object's flags as a viewer sees them, in PennMUSH's bit order (SharpMUSH's own flags after,
	/// by name): no type pseudo-flag, CONNECTED for a connected player, and only what
	/// <see cref="FlagView.CanSeeAsync"/> allows. Without a view every stored flag is shown and CONNECTED never is.
	/// </summary>
	public static async ValueTask<SharpObjectFlag[]> VisibleFlagsAsync(SharpObject obj, FlagView? view = null)
	{
		var flags = new List<SharpObjectFlag>();
		var ownFlags = (await obj.ReadFlagsAsync()).Flags
			.Where(flag => !flag.Name.Equals(obj.Type, StringComparison.OrdinalIgnoreCase));
		foreach (var flag in ownFlags)
		{
			if (view is not null && !await view.CanSeeAsync(obj, flag)) continue;
			flags.Add(flag);
		}

		if (view is not null && await view.SeesConnectedAsync(obj))
		{
			flags.Add(Connected);
		}

		return flags
			.OrderBy(flag => PennFlagRank.GetValueOrDefault(flag.Name, int.MaxValue))
			.ThenBy(flag => flag.Name, StringComparer.Ordinal)
			.ToArray();
	}

	/// <summary>
	/// PennMUSH's <c>unparse_flags</c> (<c>src/flags.c:1638</c>): the type letter, then the symbols of
	/// <see cref="VisibleFlagsAsync"/>. Example: <c>PenAc</c> for a connected player.
	/// </summary>
	public static async ValueTask<string> FlagSymbolsAsync(SharpObject obj, FlagView? view = null)
		=> obj.Type[..1].ToUpperInvariant()
			+ string.Concat((await VisibleFlagsAsync(obj, view)).Select(flag => flag.Symbol));

	/// <summary>
	/// PennMUSH's <c>flag_description</c> (<c>src/flags.c:1683</c>), examine's
	/// <c>Type: PLAYER Flags: ...</c> line.
	/// </summary>
	public static async ValueTask<string> FlagDescriptionAsync(SharpObject obj, FlagView? view = null)
		=> $"Type: {obj.Type} Flags: {string.Join(" ", (await VisibleFlagsAsync(obj, view)).Select(flag => flag.Name))}";

	/// <summary>
	/// Formats an object name with its dbref and flag symbols as a markup-preserving MString.
	/// The name portion is hilighted (white foreground); the dbref and flag symbols are plain text.
	/// Example: an MString with "Chest" hilighted followed by plain "(#5Tn)".
	/// </summary>
	/// <param name="obj">The sharp object to format</param>
	/// <param name="view">Whose view of the flags to show; see <see cref="VisibleFlagsAsync"/>.</param>
	/// <returns>A task yielding the formatted MString with the name hilighted</returns>
	public static async ValueTask<MString> FormatObjectWithDbrefMString(SharpObject obj, FlagView? view = null)
	{
		var flagSymbols = await FlagSymbolsAsync(obj, view);
		return Format($"{obj.Name.Hilight()}(#{obj.DBRef.Number}{flagSymbols})");
	}

	/// <summary>
	/// Formats an object name with its dbref and flag symbols, matching PennMUSH display format.
	/// Example: "Chest(#5Tn)"
	/// </summary>
	/// <param name="obj">The sharp object to format</param>
	/// <param name="view">Whose view of the flags to show; see <see cref="VisibleFlagsAsync"/>.</param>
	/// <returns>A task yielding the formatted string</returns>
	public static async ValueTask<string> FormatObjectWithDbref(SharpObject obj, FlagView? view = null)
	{
		var flagSymbols = await FlagSymbolsAsync(obj, view);
		return $"{obj.Name}(#{obj.DBRef.Number}{flagSymbols})";
	}

	/// <summary>
	/// Formats a list of strings using Oxford comma style.
	/// Examples: ["East"] → "East"; ["East", "West"] → "East and West";
	/// ["East", "West", "North"] → "East, West, and North"
	/// </summary>
	/// <param name="items">The items to format</param>
	/// <returns>The formatted string</returns>
	public static string FormatWithOxfordComma(IReadOnlyList<string> items) => items.Count switch
	{
		0 => string.Empty,
		1 => items[0],
		2 => $"{items[0]} and {items[1]}",
		_ => string.Join(", ", items.Take(items.Count - 1)) + ", and " + items[^1]
	};

	/// <summary>
	/// Formats a list of MStrings using Oxford comma style, preserving markup.
	/// </summary>
	public static MString FormatMStringsWithOxfordComma(IReadOnlyList<MString> items) => items.Count switch
	{
		0 => MarkupText.Empty,
		1 => items[0],
		2 => MarkupText.Concat([items[0], MarkupText.Plain(" and "), items[1]]),
		_ => MarkupText.Concat(items.SelectMany<MString, MString>((item, i) => i == 0
				? [item]
				: i < items.Count - 1
					? [MarkupText.Plain(", "), item]
					: [MarkupText.Plain(", and "), item]).ToArray())
	};
}
