using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class NetworkCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private OwnRoomPlayers Players => new(WebAppFactoryArg);
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	/// <summary>
	/// PennMUSH <c>cmd_http</c> (<c>cmds.c:1870-1877</c>): a mortal is refused, and a wizard who names no
	/// URL is asked for one.
	/// </summary>
	[Test]
	public async ValueTask HttpCommand()
	{
		var mortal = await Players.PlayerAsync("HttpMortal");
		var wizard = await Players.WizardAsync("HttpWizard");

		await Assert.That(await Players.HeardRunning(mortal, "@http")).IsEquivalentTo(["Permission denied."]);
		await Assert.That(await Players.HeardRunning(wizard, "@http")).IsEquivalentTo(["What do you want to query?"]);
	}

	[Test]
	public async ValueTask SqlCommand()
	{
		var mortal = await Players.PlayerAsync("SqlMortal");

		await Assert.That(await Players.HeardRunning(mortal, "@sql SELECT 1")).IsEquivalentTo(["Permission denied."]);
	}

	/// <summary>@mapsql runs the attribute once per row, with the row number in %0 and the columns from %1.</summary>
	[Test]
	public async ValueTask MapsqlCommand()
	{
		var wizard = await Players.WizardAsync("MapsqlWizard");
		var token = TestIsolationHelpers.GenerateUniqueName("MapsqlRow");
		await Players.RunAsync(wizard, "&ROW me=@pemit me=row %0:%1");

		var heard = await Players.HeardRunning(wizard, $"@mapsql me/ROW=SELECT '{token}'");

		await Assert.That(heard).Contains($"row 1:{token}");
	}

	[Test]
	public async ValueTask SitelockCommand()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "SitelockCmd");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {testPlayer.DbRef}=WIZARD"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@sitelock/list"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Any<SharpMessage>(), TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// PennMUSH <c>@sockset [&lt;descriptor&gt;][=&lt;option&gt;, &lt;value&gt;]</c>: with no descriptor
	/// it sets the player's own connection.
	/// </summary>
	[Test]
	public async ValueTask SocksetCommand()
	{
		var player = await Players.PlayerAsync("SocksetCmd");

		await Players.RunAsync(player, "@sockset =width,100");

		await Assert.That(await Players.HeardRunning(player, "think width(me)")).IsEquivalentTo(["100"]);
	}

	[Test]
	public async ValueTask SlaveCommand()
	{
		var mortal = await Players.PlayerAsync("SlaveMortal");

		await Assert.That(await Players.HeardRunning(mortal, "@slave")).IsEquivalentTo(["Permission denied."]);
	}
}
