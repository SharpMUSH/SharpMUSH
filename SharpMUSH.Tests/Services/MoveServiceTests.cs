using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class MoveServiceTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMoveService MoveService => WebAppFactoryArg.Services.GetRequiredService<IMoveService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	[Test]
	[Skip("Integration test - requires database setup")]
	public async ValueTask NoLoopWithSimpleMove()
	{
		// This test would require proper database setup with objects created
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Integration test - requires database setup")]
	public async ValueTask DetectsDirectLoop()
	{
		// This test would require proper database setup with objects created
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Integration test - requires database setup")]
	public async ValueTask DetectsIndirectLoop()
	{
		// This test would require proper database setup with objects created
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Integration test - requires database setup")]
	public async ValueTask NoLoopIntoRoom()
	{
		// This test would require proper database setup with objects created
		await ValueTask.CompletedTask;
	}
}
