using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Discord.Members;

namespace SharpMUSH.Tests.DiscordPlugin;

public class DiscordSlashCommandsTests
{
	private static readonly DiscordUserCard Ann = new(1001, "ann", null);

	private static DiscordSlashCommands Commands(string? site)
	{
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		var defaults = SharpMUSHOptions.Default();
		options.CurrentValue.Returns(defaults with { Net = defaults.Net with { MudUrl = site } });
		var accounts = new DiscordAccounts(Substitute.For<IDiscordAccountStore>(), new DiscordLinkTickets(TimeProvider.System),
			Substitute.For<IAccountService>(), Substitute.For<IDiscordLinkedRole>(), TimeProvider.System,
			NullLogger<DiscordAccounts>.Instance);
		return new DiscordSlashCommands(accounts, options, NullLogger<DiscordSlashCommands>.Instance);
	}

	[Test]
	public async Task Link_AnswersWithTheGamesConfirmPage()
	{
		var answer = Commands("https://game.example/").LinkAnswer(Ann);

		await Assert.That(answer).Contains("https://game.example/account/discord/link?ticket=");
		await Assert.That(answer).Contains("It works once, for 10 minutes");
	}

	[Test]
	public async Task Link_WithoutAWebAddress_SaysWhatStaffMustSet()
	{
		var answer = Commands(null).LinkAnswer(Ann);

		await Assert.That(answer).IsEqualTo("This game has no web address set, so linking cannot finish. Ask the game's staff to set mud_url.");
	}
}
