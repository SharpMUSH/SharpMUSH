namespace SharpMUSH.Library.Models;

/// <summary>
/// Configuration for first-run admin account bootstrapping.
/// Reads from the "Bootstrap" section in appsettings.json or env vars
/// (SHARPMUSH_BOOTSTRAP_USERNAME, SHARPMUSH_BOOTSTRAP_PASSWORD).
/// </summary>
public class BootstrapOptions
{
	public const string Section = "Bootstrap";

	/// <summary>Username for the initial admin account. Defaults to "admin".</summary>
	public string AdminUsername { get; set; } = "admin";

	/// <summary>
	/// Password for the initial admin account. If null/empty, a random password is
	/// generated and printed to the startup log.
	/// </summary>
	public string? AdminPassword { get; set; }
}
