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
		AnySharpObject viewer, AnySharpObject target)
		=> await ShowsReferenceAsync(permissions, viewer, target)
			? await FormatObjectWithDbref(target.Object())
			: target.Object().Name;

	/// <inheritdoc cref="UnparseObjectAsync"/>
	/// <remarks>The name portion is hilighted, as <see cref="FormatObjectWithDbrefMString"/> does.</remarks>
	public static async ValueTask<MString> UnparseObjectMStringAsync(IPermissionService permissions,
		AnySharpObject viewer, AnySharpObject target)
		=> await ShowsReferenceAsync(permissions, viewer, target)
			? await FormatObjectWithDbrefMString(target.Object())
			: target.Object().Name.Hilight();

	/// <summary><c>real_unparse</c>'s dbref visibility test (<c>src/unparse.c:118-119</c>).</summary>
	private static async ValueTask<bool> ShowsReferenceAsync(IPermissionService permissions,
		AnySharpObject viewer, AnySharpObject target)
		=> await permissions.CanExamine(viewer, target)
			|| await permissions.CanLinkToAsync(viewer, target) || await target.HasFlag("JUMP_OK")
			|| await target.HasFlag("CHOWN_OK") || await target.HasFlag("DESTROY_OK");

	/// <summary>
	/// The concatenated flag symbols of an object, as PennMUSH appends them after a dbref.
	/// </summary>
	/// <param name="obj">The object whose flags to read</param>
	/// <returns>A task yielding the concatenated symbols</returns>
	public static async ValueTask<string> FlagSymbolsAsync(SharpObject obj)
		=> string.Concat(await obj.Flags.Value.Select(flag => flag.Symbol).ToArrayAsync());

	/// <inheritdoc cref="FlagSymbolsAsync"/>
	public static string FlagSymbols(IEnumerable<SharpObjectFlag> flags)
		=> string.Concat(flags.Select(flag => flag.Symbol));

	/// <summary>
	/// Formats an object name with its dbref and flag symbols as a markup-preserving MString.
	/// The name portion is hilighted (white foreground); the dbref and flag symbols are plain text.
	/// Example: an MString with "Chest" hilighted followed by plain "(#5Tn)".
	/// </summary>
	/// <param name="obj">The sharp object to format</param>
	/// <returns>A task yielding the formatted MString with the name hilighted</returns>
	public static async ValueTask<MString> FormatObjectWithDbrefMString(SharpObject obj)
	{
		var flagSymbols = await FlagSymbolsAsync(obj);
		return Format($"{obj.Name.Hilight()}(#{obj.DBRef.Number}{flagSymbols})");
	}

	/// <summary>
	/// Formats an object name with its dbref and flag symbols, matching PennMUSH display format.
	/// Example: "Chest(#5Tn)"
	/// </summary>
	/// <param name="obj">The sharp object to format</param>
	/// <returns>A task yielding the formatted string</returns>
	public static async ValueTask<string> FormatObjectWithDbref(SharpObject obj)
	{
		var flagSymbols = await FlagSymbolsAsync(obj);
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
