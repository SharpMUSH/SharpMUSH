using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class QuotaCommandTests
{
	[ClassDataSource<TestClassFactory>(Shared = SharedType.PerClass)]
	public required TestClassFactory Factory { get; init; }

	private INotifyService NotifyService => Factory.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => Factory.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => Factory.CommandParser;

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask SquotaCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@squota #1=100"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}
}
