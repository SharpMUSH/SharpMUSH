using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;
using SurrealDb.Net;

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

	private static void Surreal(IServiceCollection services, Tracker tracker, Exception? resolveFailure = null)
	{
		services.AddSingleton(_ => tracker);
		services.AddSingleton<ISurrealDbClient>(provider =>
		{
			provider.GetRequiredService<Tracker>();
			if (resolveFailure is not null) throw resolveFailure;
			return Substitute.For<ISurrealDbClient>();
		});
	}

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
	[Arguments(false)]
	[Arguments(true)]
	public async Task ClientResolutionFailureDisposesOwnedProvider(bool cleanupFails)
	{
		var original = new InvalidOperationException("client factory");
		var cleanup = new InvalidOperationException("client cleanup");
		var tracker = new Tracker(cleanupFails ? cleanup : null);
		var failure = await Failure(() => IsolatedImportWorld.CreateAsync(DatabaseProvider.SurrealDB,
			configureSurrealServices: services => Surreal(services, tracker, original)));
		await Assert.That(tracker.Disposals).IsEqualTo(1);
		await Assert.That(Failures(failure)).Contains(original);
		if (cleanupFails) await Assert.That(Failures(failure)).Contains(cleanup);
	}

	[Test]
	public async Task EngineConstructionFailureDisposesConnectedProvider()
	{
		var tracker = new Tracker();
		var failure = await Failure(() => IsolatedImportWorld.CreateAsync(DatabaseProvider.SurrealDB,
			configureServices: services => services.AddSingleton(typeof(List<>), typeof(Tracker)),
			configureSurrealServices: services => Surreal(services, tracker)));
		await Assert.That(failure is ArgumentException).IsTrue();
		await Assert.That(tracker.Disposals).IsEqualTo(1);
	}

	[Test]
	public async Task MigrationFailurePreservesOriginalAndBothCleanupFailures()
	{
		var original = new InvalidOperationException("migration");
		var engineFailure = new InvalidOperationException("engine cleanup");
		var surrealFailure = new InvalidOperationException("surreal cleanup");
		var engine = new Tracker(engineFailure);
		var surreal = new Tracker(surrealFailure);
		var failure = await Failure(() => IsolatedImportWorld.CreateAsync(DatabaseProvider.SurrealDB,
			services => Engine(services, engine, original), services => Surreal(services, surreal)));
		await Assert.That(engine.Disposals).IsEqualTo(1);
		await Assert.That(surreal.Disposals).IsEqualTo(1);
		await Assert.That(Failures(failure)).IsEquivalentTo(new[] { original, engineFailure, surrealFailure });
	}

	[Test]
	public async Task DisposalAttemptsBothProvidersAndPreservesFailures()
	{
		var engineFailure = new InvalidOperationException("engine cleanup");
		var surrealFailure = new InvalidOperationException("surreal cleanup");
		var engine = new Tracker(engineFailure);
		var surreal = new Tracker(surrealFailure);
		var world = await IsolatedImportWorld.CreateAsync(DatabaseProvider.SurrealDB,
			services => Engine(services, engine), services => Surreal(services, surreal));
		await Assert.That(engine.Disposals).IsEqualTo(0);
		await Assert.That(surreal.Disposals).IsEqualTo(0);
		var failure = await Failure(() => world.DisposeAsync().AsTask());
		await Assert.That(engine.Disposals).IsEqualTo(1);
		await Assert.That(surreal.Disposals).IsEqualTo(1);
		await Assert.That(Failures(failure)).IsEquivalentTo(new[] { engineFailure, surrealFailure });
	}

	[Test]
	public async Task EngineDisposalFailureStillRemovesOwnedDirectory()
	{
		var original = new InvalidOperationException("engine cleanup");
		var engine = new Tracker(original);
		var world = await IsolatedImportWorld.CreateAsync(DatabaseProvider.Lightning, services => Engine(services, engine));
		var path = world.LightningPath!;
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
