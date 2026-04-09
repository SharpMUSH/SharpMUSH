using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class SocialCommandTests
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
	[Category("TestInfrastructure")]
	[Skip("Issue with NotifyService mock, needs investigation")]
	public async ValueTask SayCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("Say");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("say Hello world"));

		// Sender sees "You say, ..." while others see "Name says, ..."
		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(TestHelpers.MatchingObject(executor), MModule.single("You say, \"Hello world\""), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Say);
	}

	[Test]
	[Category("TestInfrastructure")]
	[Skip("Issue with NotifyService mock, needs investigation")]
	public async ValueTask PoseCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("Pose");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("pose waves hello"));

		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(TestHelpers.MatchingObject(executor), MModule.single("One waves hello"), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Pose);
	}

	[Test]
	[Category("TestInfrastructure")]
	[Skip("Issue with NotifyService mock, needs investigation")]
	public async ValueTask SemiposeCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("Semipose");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("semipose 's greeting"));

		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(TestHelpers.MatchingObject(executor), MModule.single("One's greeting"), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.SemiPose);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask WhisperCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("Whisper");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"whisper {testPlayer.DbRef}=Secret message"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}

	[Test]
	[Category("TestInfrastructure")]
	[Skip("Issue with NotifyService mock, needs investigation")]
	public async ValueTask PageCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("Page");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"page {testPlayer.DbRef}=Hello there"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<MString>(), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Say);
	}
}
