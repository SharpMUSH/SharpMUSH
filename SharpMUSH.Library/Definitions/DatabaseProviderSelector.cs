namespace SharpMUSH.Library.Definitions;

public static class DatabaseProviderSelector
{
	public const string EnvironmentVariableName = "SHARPMUSH_DATABASE_PROVIDER";
	public const string ConfigurationKey = "SharpMUSH:DatabaseProvider";

	public static DatabaseProvider ResolveOrDefault(string? provider, DatabaseProvider fallback = DatabaseProvider.ArangoDB)
		=> string.IsNullOrWhiteSpace(provider)
			? fallback
			: TryResolve(provider, out var resolved)
				? resolved
				: throw new InvalidOperationException(
					$"Unsupported database provider '{provider}'. Supported values: arangodb, memgraph, surrealdb, loradb.");

	public static bool TryResolve(string? provider, out DatabaseProvider resolved)
	{
		if (string.IsNullOrWhiteSpace(provider))
		{
			resolved = default;
			return false;
		}

		switch (provider.Trim().ToLowerInvariant())
		{
			case "arangodb":
			case "arango":
				resolved = DatabaseProvider.ArangoDB;
				return true;
			case "memgraph":
				resolved = DatabaseProvider.Memgraph;
				return true;
			case "surrealdb":
			case "surreal":
				resolved = DatabaseProvider.SurrealDB;
				return true;
			case "loradb":
			case "lora":
				resolved = DatabaseProvider.LoraDB;
				return true;
			default:
				resolved = default;
				return false;
		}
	}
}
