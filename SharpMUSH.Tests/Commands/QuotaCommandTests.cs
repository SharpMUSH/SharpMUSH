using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
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
	private IOptionsWrapper<SharpMUSHOptions> Configuration => WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask SquotaCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@squota #1=100"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "Quota system disabled.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// Reproduces the @quota/set cache-invalidation gap: SetPlayerQuotaCommand wrote the new
	/// quota to the DB (fixed in e88c78a7) but only invalidated the PlayerList tag, which
	/// GetObjectNodeQuery's underlying number-keyed cache entry (CacheKeys.Object) isn't tagged
	/// with. A read through the SAME path LocateService/game reads use (GetObjectNodeQuery) after
	/// @quota/set must observe the new value — not a stale cached SharpPlayer with the old quota.
	/// </summary>
	[Test]
	public async ValueTask QuotaSet_InvalidatesObjectCache_VisibleOnNextRead()
	{
		var defaultHome = new DBRef((int)Configuration.CurrentValue.Database.DefaultHome);
		var startingQuota = (int)Configuration.CurrentValue.Limit.StartingQuota;
		var name = $"QuotaCacheTest{Guid.NewGuid():N}";

		var playerDbRef = await Mediator.Send(new CreatePlayerCommand(
			name,
			"testpass",
			defaultHome,
			defaultHome,
			startingQuota));

		// Populate the object cache the same way a game read (LocateService) would, before the mutation.
		var before = await Mediator.Send(new GetObjectNodeQuery(playerDbRef));
		await Assert.That(before.AsPlayer.Quota).IsEqualTo(startingQuota);

		var setResult = await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@quota/set {name}=777"));
		await Assert.That(setResult.Message).IsNotNull();

		// Re-read via the SAME cached path — must reflect 777, not the stale startingQuota.
		var after = await Mediator.Send(new GetObjectNodeQuery(playerDbRef));
		await Assert.That(after.AsPlayer.Quota).IsEqualTo(777);
	}
}
