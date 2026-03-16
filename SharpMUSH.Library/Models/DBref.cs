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

	public override int GetHashCode()
		=> HashCode.Combine(Number, CreationMilliseconds);

	public override string ToString()
		=> CreationMilliseconds is null
			? $"#{Number}"
			: $"#{Number}:{CreationMilliseconds}";

	public static bool operator ==(DBRef left, DBRef right) => left.Equals(right);

	public static bool operator !=(DBRef left, DBRef right) => !(left == right);

	public static bool TryParse(string value, out DBRef? dbref)
	{
		var parsed = HelperFunctions.ParseDbRef(value);

		dbref = parsed.IsSome() ? parsed.AsValue() : default;
		return parsed.IsSome();
	}

	public static DBRef Parse(string value)
		=> HelperFunctions.ParseDbRef(value).AsValue();
}