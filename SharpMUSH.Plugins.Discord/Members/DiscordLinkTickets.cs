using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace SharpMUSH.Plugins.Discord.Members;

/// <summary>A Discord user as the portal shows them when they confirm a link.</summary>
public sealed record DiscordUserCard(ulong UserId, string Username, string? AvatarUrl);

/// <summary>What <c>/link</c> in Discord hands the member: proof of who ran it, good once until it expires.</summary>
public sealed record DiscordLinkTicket(string Id, DiscordUserCard User, DateTimeOffset ExpiresAt);

/// <summary>
/// The tickets <c>/link</c> mints, held in memory: one that is lost to a restart costs the member a second
/// <c>/link</c>. A Discord user has one ticket at a time; a new <c>/link</c> replaces the old one.
/// </summary>
public sealed class DiscordLinkTickets(TimeProvider time)
{
	public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

	private readonly ConcurrentDictionary<string, DiscordLinkTicket> _tickets = new(StringComparer.Ordinal);

	public DiscordLinkTicket Mint(DiscordUserCard user)
	{
		Prune();
		foreach (var old in _tickets.Values.Where(ticket => ticket.User.UserId == user.UserId))
		{
			_tickets.TryRemove(old.Id, out _);
		}

		var ticket = new DiscordLinkTicket(Base64Url(RandomNumberGenerator.GetBytes(24)), user, time.GetUtcNow() + Lifetime);
		_tickets[ticket.Id] = ticket;
		return ticket;
	}

	/// <summary>The ticket, while it is unused and young enough.</summary>
	public DiscordLinkTicket? Find(string id)
		=> _tickets.TryGetValue(id, out var ticket) && ticket.ExpiresAt > time.GetUtcNow() ? ticket : null;

	/// <summary>Spends the ticket. Only one caller of several at once gets true.</summary>
	public bool Spend(DiscordLinkTicket ticket) => _tickets.TryRemove(new KeyValuePair<string, DiscordLinkTicket>(ticket.Id, ticket));

	private void Prune()
	{
		var now = time.GetUtcNow();
		foreach (var expired in _tickets.Values.Where(ticket => ticket.ExpiresAt <= now))
		{
			_tickets.TryRemove(expired.Id, out _);
		}
	}

	private static string Base64Url(byte[] bytes)
		=> Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
