namespace SharpMUSH.Server.Authentication;

/// <summary>
/// Configuration options for JWT issuance and validation.
/// Bound from the <c>Jwt</c> section of <c>appsettings.json</c>.
/// </summary>
public class JwtOptions
{
	/// <summary>Configuration section name.</summary>
	public const string Section = "Jwt";

	/// <summary>
	/// HMAC-SHA256 signing key (plain text; minimum 32 characters recommended for HS256).
	/// When absent JWT features are disabled and <c>DebugAuthenticationHandler</c>
	/// remains active in development.
	/// </summary>
	public required string SigningKey { get; set; }

	/// <summary>Optional <c>iss</c> claim value.  Leave null to omit issuer validation.</summary>
	public string? Issuer { get; set; }

	/// <summary>Optional <c>aud</c> claim value.  Leave null to omit audience validation.</summary>
	public string? Audience { get; set; }

	/// <summary>Lifetime of issued access tokens, in minutes.  Default: 15.</summary>
	public int AccessTokenLifetimeMinutes { get; set; } = 15;

	/// <summary>Lifetime of issued refresh tokens, in days.  Default: 7.</summary>
	public int RefreshTokenLifetimeDays { get; set; } = 7;
}
