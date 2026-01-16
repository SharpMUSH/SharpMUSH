using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class SocialCommandTests
{
	[ClassDataSource<TestClassFactory>(Shared = SharedType.PerClass)]
	public required TestClassFactory Factory { get; init; }

	private INotifyService NotifyService => Factory.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => Factory.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => Factory.CommandParser;

	[Test]
	[Skip("Issue with NotifyService mock, needs investigation")]

public async ValueTask SayCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("say Hello world"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), MModule.single("One says, \"Hello world\""), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Say);
	}

	[Test]
	[Skip("Issue with NotifyService mock, needs investigation")]
	public async ValueTask PoseCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("pose waves hello"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), MModule.single("One waves hello"), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Pose);
	}

	[Test]
	[Skip("Issue with NotifyService mock, needs investigation")]
	public async ValueTask SemiposeCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("semipose 's greeting"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), MModule.single("One's greeting"), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.SemiPose);
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask WhisperCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("whisper #1=Secret message"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Issue with NotifyService mock, needs investigation")]
	public async ValueTask PageCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("page #1=Hello there"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<MString>(), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Say);
	}
}
