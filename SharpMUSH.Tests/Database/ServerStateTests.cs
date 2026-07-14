using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;

namespace SharpMUSH.Tests.Database;

public class ServerStateTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private ISharpDatabase Db => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	[Test, NotInParallel(nameof(ServerStateTests))]
	public async Task ServerState_RoundTrip()
	{
		var initial = await Db.GetServerStateAsync();
		// Fresh test DB: migration ran before any claimed accounts existed.
		await Assert.That(initial.SetupCompleted).IsFalse();

		await Db.SetServerSetupCompletedAsync(true);
		var after = await Db.GetServerStateAsync();
		await Assert.That(after.SetupCompleted).IsTrue();

		// Restore so later setup-flow tests see an unclaimed game.
		await Db.SetServerSetupCompletedAsync(false);
		var restored = await Db.GetServerStateAsync();
		await Assert.That(restored.SetupCompleted).IsFalse();
	}
}
