using Microsoft.Extensions.DependencyInjection;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>@poor</c> and <c>@allquota</c> change the quota they name. A full <c>@allquota</c> changes every player's
/// quota, so it runs in <see cref="AllQuotaWorldTests"/>, on a world of its own.
/// </summary>
public class QuotaAdminCommandTests : ServerTestBase
{
	private OwnRoomPlayers Players => new(WebAppFactoryArg);

	[Test]
	public async Task Poor_SetsThePlayersQuotaToZero()
	{
		var wizard = await Players.WizardAsync("PoorWizard");
		var player = await Players.PlayerAsync("PoorPlayer");

		await Players.RunAsync(wizard, $"@poor {player.Name}");

		await Assert.That(await Eval($"quota(#{player.DbRef.Number})")).IsEqualTo("0");
	}

	[Test]
	public async Task Poor_ByAMortal_ChangesNothing()
	{
		var mortal = await Players.PlayerAsync("PoorMortal");
		var player = await Players.PlayerAsync("PoorTarget");
		var quota = await Eval($"quota(#{player.DbRef.Number})");

		await Players.RunAsync(mortal, $"@poor {player.Name}");

		await Assert.That(await Eval($"quota(#{player.DbRef.Number})")).IsEqualTo(quota);
	}
}

/// <summary><c>@allquota</c> run to the end, on a world no other test plays in.</summary>
[NotInParallel]
public class AllQuotaWorldTests
{
	[ClassDataSource<AllQuotaServerFactory>(Shared = SharedType.PerTestSession)]
	public required AllQuotaServerFactory Factory { get; init; }

	[Test]
	[Arguments("@allquota 7", true)]
	[Arguments("@allquota/quiet 9", false)]
	public async Task AllQuota_SetsEveryPlayersQuota(string command, bool playersAreTold)
	{
		var players = new OwnRoomPlayers(Factory);
		var wizard = await players.WizardAsync("AllQuotaWizard");
		var player = await players.PlayerAsync("AllQuotaPlayer");
		var amount = command.Split(' ')[1];

		var heard = await players.HeardWhile(player, () => players.RunAsync(wizard, command));

		await Assert.That(heard.Any(line => line.Contains(amount) && line.Contains(wizard.Name))).IsEqualTo(playersAreTold);
		foreach (var everyone in new[] { player.DbRef, wizard.DbRef, new SharpMUSH.Library.Models.DBRef(1) })
			await Assert.That((await Factory.FunctionParser.FunctionParse(MString.Plain($"quota(#{everyone.Number})")))?.Message.ToPlainText())
				.IsEqualTo(amount);
	}
}

/// <summary>A world of its own, so setting every player's quota reaches nobody else's test.</summary>
public class AllQuotaServerFactory : ServerWebAppFactory
{
	protected override bool UsesOwnWorld => true;
}
