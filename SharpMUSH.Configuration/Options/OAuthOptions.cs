namespace SharpMUSH.Configuration.Options;

/// <summary>
/// Which apps may ask players for access without staff registering them first: apps whose <c>client_id</c> is an
/// https address serving a description of the app (OAuth Client ID Metadata Documents).
/// </summary>
public sealed record OAuthOptions
{
	/// <summary>Only apps staff register (or allowed by hand) may ask; a <c>client_id</c> address is never read.</summary>
	public const string Registered = "registered";

	/// <summary>An app that describes itself is listed for staff, and asks nobody until staff allow it.</summary>
	public const string Review = "review";

	/// <summary>An app that describes itself may ask at once; the consent page says staff have not reviewed it.</summary>
	public const string Open = "open";

	[SharpConfig(Name = "oauth_app_policy", Category = "OAuth",
		Description = "Apps that describe themselves at an https client_id address: open (accepted, marked as not reviewed), review (staff allow each first) or registered (not accepted)",
		Choices = [Open, Review, Registered])]
	public string AppPolicy { get; init; } = Open;

	[SharpConfig(Name = "oauth_blocked_domains", Category = "OAuth",
		Description = "Domains whose self-described apps are refused, each with its subdomains, such as example.com")]
	public string[] BlockedDomains { get; init; } = [];

	/// <summary>
	/// <paramref name="options"/>' <see cref="AppPolicy"/> as one of the three values. Unset options read as the default,
	/// <see cref="Open"/>; an unknown value reads as the safest, <see cref="Registered"/>.
	/// </summary>
	public static string PolicyOf(OAuthOptions? options) => (options ?? new()).AppPolicy?.Trim().ToLowerInvariant() switch
	{
		Review => Review,
		Open => Open,
		_ => Registered,
	};
}
