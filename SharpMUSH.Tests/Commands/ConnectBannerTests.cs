using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Text;
using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// connect.txt is the first thing a telnet player reads. It advertised
/// <c>create &lt;name&gt; &lt;password&gt;</c>, which is not a command — typing it answers
/// "No such command available at login." — and said nothing at all about the account flow
/// (register / login / make / play), which is the only route to a character for someone who has
/// never played here.
/// </summary>
public class ConnectBannerTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	/// <summary>The banner as shipped, copied into the test output by the csproj as <c>txt/</c>.</summary>
	private static string BannerPath => Path.Combine(AppContext.BaseDirectory, "txt", "connect.txt");

	/// <summary>Every command word the banner tells a player to type must exist pre-login.</summary>
	[Test]
	public async Task TheBannerOnlyAdvertisesCommandsThatExist()
	{
		var socketCommands = Parser.CommandLibrary
			.Where(x => x.Value.IsSystem
				&& x.Value.LibraryInformation.Attribute.Behavior.HasFlag(CommandBehavior.SOCKET))
			.Select(x => x.Key)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		foreach (var advertised in AdvertisedCommands(await File.ReadAllTextAsync(BannerPath)))
		{
			await Assert.That(socketCommands).Contains(advertised);
		}
	}

	/// <summary>
	/// The account flow is the telnet mirror of the portal's register / login / create-character.
	/// </summary>
	[Test]
	public async Task TheBannerDocumentsTheAccountFlow()
	{
		var advertised = AdvertisedCommands(await File.ReadAllTextAsync(BannerPath));

		await Assert.That(advertised).Contains("register");
		await Assert.That(advertised).Contains("login");
		await Assert.That(advertised).Contains("make");
		await Assert.That(advertised).Contains("play");
		await Assert.That(advertised).Contains("connect");
		await Assert.That(advertised).Contains("WHO");
		await Assert.That(advertised).Contains("QUIT");
	}

	/// <summary>The banner really is what a freshly opened socket receives.</summary>
	[Test]
	public async Task AFreshConnectionReceivesTheBanner()
	{
		var handle = Random.Shared.NextInt64(600_000, 699_999);
		await ConnectionService.Register(handle, "localhost", "localhost", "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);

		var messages = ConnectScreenTests.NotificationsTo(WebAppFactoryArg, handle);

		await Assert.That(messages.Any(m => m.Contains("Welcome to SharpMUSH!"))).IsTrue();
		await Assert.That(messages.Any(m => m.Contains("play <character>"))).IsTrue();
	}

	/// <summary>
	/// Command words the banner instructs a player to type: the first word of any line that is
	/// indented past the ASCII art and is not a section heading.
	/// </summary>
	private static string[] AdvertisedCommands(string banner) =>
		banner.Split('\n')
			.Select(line => line.Length > 37 ? line[37..].Trim() : string.Empty)
			.Where(text => text.Length > 0 && !text.EndsWith(':') && !text.EndsWith('!'))
			.Select(text => text.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0])
			.Where(word => Regex.IsMatch(word, "^[A-Za-z]+$"))
			.Distinct()
			.ToArray();
}
