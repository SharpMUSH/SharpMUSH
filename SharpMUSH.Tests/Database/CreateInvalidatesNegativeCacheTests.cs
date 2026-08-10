using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// Creating an object must invalidate the cached lookup for its dbref.
///
/// <see cref="GetObjectNodeByNumberQuery"/> is <c>ICacheable</c>, and
/// <see cref="SharpMUSH.Library.Behaviors.QueryCachingBehavior{TRequest,TResponse}"/> caches whatever
/// it returns — including a miss. The create commands declare cache keys for the objects they touch
/// (owner, home, container), but they cannot declare the NEW object's key, because its dbref does not
/// exist until the insert returns. So a lookup of a not-yet-existing dbref poisoned that key, and the
/// object stayed invisible after creation until the entry aged out.
///
/// Anything that probes a dbref before it exists hits this. The portal's Softcode Editor does it on
/// every create: it makes the object, then immediately selects it, and the select 404'd.
/// </summary>
[NotInParallel]
public class CreateInvalidatesNegativeCacheTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private async ValueTask<DBRef> CreateThingAsync(string prefix)
		=> await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, prefix);

	[Test]
	public async ValueTask ObjectIsVisible_WhenItsDbrefWasLookedUpBeforeItExisted()
	{
		// The test has to poison the key for a dbref that does not exist yet, which means predicting
		// the next one. The factory is shared per test session, so another class can take that dbref
		// in between; retry rather than fail, since a lost race says nothing about the behaviour.
		const int attempts = 5;
		for (var attempt = 1; ; attempt++)
		{
			var anchor = await CreateThingAsync("NegCacheAnchor");
			var next = new DBRef(anchor.Number + 1);

			var beforeCreation = await Mediator.Send(new GetObjectNodeQuery(next));
			if (!beforeCreation.IsNone)
			{
				if (attempt < attempts) continue;
				Assert.Fail($"#{next.Number} already existed on every one of {attempts} attempts.");
			}

			var created = await CreateThingAsync("NegCacheTarget");
			if (created.Number != next.Number)
			{
				// Someone else took the dbref we poisoned; this round proves nothing either way.
				if (attempt < attempts) continue;
				Assert.Fail($"lost the dbref race {attempts} times (wanted #{next.Number}, got #{created.Number}).");
			}

			var afterCreation = await Mediator.Send(new GetObjectNodeQuery(created));

			await Assert.That(afterCreation.IsNone).IsFalse()
				.Because("creating the object must clear the cached miss for its dbref");
			return;
		}
	}

	[Test]
	public async ValueTask CreatedObjectResolvesByBareDbref_ImmediatelyAfterCreation()
	{
		var created = await CreateThingAsync("NegCacheBare");

		var resolved = await Mediator.Send(new GetObjectNodeQuery(new DBRef(created.Number)));

		await Assert.That(resolved.IsNone).IsFalse();
	}
}
