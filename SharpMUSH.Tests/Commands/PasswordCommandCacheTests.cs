using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Verifies that SetPlayerPasswordCommand invalidates the same object-cache entry LocateService
/// reads (CacheKeys.Object) — the identical gap that @quota/set had, but on the password path.
/// SetPlayerPasswordCommand is shaped as ICommand&lt;ValueTask&lt;Unit&gt;&gt; (an unusual double-wrapped
/// response), so this also empirically confirms CacheInvalidationBehavior&lt;TRequest, TResponse&gt;
/// actually fires for that shape rather than being silently skipped by the pipeline.
/// A non-null salt is passed so the DB layer stores the value verbatim (bypassing the separate,
/// out-of-scope double-hashing bug on the salt == null path).
/// </summary>
public class PasswordCommandCacheTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IOptionsWrapper<SharpMUSHOptions> Configuration => WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();

	[Test]
	public async ValueTask SetPlayerPasswordCommand_InvalidatesObjectCache_VisibleOnNextRead()
	{
		var defaultHome = new DBRef((int)Configuration.CurrentValue.Database.DefaultHome);
		var startingQuota = (int)Configuration.CurrentValue.Limit.StartingQuota;
		var name = $"PasswordCacheTest{Guid.NewGuid():N}";

		var playerDbRef = await Mediator.Send(new CreatePlayerCommand(
			name,
			"testpass",
			defaultHome,
			defaultHome,
			startingQuota));

		// Populate the object cache the same way a game read (LocateService) would, before the mutation.
		var before = await Mediator.Send(new GetObjectNodeQuery(playerDbRef));
		var originalHash = before.AsPlayer.PasswordHash;

		const string newHash = "already-hashed-marker-value";
		await Mediator.Send(new SetPlayerPasswordCommand(before.AsPlayer, newHash, Salt: "fixed-salt"));

		// Re-read via the SAME cached path — must reflect the new hash, not the stale cached one.
		var after = await Mediator.Send(new GetObjectNodeQuery(playerDbRef));
		await Assert.That(after.AsPlayer.PasswordHash).IsEqualTo(newHash);
		await Assert.That(after.AsPlayer.PasswordHash).IsNotEqualTo(originalHash);
	}
}
