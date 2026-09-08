namespace SharpMUSH.Library.Definitions;

/// <summary>Resolves the configured storage provider and rejects unsupported values.</summary>
public static class DatabaseProviderResolver
{
	/// <summary>Returns Lightning for an unset value and otherwise requires a supported provider name.</summary>
	public static DatabaseProvider Resolve(string? value) => value?.Trim().ToLowerInvariant() switch
	{
		null or "" or "lightning" => DatabaseProvider.Lightning,
		"surrealdb" => DatabaseProvider.SurrealDB,
		_ => throw new InvalidOperationException(
			$"Unsupported SHARPMUSH_DATABASE_PROVIDER value '{value}'. Supported values are 'lightning' and 'surrealdb'.")
	};
}
