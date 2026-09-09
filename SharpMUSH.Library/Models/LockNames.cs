using System.Collections.Frozen;
using System.Collections.Immutable;

namespace SharpMUSH.Library.Models;

/// <summary>
/// The one place a standard lock's name is spelled. <see cref="LockType"/> is the source of truth —
/// it is what every gate looks up (<c>LockService.Get(LockType.ChZone, ...)</c> reads
/// <c>Locks["ChZone"]</c>) and what <c>@list/locks</c> already shows players — so every other
/// spelling a name can arrive in resolves to the enum member's name here.
/// </summary>
/// <remarks>
/// Names arrive spelled three ways: from an <c>@lock</c>/<c>@unlock</c> switch (whatever case the
/// player typed), from a PennMUSH world import (<c>PennMUSHDatabaseConverter</c> writes the lock
/// names verbatim out of the foreign database), and from a package manifest. PennMUSH spells three
/// of them differently only in case — <c>Chzone</c>, <c>Dropto</c>, <c>Chown</c> — and one as a
/// different word, <c>Teleport</c> for <see cref="LockType.TPort"/>, which is why
/// <see cref="Aliases"/> exists and case-insensitivity alone is not enough.
/// <para>
/// Lock names that are not standard locks (a user lock, say) pass through unchanged; they are still
/// matched case-insensitively, as PennMUSH matches lock names.
/// </para>
/// </remarks>
public static class LockNames
{
	/// <summary>
	/// The comparer every lock dictionary must use — the loaded <see cref="SharpObject.Locks"/>
	/// model and each provider's persisted record alike. MUSH lock names are case-insensitive, so
	/// <c>@lock/CHZONE</c> and <c>@lock/chzone</c> have to name one lock, not two.
	/// </summary>
	public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

	/// <summary>Spellings that are not just a case variation of a <see cref="LockType"/> member.</summary>
	private static readonly (string Alias, LockType Type)[] Aliases =
	[
		("Teleport", LockType.TPort)
	];

	private static readonly FrozenDictionary<string, string> CanonicalByName =
		Enum.GetNames<LockType>()
			.Select(name => KeyValuePair.Create(name, name))
			.Concat(Aliases.Select(alias => KeyValuePair.Create(alias.Alias, alias.Type.ToString())))
			.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// The <see cref="LockType"/> spelling of <paramref name="name"/>, or <paramref name="name"/>
	/// unchanged when it names no standard lock.
	/// </summary>
	public static string Canonical(string name)
		=> CanonicalByName.GetValueOrDefault(name, name);

	/// <summary>
	/// Canonicalises every key and collapses the entries that then collide, so the result can be
	/// handed to a <see cref="Comparer"/>-keyed dictionary without throwing on a duplicate key.
	/// </summary>
	/// <remarks>
	/// A world written before lock names were canonicalised can carry two entries for one lock on
	/// one object: <c>@CHZONE</c> wrote its default under <c>ChZone</c> while <c>@lock/chzone</c>
	/// wrote the player's under <c>Chzone</c>. Only the canonical one was ever visible to the gate,
	/// so <b>the canonically-spelled entry wins</b> and any other spelling of the same lock is
	/// dropped: loading such a world must not change a permission decision it is already enforcing.
	/// With no canonical entry present the ordinally-first key wins, which is arbitrary but
	/// deterministic — dictionary enumeration order is not.
	/// </remarks>
	public static Dictionary<string, TValue> Fold<TValue>(
		IReadOnlyCollection<KeyValuePair<string, TValue>> locks)
	{
		var folded = new Dictionary<string, TValue>(locks.Count, Comparer);
		if (locks.Count == 0) return folded;

		// Ordered so the winner among two non-canonical spellings does not depend on hash order.
		foreach (var (name, value) in locks.OrderBy(pair => pair.Key, StringComparer.Ordinal))
		{
			var canonical = Canonical(name);
			// An exactly-canonical key displaces whatever landed first; anything else defers.
			if (folded.ContainsKey(canonical) && !string.Equals(name, canonical, StringComparison.Ordinal))
			{
				continue;
			}

			folded[canonical] = value;
		}

		return folded;
	}

	/// <inheritdoc cref="Fold{TValue}"/>
	public static ImmutableDictionary<string, TOut> FoldToImmutable<TIn, TOut>(
		IReadOnlyCollection<KeyValuePair<string, TIn>> locks,
		Func<TIn, TOut> selector)
	{
		if (locks.Count == 0) return ImmutableDictionary<string, TOut>.Empty.WithComparers(Comparer);

		var folded = Fold(locks);
		var builder = ImmutableDictionary.CreateBuilder<string, TOut>(Comparer);
		foreach (var (name, value) in folded)
		{
			builder[name] = selector(value);
		}

		return builder.ToImmutable();
	}
}
