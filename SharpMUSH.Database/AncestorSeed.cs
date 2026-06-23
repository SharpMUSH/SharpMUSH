using MModule = MarkupString.MarkupStringModule;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Database;

/// <summary>
/// Seeds the default <c>FORMAT`*</c> attributes on the Ancestor Player (#4) so a plain player inherits
/// PennMUSH-style say/pose/semipose/emit render templates even without a per-player override.
///
/// <para>The literals here mirror the built-in fallbacks consumed by the scene-capture package
/// (<c>2026-06-21-scene-capture-message-formats-design.md</c>): the format reads <c>%0</c> = the message
/// text and a recipient-dbref argument (<c>%1</c>) so it can render the speaker's "You…" form versus an
/// observer's "Name…" form. <c>%#</c> is the speaker (enactor).</para>
///
/// <para>Run once at the tail of each provider's migration via <see cref="ISharpDatabase.SetAttributeAsync"/>
/// — a single provider-agnostic code path so all three backends seed byte-identical attribute values.
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

	/// <summary>
	/// Seed the FORMAT attributes on the Ancestor Player. No-op if God (#1) or the Ancestor Player (#4)
	/// is missing (defensive — both are created earlier in the same migration).
	/// </summary>
	public static async ValueTask SeedAncestorPlayerFormatsAsync(ISharpDatabase database,
		CancellationToken ct = default)
	{
		var godNode = await database.GetObjectNodeAsync(God, ct);
		if (godNode.IsNone)
		{
			return;
		}

		var owner = godNode.Match(
			player => player,
			_ => null!,
			_ => null!,
			_ => null!,
			_ => null!);

		if (owner is null || string.IsNullOrEmpty(owner.Id))
		{
			return;
		}

		foreach (var (path, value) in Formats)
		{
			await database.SetAttributeAsync(AncestorPlayer, path, MModule.single(value), owner, ct);
		}
	}
}
