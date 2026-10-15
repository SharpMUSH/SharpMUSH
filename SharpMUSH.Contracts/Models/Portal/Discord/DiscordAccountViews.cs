namespace SharpMUSH.Library.Models.Portal.Discord;

/// <summary>A <c>/link</c> ticket as the portal shows it before the member confirms (<c>GET api/discord/link/{ticket}</c>).</summary>
/// <param name="Username">The Discord user who ran <c>/link</c>.</param>
/// <param name="AvatarUrl">Their Discord picture.</param>
/// <param name="ExpiresAt">When the ticket stops working.</param>
public sealed record DiscordLinkTicketView(string Username, string? AvatarUrl, DateTimeOffset ExpiresAt);

/// <summary>The signed-in account's Discord link (<c>GET api/discord/account</c>).</summary>
/// <param name="Username">The linked Discord user, or null when none is.</param>
/// <param name="LinkedAt">When it was linked.</param>
public sealed record DiscordAccountView(string? Username, DateTimeOffset? LinkedAt)
{
	public bool Linked => Username is not null;
}
