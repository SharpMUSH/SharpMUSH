using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class QuotaCommandTests
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
	public async ValueTask SquotaCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("Squota");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@squota {testPlayer.DbRef}=100"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}
}
