namespace SharpMUSH.Library.Models;

public readonly struct DBRef : IEquatable<DBRef>
{
	public DBRef(int number) => Number = number;

	public DBRef(int number, long? milliseconds)
	{
		Number = number;
		CreationMilliseconds = milliseconds;
	}

	public int Number { get; init; }
	public long? CreationMilliseconds { get; init; }

	/// <summary>
	/// Whether this is a full objid (<c>#N:creation</c>) rather than a bare dbref (<c>#N</c>).
	/// </summary>
	/// <remarks>
	/// Required wherever a reference is used as an identity <em>key</em> — a SignalR group name, a
	/// cache key — because <c>#N</c> and <c>#N:creation</c> are necessarily different keys, so a
	/// producer and consumer that disagree on completeness miss each other silently. Being a
	/// <see cref="DBRef"/> stops the two from being spelled differently; it does not stop one side
	/// from lacking the timestamp, which this exists to catch.
	/// </remarks>
	public bool IsObjid => CreationMilliseconds.HasValue;

	public override bool Equals(object? obj) => obj is DBRef @ref && Equals(@ref);

	public bool Equals(DBRef other)
		=> Number == other.Number && CreationMilliseconds == other.CreationMilliseconds;

	/// <summary>
	/// Checks whether this DBRef matches a search DBRef.
	/// If the search DBRef has no creation timestamp (bare dbref), only the number is compared.
	/// If the search DBRef has a creation timestamp (objid), both number and timestamp must match.
	/// </summary>
	public bool Matches(DBRef search)
		=> Number == search.Number &&
			(!search.CreationMilliseconds.HasValue || CreationMilliseconds == search.CreationMilliseconds);

	/// <summary>
	/// Whether this and <paramref name="other"/> name the same object, tolerating either side being a
	/// bare dbref: the numbers must always agree, and the creation stamps only when BOTH carry one.
	/// </summary>
	/// <remarks>
	/// <para>Unlike <see cref="Matches"/> this is symmetric. <c>Matches</c> is search-shaped — it treats
	/// its argument as the query and this instance as the candidate — so for one bare and one stamped
	/// reference it answers differently depending on which way round it is called, which is never what an
	/// equality test between two independently-produced references wants.</para>
	/// <para>Only sound when both sides were resolved from the LIVE object in the same request (a graph
	/// edge dereferenced to its object vertex, a claim minted from the session's current character): two
	/// live objects cannot share a dbref number, so the number alone is exact and the stamp is a bonus.
	/// A <em>stored</em> reference — a persisted dbref string that can outlive the object it names — must
	/// keep comparing full objids (see <see cref="IsObjid"/>), since a recycled number would otherwise
	/// match the wrong object.</para>
	/// </remarks>
	public bool SameObjectAs(DBRef other)
		=> Number == other.Number
			&& (CreationMilliseconds is null || other.CreationMilliseconds is null
				|| CreationMilliseconds == other.CreationMilliseconds);

	public override int GetHashCode()
		=> HashCode.Combine(Number, CreationMilliseconds);

	public override string ToString()
		=> CreationMilliseconds is null
			? $"#{Number}"
			: $"#{Number}:{CreationMilliseconds}";

	public static bool operator ==(DBRef left, DBRef right) => left.Equals(right);

	public static bool operator !=(DBRef left, DBRef right) => !(left == right);

	public static bool TryParse(string? value, out DBRef? dbref)
	{
		// Null and blank return false rather than throwing, matching every framework TryParse.
		// This is the inbound boundary for untrusted text — JSON payloads and SignalR arguments
		// deserialize to null despite non-nullable declarations, since nullable reference types
		// are not enforced at runtime — so callers must be able to rely on a bool, not a catch.
		if (string.IsNullOrWhiteSpace(value))
		{
			dbref = default;
			return false;
		}

		var parsed = HelperFunctions.ParseDbRef(value);

		// Cast the null arm explicitly: with `: default` the ternary's natural type is DBRef, so a
		// failed parse yielded default(DBRef) — #0 — which then widened to DBRef?. The signature
		// promised null and never produced it, making `out var` look safe when it was #0.
		dbref = parsed.IsSome() ? parsed.AsValue() : (DBRef?)null;
		return parsed.IsSome();
	}

	public static DBRef Parse(string value)
		=> HelperFunctions.ParseDbRef(value).AsValue();
}