using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Services;

public class IsolatedImportWorldCleanupTests
{
	private sealed class Tracker(Exception? failure = null) : IAsyncDisposable
	{
		public int Disposals { get; private set; }
		public ValueTask DisposeAsync()
		{
			Disposals++;
			return failure is null ? ValueTask.CompletedTask : ValueTask.FromException(failure);
		}
	}

	private static async Task<Exception> Failure(Func<Task> action)
	{
		try { await action(); }
		catch (Exception exception) { return exception; }
		throw new InvalidOperationException("Expected the injected failure.");
	}

	private static Exception[] Failures(Exception exception) => exception is AggregateException aggregate
		? aggregate.Flatten().InnerExceptions.ToArray() : [exception];

	private static void Engine(IServiceCollection services, Tracker tracker, Exception? migrationFailure = null)
	{
		services.AddSingleton(_ => tracker);
		services.AddSingleton<IDatabaseLifecycle>(provider =>
		{
			provider.GetRequiredService<Tracker>();
			var lifecycle = Substitute.For<IDatabaseLifecycle>();
			if (migrationFailure is not null)
				lifecycle.Migrate(Arg.Any<CancellationToken>()).Returns(ValueTask.FromException(migrationFailure));
			return lifecycle;
		});
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(ValueTask.FromResult<AnyOptionalSharpObject>(new None()));
		services.AddSingleton(mediator);
	}

	[Test]
	public async Task MigrationFailurePreservesOriginalAndCleanupFailure()
	{
		var original = new InvalidOperationException("migration");
		var engineFailure = new InvalidOperationException("engine cleanup");
		var engine = new Tracker(engineFailure);
		var failure = await Failure(() => IsolatedImportWorld.CreateAsync(services => Engine(services, engine, original)));
		await Assert.That(engine.Disposals).IsEqualTo(1);
		await Assert.That(Failures(failure)).IsEquivalentTo(new[] { original, engineFailure });
	}

	[Test]
	public async Task EngineDisposalFailureStillRemovesOwnedDirectory()
	{
		var original = new InvalidOperationException("engine cleanup");
		var engine = new Tracker(original);
		var world = await IsolatedImportWorld.CreateAsync(services => Engine(services, engine));
		var path = world.LightningPath;
		Directory.CreateDirectory(path);
		try
		{
			await File.WriteAllTextAsync(Path.Join(path, "owned-fixture"), "owned");
			var failure = await Failure(() => world.DisposeAsync().AsTask());
			await Assert.That(ReferenceEquals(failure, original)).IsTrue();
			await Assert.That(Directory.Exists(path)).IsFalse();
		}
		finally
		{
			if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
		}
	}
}
