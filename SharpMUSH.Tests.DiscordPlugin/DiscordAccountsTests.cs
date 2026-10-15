using SharpMUSH.Tests;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Discord.Members;

namespace SharpMUSH.Tests.DiscordPlugin;

public class DiscordAccountsTests
{
	private static readonly DateTimeOffset Start = new(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);
	private static readonly DiscordUserCard Ann = new(1001, "ann", "https://cdn.example/ann.png");
	private static readonly DiscordUserCard Bob = new(1002, "bob", null);

	private sealed class Clock : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = Start;

		public override DateTimeOffset GetUtcNow() => Now;
	}

	private sealed class MemoryAccountStore : IDiscordAccountStore
	{
		private readonly ConcurrentDictionary<ulong, DiscordAccountLink> _links = new();
		private readonly ConcurrentDictionary<ulong, DiscordGuildRecord> _guilds = new();

		public IReadOnlyList<DiscordAccountLink> List() => [.. _links.Values];

		public DiscordAccountLink? Get(ulong userId) => _links.GetValueOrDefault(userId);

		public DiscordAccountLink? ForAccount(string accountId)
			=> _links.Values.FirstOrDefault(link => link.Linked && link.AccountId == accountId);

		public ValueTask SaveAsync(DiscordAccountLink link, CancellationToken cancellationToken = default)
		{
			_links[link.UserId] = link;
			return ValueTask.CompletedTask;
		}

		public DiscordGuildRecord? Guild(ulong guildId) => _guilds.GetValueOrDefault(guildId);

		public ValueTask SaveGuildAsync(DiscordGuildRecord guild, CancellationToken cancellationToken = default)
		{
			_guilds[guild.GuildId] = guild;
			return ValueTask.CompletedTask;
		}
	}

	private sealed class RecordingRole : IDiscordLinkedRole
	{
		public List<string> Changes { get; } = [];

		public Task GiveAsync(ulong userId, CancellationToken cancellationToken)
		{
			Changes.Add($"give {userId}");
			return Task.CompletedTask;
		}

		public Task TakeAsync(ulong userId, CancellationToken cancellationToken)
		{
			Changes.Add($"take {userId}");
			return Task.CompletedTask;
		}
	}

	private sealed class Game
	{
		public Clock Clock { get; } = new();
		public MemoryAccountStore Store { get; } = new();
		public RecordingRole Role { get; } = new();
		public MemorySpeakerStore Speakers { get; } = new();
		public IAccountService Accounts { get; } = Substitute.For<IAccountService>();
		public DiscordAccounts Discord { get; }

		public Game()
		{
			Discord = new DiscordAccounts(Store, Speakers, new DiscordLinkTickets(Clock), Accounts, Role, Clock,
				NullLogger<DiscordAccounts>.Instance);
		}

		public void Account(string id, AccountStatus status = AccountStatus.Active)
			=> Accounts.GetByIdAsync(id, Arg.Any<CancellationToken>())
				.Returns(new SharpAccount { Id = id, Username = id, PasswordHash = "", Status = status });

		public async Task<Result<DiscordAccountLink>> LinkAsync(DiscordUserCard user, string account)
			=> Discord.StartLink(user) switch
			{
				DiscordLinkTicket ticket => await Discord.ConfirmAsync(ticket.Id, account, CancellationToken.None),
				Error<string> error => error,
			};
	}

	[Test]
	public async Task ConfirmingATicket_LinksTheAccount_AndGivesTheLinkedRole()
	{
		var game = new Game();
		game.Account("acct-1");

		var link = (await game.LinkAsync(Ann, "acct-1")).Expect<DiscordAccountLink>();

		await Assert.That(link.AccountId).IsEqualTo("acct-1");
		await Assert.That(game.Discord.ForAccount("acct-1")?.Username).IsEqualTo("ann");
		await Assert.That(game.Discord.Find(Ann.UserId)).IsNotNull();
		await Assert.That(game.Role.Changes).IsEquivalentTo(["give 1001"]);
	}

	[Test]
	public async Task ATicket_WorksOnce()
	{
		var game = new Game();
		game.Account("acct-1");
		var ticket = game.Discord.StartLink(Ann).Expect<DiscordLinkTicket>();

		await game.Discord.ConfirmAsync(ticket.Id, "acct-1", CancellationToken.None);
		var again = await game.Discord.ConfirmAsync(ticket.Id, "acct-1", CancellationToken.None);

		await Assert.That(again.Expect<Error<string>>().Value).Contains("expired or was already used");
	}

	[Test]
	public async Task ATicket_ExpiresAfterTenMinutes()
	{
		var game = new Game();
		game.Account("acct-1");
		var ticket = game.Discord.StartLink(Ann).Expect<DiscordLinkTicket>();

		game.Clock.Now = Start + DiscordLinkTickets.Lifetime;

		await Assert.That(game.Discord.Ticket(ticket.Id)).IsNull();
		var confirmed = await game.Discord.ConfirmAsync(ticket.Id, "acct-1", CancellationToken.None);
		await Assert.That(confirmed.Expect<Error<string>>().Value).Contains("expired");
	}

	[Test]
	public async Task ANewLinkCommand_ReplacesTheMembersOldTicket()
	{
		var game = new Game();
		var first = game.Discord.StartLink(Ann).Expect<DiscordLinkTicket>();
		var second = game.Discord.StartLink(Ann).Expect<DiscordLinkTicket>();

		await Assert.That(game.Discord.Ticket(first.Id)).IsNull();
		await Assert.That(game.Discord.Ticket(second.Id)).IsNotNull();
	}

	[Test]
	public async Task AnInactiveAccount_CannotLink()
	{
		var game = new Game();
		game.Account("acct-1", AccountStatus.Disabled);

		var linked = await game.LinkAsync(Ann, "acct-1");

		await Assert.That(linked.Expect<Error<string>>().Value).IsEqualTo("Only an active account can link Discord.");
		await Assert.That(game.Role.Changes).IsEmpty();
	}

	[Test]
	public async Task ALinkedMember_IsToldToUnlinkFirst()
	{
		var game = new Game();
		game.Account("acct-1");
		await game.LinkAsync(Ann, "acct-1");

		var again = game.Discord.StartLink(Ann);

		await Assert.That(again.Expect<Error<string>>().Value).Contains("already linked");
	}

	[Test]
	public async Task AnAccount_HasOneDiscordUser()
	{
		var game = new Game();
		game.Account("acct-1");
		await game.LinkAsync(Ann, "acct-1");

		var second = await game.LinkAsync(Bob, "acct-1");

		await Assert.That(second.Expect<Error<string>>().Value)
			.IsEqualTo("This account is already linked to Discord as ann. Unlink it first.");
	}

	[Test]
	public async Task UnlinkingFromDiscord_DropsTheLink_TheRole_AndTheSpeakingIdentities()
	{
		var game = new Game();
		game.Account("acct-1");
		await game.LinkAsync(Ann, "acct-1");
		await game.Speakers.SetAsync(Ann.UserId, 555, "#5:1");

		var dropped = await game.Discord.UnlinkUserAsync(Ann.UserId, CancellationToken.None);

		await Assert.That(dropped.Expect<DiscordAccountLink>().AccountId).IsEqualTo("acct-1");
		await Assert.That(game.Discord.ForAccount("acct-1")).IsNull();
		await Assert.That(game.Role.Changes).IsEquivalentTo(["give 1001", "take 1001"]);
		await Assert.That(game.Speakers.Character(Ann.UserId, 555)).IsNull().Because("unlinking drops the speaking identities");
		await Assert.That(await game.Discord.UnlinkUserAsync(Ann.UserId, CancellationToken.None) is NotFound).IsTrue();
	}

	[Test]
	public async Task AnUnlinkedMember_WaitsAWeek_BeforeLinkingAnotherAccount()
	{
		var game = new Game();
		game.Account("acct-1");
		game.Account("acct-2");
		await game.LinkAsync(Ann, "acct-1");
		await game.Discord.UnlinkAccountAsync("acct-1", CancellationToken.None);

		game.Clock.Now = Start + TimeSpan.FromDays(6);
		var early = await game.LinkAsync(Ann, "acct-2");
		game.Clock.Now = Start + DiscordAccounts.RelinkCooldown;
		var later = await game.LinkAsync(Ann, "acct-2");

		await Assert.That(early.Expect<Error<string>>().Value).Contains("It can link again from 2026-10-22 03:00 UTC");
		await Assert.That(later.Expect<DiscordAccountLink>().AccountId).IsEqualTo("acct-2");
	}

	[Test]
	public async Task AnUnlinkedMember_MayLinkTheSameAccountAgainAtOnce()
	{
		var game = new Game();
		game.Account("acct-1");
		await game.LinkAsync(Ann, "acct-1");
		await game.Discord.UnlinkUserAsync(Ann.UserId, CancellationToken.None);

		var again = await game.LinkAsync(Ann, "acct-1");

		await Assert.That(again.Expect<DiscordAccountLink>().UnlinkedAt).IsNull();
	}

	[Test]
	public async Task WhileABanStands_ItsDiscordUserCannotLinkAnotherAccount()
	{
		var game = new Game();
		game.Account("acct-1");
		game.Account("acct-2");
		await game.LinkAsync(Ann, "acct-1");
		await game.Discord.UnlinkUserAsync(Ann.UserId, CancellationToken.None);
		game.Clock.Now = Start + DiscordAccounts.RelinkCooldown;
		game.Accounts.GetBanAsync("acct-1", Arg.Any<CancellationToken>())
			.Returns(new AccountBan("acct-1", "griefing", null, Start, null));

		var banned = await game.LinkAsync(Ann, "acct-2");
		game.Accounts.GetBanAsync("acct-1", Arg.Any<CancellationToken>()).Returns((AccountBan?)null);
		var lifted = await game.LinkAsync(Ann, "acct-2");

		await Assert.That(banned.Expect<Error<string>>().Value).IsEqualTo("That Discord account was linked to a game account that is banned.");
		await Assert.That(lifted.Expect<DiscordAccountLink>().AccountId).IsEqualTo("acct-2");
	}
}
