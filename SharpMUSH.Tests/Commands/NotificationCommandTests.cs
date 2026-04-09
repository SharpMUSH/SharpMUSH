using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class NotificationCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private Task<TestIsolationHelpers.TestPlayer> CreateTestPlayerAsync(string namePrefix) =>
		TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, namePrefix);

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask MessageCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("Message");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@message {testPlayer.DbRef}=Test message"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask RespondCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("Respond");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@respond {testPlayer.DbRef}=Response"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask RwallCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("Rwall");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@rwall Test message"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask WarningsCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("Warnings");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@warnings"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask WcheckCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("Wcheck");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@wcheck {testPlayer.DbRef}"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask SuggestCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("Suggest");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@suggest Test suggestion"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}
}
