namespace SharpMUSH.Database.Lightning.Records;

/// <summary>
/// The value stored under an object's <c>Tables.Obj</c> row. Mirrors
/// <c>SharpMUSH.Database.Models.SharpObjectQueryResult</c> / <c>SharpObjectCreateRequest</c>; the
/// object's own dbref is the LMDB key, not a field here.
/// </summary>
public sealed record ObjectRecord
{
	public string Name { get; init; } = "";
	public string Type { get; init; } = "";
	public string[] Aliases { get; init; } = [];
	public long CreationTime { get; init; }
	public long ModifiedTime { get; init; }
	public string? PasswordHash { get; init; }
	public string? PasswordSalt { get; init; }
	public long Quota { get; init; }
	public string? Warnings { get; init; }
	public Dictionary<string, LockRecord> Locks { get; init; } = new();

	// A record's synthesized Equals compares Aliases and Locks by reference (arrays don't override
	// Equals, and Dictionary<TKey,TValue> doesn't either), so two deserialized copies with identical
	// contents would never compare equal. Both are compared structurally here instead: Aliases by
	// sequence, Locks by same key set with equal (order-independent) LockRecord values.
	public bool Equals(ObjectRecord? other) =>
		other is not null
		&& Name == other.Name
		&& Type == other.Type
		&& Aliases.SequenceEqual(other.Aliases)
		&& CreationTime == other.CreationTime
		&& ModifiedTime == other.ModifiedTime
		&& PasswordHash == other.PasswordHash
		&& PasswordSalt == other.PasswordSalt
		&& Quota == other.Quota
		&& Warnings == other.Warnings
		&& LocksEqual(other.Locks);

	private bool LocksEqual(Dictionary<string, LockRecord> other) =>
		Locks.Count == other.Count
		&& Locks.All(pair => other.TryGetValue(pair.Key, out var value) && pair.Value == value);

	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add(Name);
		hash.Add(Type);
		foreach (var alias in Aliases) hash.Add(alias);
		hash.Add(CreationTime);
		hash.Add(ModifiedTime);
		hash.Add(PasswordHash);
		hash.Add(PasswordSalt);
		hash.Add(Quota);
		hash.Add(Warnings);
		hash.Add(Locks.Count);
		var locksHash = 0;
		foreach (var pair in Locks) locksHash ^= HashCode.Combine(pair.Key, pair.Value);
		hash.Add(locksHash);
		return hash.ToHashCode();
	}
}

/// <summary>Mirrors <c>SharpMUSH.Database.Models.SharpLockDataQueryResult</c>.</summary>
public sealed record LockRecord
{
	public string LockString { get; init; } = "#TRUE";
	public string Flags { get; init; } = "";
}
