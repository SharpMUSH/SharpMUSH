namespace SharpMUSH.Tests.Functions;

/// <summary>The Immortal power, TinyMUX's and RhostMUSH's IMMORTAL, needs no quota, and quota() says so.</summary>
public class ImmortalPowerTests : ServerTestBase
{
	[Test]
	public async Task QuotaReportsNoLimitForAnImmortal()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "Immortal");
		try
		{
			await Assert.That(await Eval($"quota(#{player.DbRef.Number})")).IsNotEqualTo("99999");
			await Cmd($"@power #{player.DbRef.Number}=Immortal");
			await Assert.That(await Eval($"quota(#{player.DbRef.Number})")).IsEqualTo("99999");
		}
		finally
		{
			await ConnectionService.Disconnect(player.Handle);
		}
	}
}
