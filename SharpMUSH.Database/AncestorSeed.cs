using MarkupString;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Database;

/// <summary>
/// Seeds the default <c>FORMAT`*</c> attributes on the Ancestor Player (#4) so a plain player inherits
/// PennMUSH-style say/pose/semipose/emit render templates even without a per-player override, and the
/// <c>NAMEFORMAT</c> and <c>DESCFORMAT</c> every room inherits from the Ancestor Room (#3).
///
/// <para>The literals here mirror the built-in fallbacks consumed by the scene-capture package
/// (<c>2026-06-21-scene-capture-message-formats-design.md</c>): the format reads <c>%0</c> = the message
/// text and a recipient-dbref argument (<c>%1</c>) so it can render the speaker's "You…" form versus an
/// observer's "Name…" form. <c>%#</c> is the speaker (enactor).</para>
///
/// <para>Run once at the tail of the provider's migration via <see cref="IAttributeStore.SetAttributeAsync"/>,
/// the same code path every attribute write takes.
/// Idempotent: re-running simply overwrites with the same values.</para>
/// </summary>
public static class AncestorSeed
{
	/// <summary>The Ancestor Player slot seeded by the create-database migration.</summary>
	private static readonly DBRef AncestorPlayer = new(4);

	/// <summary>God (#1): owner of the seeded attributes.</summary>
	private static readonly DBRef God = new(1);

	/// <summary>
	/// The default FORMAT templates. Keyed by the leaf attribute path under <c>FORMAT`</c>.
	/// </summary>
	private static readonly (string[] Path, string Value)[] Formats =
	[
		// Speaker (recipient == %#) sees "You say, ..."; everyone else sees "<Name> says, ...".
		(["FORMAT", "SAY"], "[if(strmatch(%1,%#),You say\\, \"%0\",[name(%#)] says\\, \"%0\")]"),
		// Pose / semipose / emit render identically for speaker and observers.
		(["FORMAT", "POSE"], "[name(%#)] %0"),
		(["FORMAT", "SEMIPOSE"], "[name(%#)]%0"),
		(["FORMAT", "EMIT"], "%0"),
	];

	/// <summary>The Ancestor Room slot seeded by the create-database migration.</summary>
	private static readonly DBRef AncestorRoom = new(3);

	/// <summary>
	/// How every room looks unless it says otherwise, evaluated as the room being looked at. The name is a
	/// rule with the name (<c>%1</c>, as <c>look</c> would show it, its colour left to the theme) set into it
	/// at the left. The description is the room's <c>@describe</c>, beside its picture (<c>IMAGE</c>,
	/// described by <c>IMAGE`ALT</c>) when it has one. A picture is shown only when the room itself may show
	/// pictures (Send_Image or the approved role) and the reader's client can draw it; otherwise its
	/// description stands in for it, above the text.
	/// </summary>
	private static readonly (string[] Path, string Value)[] RoomFormats =
	[
		(["NAMEFORMAT"], "[rule(stripansi(%1),,json(object,title,json(string,left)))]"),
		// localize() keeps the description register from reaching the code that ran the look.
		(["DESCFORMAT"], "[localize(setq(description,if(or(strlen(%0),hasattrp(%!,DESCRIBE)),%0,You see nothing special.))"
			+ "[if(hasattrval(%!,IMAGE),figure(v(IMAGE),v(IMAGE`ALT),,left,%q<description>),%q<description>)])]"),
	];

	/// <summary>
	/// Seed the FORMAT attributes on the Ancestor Player and the room formats on the Ancestor Room. No-op if
	/// God (#1) is missing (defensive — both are created earlier in the same migration).
	/// </summary>
	public static async ValueTask SeedAncestorFormatsAsync(ISharpDatabase database,
		CancellationToken ct = default)
	{
		if (await database.GetObjectNodeAsync(God, ct) is not (AnySharpObject and SharpPlayer owner) || string.IsNullOrEmpty(owner.Id))
		{
			return;
		}

		foreach (var (path, value) in Formats)
		{
			await database.SetAttributeAsync(AncestorPlayer, path, MarkupText.Plain(value), owner, ct);
		}

		foreach (var (path, value) in RoomFormats)
		{
			await database.SetAttributeAsync(AncestorRoom, path, MarkupText.Plain(value), owner, ct);
		}
	}
}
