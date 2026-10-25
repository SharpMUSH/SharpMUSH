using MarkupString.Ansi;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// The connect screen is the first thing a telnet player reads. It advertised
/// <c>create &lt;name&gt; &lt;password&gt;</c>, which is not a command — typing it answers
/// "No such command available at login." — and said nothing at all about the account flow
/// (register / login), which is the only route to a character for someone who has never played here.
/// </summary>
public class ConnectBannerTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	/// <summary>The banner as shipped, without its colours.</summary>
	private string Banner => AnsiEscapeParser.Parse(WebAppFactoryArg.Services.GetRequiredService<IGameMessageService>()
		.ShippedText(GameMessage.Connect)).ToPlainText();

	/// <summary>Every command word the banner tells a player to type must exist pre-login.</summary>
	[Test]
	public async Task TheBannerOnlyAdvertisesCommandsThatExist()
	{
		var socketCommands = Parser.CommandLibrary
			.Where(x => x.Value.IsSystem
				&& x.Value.LibraryInformation.Attribute.Behavior.HasFlag(CommandBehavior.SOCKET))
			.Select(x => x.Key)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		foreach (var advertised in AdvertisedCommands(Banner))
		{
			await Assert.That(socketCommands).Contains(advertised);
		}
	}

	/// <summary>
	/// The banner offers the ways into an account (register, login), not create, play and claim, which work only
	/// once login has put the connection in account mode; the account menu lists those.
	/// </summary>
	[Test]
	public async Task TheBannerDocumentsTheAccountFlow()
	{
		var advertised = AdvertisedCommands(Banner);

		await Assert.That(advertised).Contains("register");
		await Assert.That(advertised).Contains("login");
		await Assert.That(advertised).Contains("connect");
		await Assert.That(advertised).Contains("WHO");
		await Assert.That(advertised).Contains("QUIT");
		await Assert.That(advertised).DoesNotContain("create");
		await Assert.That(advertised).DoesNotContain("play");
		await Assert.That(advertised).DoesNotContain("claim");
	}

	/// <summary>
	/// SCREENREADER and WHO come first, right under the welcome and above the logo, so a screen reader reaches them
	/// before anything else.
	/// </summary>
	[Test]
	public async Task TheBannerOffersTheScreenReaderAndWhoFirst()
	{
		var advertised = AdvertisedCommands(Banner);

		await Assert.That(advertised.Take(2)).IsEquivalentTo(["SCREENREADER", "WHO"]);
		await Assert.That(Banner.Split('\n')[1]).StartsWith("SCREENREADER");
	}

	/// <summary>The banner really is what a freshly opened socket receives.</summary>
	[Test]
	public async Task AFreshConnectionReceivesTheBanner()
	{
		var handle = await TestIsolationHelpers.RegisterTestHandleAsync(ConnectionService);

		var messages = ConnectScreenTests.NotificationsTo(WebAppFactoryArg, handle);

		await Assert.That(messages.Any(m => m.Contains("Welcome to SharpMUSH!"))).IsTrue();
		await Assert.That(messages.Any(m => Regex.IsMatch(m, @"login +<name-or-email> <password>"))).IsTrue();
	}

	/// <summary>
	/// Command words the banner instructs a player to type, in order: the first word of any line above the logo, or
	/// beside it (its 33 columns and the gap of two after them), that is not a section heading.
	/// </summary>
	private static string[] AdvertisedCommands(string banner) =>
		banner.Split('\n')
			.Select(line => line.Length > 35 && line[..35].All(c => c is ' ' or '=' or '-') ? line[35..].Trim() : line.Trim())
			.Where(text => text.Length > 0 && !text.EndsWith(':') && !text.EndsWith('!'))
			.Select(text => text.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0])
			.Where(word => Regex.IsMatch(word, "^[A-Za-z]+$"))
			.Distinct()
			.ToArray();
}
