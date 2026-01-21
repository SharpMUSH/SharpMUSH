using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class UserDefinedCommandsTests : TestsBase
{
	private INotifyService NotifyService => Services.GetRequiredService<INotifyService>(); 
	private IConnectionService ConnectionService => Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => Services.GetRequiredService<IMUSHCodeParser>();

	[Test]
	[Skip("Test needs investigation - unrelated to communication commands")]
	public async Task SetAndResetCacheTest()
	{
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("&cmd`setandresetcache #1=$test:@pemit #1=Value 1 received"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("test"));

		await Parser.CommandParse(1, ConnectionService,
			MModule.single("&cmd`setandresetcache #1=$test2:@pemit #1=Value 2 received"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("test2"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToPlainText() == "Value 1 received") ||
				(msg.IsT1 && msg.AsT1 == "Value 1 received")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToPlainText() == "Value 2 received") ||
				(msg.IsT1 && msg.AsT1 == "Value 2 received")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}
}