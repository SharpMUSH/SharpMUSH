namespace SharpMUSH.Database.Lightning.Store;

/// <summary>Managed names must preserve the identity passed to LMDB's NUL-terminated name API.</summary>
internal static class NativeTableName
{
	internal static bool IsValid(string? name) => name is not null && !name.Contains('\0');
	internal static void Validate(string name)
	{
		ArgumentNullException.ThrowIfNull(name);
		if (!IsValid(name)) throw new ArgumentException("Native table names cannot contain NUL characters.", nameof(name));
	}
}
