using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Handlers;

/// <summary>
/// <c>GetConnectionLogsQueryHandler</c> depends on the narrow <c>ISharpDatabaseWithLogging</c>
/// rather than casting the <c>ISharpDatabase</c> composite to it (engine data trunk §2). Nothing
/// registers that interface — no shipped provider implements it — so the dependency is optional and
/// DI has to supply the parameter's default. This test is the proof that it does: a handler DI
/// cannot construct would throw here rather than stream an empty result.
/// </summary>
public class ConnectionLogsQueryHandlerTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async Task QueryResolvesWithoutALoggingProviderAndStreamsNothing()
	{
		var logs = await Mediator.CreateStream(new GetConnectionLogsQuery("Connection", 0, 10))
			.ToArrayAsync();

		await Assert.That(logs).IsEmpty();
	}
}
