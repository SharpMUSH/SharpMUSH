using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Discord.Members;

namespace SharpMUSH.Tests.DiscordPlugin;

public class DiscordSlashCommandsTests
{
	private static readonly DiscordUserCard Ann = new(1001, "ann", null);

	private static readonly DateTimeOffset Installed = new(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);

	private static DiscordSlashCommands Commands(string? site, bool installed = true)
	{
		var registry = Substitute.For<IPackageRegistryService>();
		Found<InstalledPackageRecord> found = installed
			? new InstalledPackageRecord(DiscordSlashCommands.PackageId, "1.0.0", "bundled", null, "bundled", null, Installed, 1)
			: new NotFound();
		registry.GetInstalledPackageAsync(DiscordSlashCommands.PackageId).Returns(found);
		var services = Substitute.For<IServiceProvider>();
		services.GetService(typeof(IPackageRegistryService)).Returns(registry);

		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		var defaults = SharpMUSHOptions.Default();
		options.CurrentValue.Returns(defaults with { Net = defaults.Net with { MudUrl = site } });
		var accounts = new DiscordAccounts(Substitute.For<IDiscordAccountStore>(), Substitute.For<IDiscordSpeakerStore>(), new DiscordLinkTickets(TimeProvider.System),
			Substitute.For<IAccountService>(), Substitute.For<IDiscordLinkedRole>(), TimeProvider.System,
			NullLogger<DiscordAccounts>.Instance);
		return new DiscordSlashCommands(accounts, options, services, NullLogger<DiscordSlashCommands>.Instance);
	}

	[Test]
	public async Task SpeakAsIsDiscoverableAndRequiresACharacter()
	{
		var command = DiscordSlashCommands.Commands.OfType<Discord.SlashCommandProperties>().Single(command => command.Name.Value == "speakas");
		await Assert.That(command.Options.Value.Single().Name).IsEqualTo("character");
		await Assert.That(command.Options.Value.Single().IsRequired).IsTrue();
	}

	[Test]
	public async Task Link_AnswersWithTheGamesConfirmPage()
	{
		var answer = await Commands("https://game.example/").LinkAnswerAsync(Ann);

		await Assert.That(answer).Contains("https://game.example/account/discord/link?ticket=");
		await Assert.That(answer).Contains("It works once, for 10 minutes");
	}

	[Test]
	[Arguments(null)]
	[Arguments("   ")]
	[Arguments("game.example")]
	[Arguments("telnet://game.example:4201")]
	public async Task Link_WithoutAWebAddress_SaysWhatStaffMustSet(string? site)
	{
		var answer = await Commands(site).LinkAnswerAsync(Ann);

		await Assert.That(answer).IsEqualTo("This game has no web address set, so linking cannot finish. Ask the game's staff to set mud_url to the website's address.");
	}

	/// <summary>The portal's confirm page is gone with the package, so a ticket would lead nowhere.</summary>
	[Test]
	public async Task Link_WithTheDiscordPackageRemoved_SaysLinkingIsOff()
	{
		var answer = await Commands("https://game.example/", installed: false).LinkAnswerAsync(Ann);

		await Assert.That(answer).IsEqualTo("Linking is turned off on this game.");
	}
}
