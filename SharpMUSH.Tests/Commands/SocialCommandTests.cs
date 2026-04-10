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

	[Test]
	[Category("TestInfrastructure")]
	[Skip("Issue with NotifyService mock, needs investigation")]
	public async ValueTask SayCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("say Hello world"));

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
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("pose waves hello"));

		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(TestHelpers.MatchingObject(executor), MModule.single("One waves hello"), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Pose);
	}

	[Test]
	[Category("TestInfrastructure")]
	[Skip("Issue with NotifyService mock, needs investigation")]
	public async ValueTask SemiposeCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("semipose 's greeting"));

		await NotifyService
			.Received(Quantity.AtLeastOne())
			.Notify(TestHelpers.MatchingObject(executor), MModule.single("One's greeting"), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.SemiPose);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask WhisperCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("whisper #1=Secret message"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>(), Arg.Any<AnySharpObject?>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[Category("TestInfrastructure")]
	[Skip("Issue with NotifyService mock, needs investigation")]
	public async ValueTask PageCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("page #1=Hello there"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<MString>(), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Say);
	}
}
