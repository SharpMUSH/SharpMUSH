namespace SharpMUSH.Library.Definitions;

/// <summary>
/// Validates <c>SHARPMUSH_DATABASE_PROVIDER</c>. Lightning is the only storage provider, so the setting
/// selects nothing; it is still read so that a deployment naming another provider fails at startup
/// instead of silently opening an empty Lightning world beside its real data.
/// </summary>
public static class DatabaseProviderSetting
{
	/// <summary>Accepts an unset value or <c>lightning</c>, and throws for anything else.</summary>
	public static void EnsureSupported(string? value)
	{
		switch (value?.Trim().ToLowerInvariant())
		{
			case null or "" or "lightning":
				return;
			case "surrealdb":
				throw new InvalidOperationException(
					"SHARPMUSH_DATABASE_PROVIDER is 'surrealdb', which is no longer supported. Lightning is the only "
					+ "storage provider; there is no in-place migration, so bring the world across through a "
					+ "PennMUSH flatfile import and unset SHARPMUSH_DATABASE_PROVIDER or set it to 'lightning'.");
			default:
				throw new InvalidOperationException(
					$"Unsupported SHARPMUSH_DATABASE_PROVIDER value '{value}'. The only supported value is 'lightning'.");
		}
	}
}
