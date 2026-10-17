using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Discord.Links;
using SharpMUSH.Plugins.Discord.Members;

namespace SharpMUSH.Tests.DiscordPlugin;

public class DiscordIdentitiesTests
{
	private const ulong Channel = 4242;
	private const ulong Ann = 1001;
	private static readonly DateTimeOffset Now = new(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);
	private static readonly DiscordCharacter Annabel = new(new DBRef(5, 1), "Annabel", IsPlayer: true);

	private sealed class Game
	{
		public IDiscordLinkStore Links { get; } = Substitute.For<IDiscordLinkStore>();
		public IDiscordAccountStore Store { get; } = Substitute.For<IDiscordAccountStore>();
		public IAccountService Accounts { get; } = Substitute.For<IAccountService>();
		public MemorySpeakerStore Speakers { get; } = new();
		public DiscordIdentities Identities { get; }

		public Game()
		{
			Links.Get(Channel).Returns(new DiscordChannelLink(Channel, 900, 1, "general", "Guild", Now, "#1:1"));
			var discord = new DiscordAccounts(Store, Speakers, new DiscordLinkTickets(TimeProvider.System), Accounts,
				Substitute.For<IDiscordLinkedRole>(), TimeProvider.System, NullLogger<DiscordAccounts>.Instance);
			Identities = new DiscordIdentities(Links, discord, Speakers, Accounts);
		}

		public void OnAccount(DiscordCharacter character, string account)
			=> Accounts.GetAccountForCharacterAsync(character.Objid, Arg.Any<CancellationToken>())
				.Returns(new SharpAccount { Id = account, Username = account, PasswordHash = "" });

		public void Linked(string account)
		{
			var user = new DiscordAccountLink(Ann, account, "ann", Now);
			Store.ForAccount(account).Returns(user); Store.Get(Ann).Returns(user);
		}

		public Task<Result<string>> SpeakAsync(DiscordCharacter character, ulong channel = Channel, bool maySend = true)
			=> Identities.SpeakAsync(character, channel, () => Task.FromResult(maySend), CancellationToken.None);
	}

	[Test]
	public async Task DiscordChoiceCannotSelectSomebodyElsesCharacter()
	{
		var game = new Game();
		game.Store.Get(Ann).Returns(new DiscordAccountLink(Ann, "mine", "ann", Now));
		game.Accounts.GetByIdAsync("mine", Arg.Any<CancellationToken>()).Returns(new SharpAccount { Id = "mine", Username = "mine", PasswordHash = "" });
		game.Accounts.GetCharactersAsync("mine", Arg.Any<CancellationToken>()).Returns(Array.Empty<SharpPlayer>());
		var result = await game.Identities.SpeakAsAsync(Ann, Channel, "AnotherPlayer", _ => Task.FromResult(true), default);
		await Assert.That(result is Error<string>).IsTrue();
		await Assert.That(game.Speakers.Character(Ann, Channel)).IsNull();
	}
	[Test]
	public async Task DiscordChoiceRejectsInactiveAccountBeforeConsultingRoster()
	{
		var game = new Game();
		game.Store.Get(Ann).Returns(new DiscordAccountLink(Ann, "mine", "ann", Now));
		game.Accounts.GetByIdAsync("mine", Arg.Any<CancellationToken>()).Returns(new SharpAccount { Id = "mine", Username = "mine", PasswordHash = "", Status = AccountStatus.Disabled });
		await Assert.That(await game.Identities.SpeakAsAsync(Ann, Channel, "Annabel", _ => Task.FromResult(true), default) is Error<string>).IsTrue();
		await game.Accounts.DidNotReceive().GetCharactersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task ACharacterOnALinkedAccount_BecomesItsUsersIdentity()
	{
		var game = new Game();
		game.OnAccount(Annabel, "acct-1");
		game.Linked("acct-1");

		var said = (await game.SpeakAsync(Annabel)).Expect<string>();

		await Assert.That(game.Speakers.Character(Ann, Channel)).IsEqualTo("#5:1");
		await Assert.That(said).Contains("Anyone who reads both can tell that ann plays Annabel.");
	}

	[Test]
	public async Task AnotherCharacter_ReplacesTheFirst()
	{
		var game = new Game();
		var bea = new DiscordCharacter(new DBRef(6, 1), "Bea", IsPlayer: true);
		game.OnAccount(Annabel, "acct-1");
		game.OnAccount(bea, "acct-1");
		game.Linked("acct-1");

		await game.SpeakAsync(Annabel);
		await game.SpeakAsync(bea);

		await Assert.That(game.Speakers.Character(Ann, Channel)).IsEqualTo("#6:1");
		await Assert.That((await game.Identities.StopAsync(Annabel, Channel, CancellationToken.None)).Expect<Error<string>>().Value)
			.IsEqualTo("Annabel does not speak in #general.");
		await game.Identities.StopAsync(bea, Channel, CancellationToken.None);
		await Assert.That(game.Speakers.Character(Ann, Channel)).IsNull();
	}

	[Test]
	public async Task Refusals_SayWhy()
	{
		var game = new Game();
		var thing = new DiscordCharacter(new DBRef(9, 1), "Lamp", IsPlayer: false);
		game.OnAccount(Annabel, "acct-1");

		async Task<string> Why(Task<Result<string>> attempt) => (await attempt).Expect<Error<string>>().Value;

		await Assert.That(await Why(game.SpeakAsync(thing))).IsEqualTo("Only a character speaks in Discord.");
		await Assert.That(await Why(game.SpeakAsync(Annabel, channel: 99))).IsEqualTo("That channel is not linked.");
		await Assert.That(await Why(game.SpeakAsync(new DiscordCharacter(new DBRef(7, 1), "Stray", true))))
			.IsEqualTo("Stray is not on a game account, so it cannot speak in Discord.");
		await Assert.That(await Why(game.SpeakAsync(Annabel)))
			.IsEqualTo("Your game account is not linked to Discord. Type /link in Discord to link it.");
		game.Linked("acct-1");
		await Assert.That(await Why(game.SpeakAsync(Annabel, maySend: false)))
			.IsEqualTo("Annabel may not send to discord/4242, so it cannot speak there.");
		await Assert.That(game.Speakers.Character(Ann, Channel)).IsNull();
	}
}
