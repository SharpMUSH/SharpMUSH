using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// The four locks whose <c>@lock</c> switch used to be stored under a name the gate never read:
/// <c>@lock/chzone</c> wrote <c>Chzone</c> while <see cref="ILockService"/> looked up
/// <c>ChZone</c>, missed, and returned "no lock", which passes everybody.
/// </summary>
public class LockNameCanonicalisationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private ILockService LockService => WebAppFactoryArg.Services.GetRequiredService<ILockService>();

	[Test]
	[Arguments("chzone", LockType.ChZone)]
	[Arguments("teleport", LockType.Teleport)]
	[Arguments("dropto", LockType.DropTo)]
	[Arguments("chown", LockType.ChOwn)]
	public async ValueTask LockSetThroughItsSwitchIsTheLockTheGateReads(string switchName, LockType lockType)
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService,
			$"LockCanon{switchName}");

		var result = await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@lock/{switchName} #{obj.Number}=#FALSE"));
		await Assert.That(result.Message?.ToPlainText() ?? string.Empty).DoesNotContain("#-1");

		var found = await Mediator.Send(new GetObjectNodeQuery(obj));
		await Assert.That(found.IsNone).IsFalse();

		var player = await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)));

		// #FALSE passes nobody. Before the fix the gate looked the lock up under the enum spelling,
		// found nothing, and read that as "unlocked" — which passes everybody.
		await Assert.That(await LockService.Evaluate(lockType, found.Known, player.Known)).IsFalse();
	}

	[Test]
	[Arguments("chzone", LockType.ChZone)]
	[Arguments("teleport", LockType.Teleport)]
	[Arguments("dropto", LockType.DropTo)]
	[Arguments("chown", LockType.ChOwn)]
	public async ValueTask LockSetThroughItsSwitchIsStoredUnderTheEnumSpelling(string switchName, LockType lockType)
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService,
			$"LockStore{switchName}");

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@lock/{switchName} #{obj.Number}=#FALSE"));

		var found = await Mediator.Send(new GetObjectNodeQuery(obj));
		var locks = found.Known.Object().Locks;

		await Assert.That(locks.Keys).Contains(lockType.ToString());
		await Assert.That(locks.Count(pair => string.Equals(pair.Key, lockType.ToString(),
			StringComparison.OrdinalIgnoreCase))).IsEqualTo(1);
	}
}
