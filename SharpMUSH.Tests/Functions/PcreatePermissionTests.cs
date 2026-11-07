using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// <c>pcreate()</c> is <c>do_pcreate</c>, as <c>@pcreate</c> is: it refuses anyone who is not Create_Player, a wizard
/// or a holder of the Player_Create power (PennMUSH <c>src/wiz.c:113-116</c>).
/// </summary>
public class PcreatePermissionTests : ServerTestBase
{
	[Test]
	public async Task PlayerCreatePowerLetsAMortalCallPcreate()
	{
		var creator = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "PcMortal");
		try
		{
			var refused = "PcN" + Guid.NewGuid().ToString("N")[..10];
			await Assert.That(await EvalAs(creator.DbRef, $"pcreate({refused},pcpass1)")).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
			await Assert.That(await Eval($"pmatch({refused})")).StartsWith("#-");

			await Cmd($"@power {creator.Name}=Player_Create");
			var made = "PcY" + Guid.NewGuid().ToString("N")[..10];
			await Assert.That(await EvalAs(creator.DbRef, $"pcreate({made},pcpass1)")).StartsWith("#");
			await Assert.That(await Eval($"checkpass(*{made},pcpass1)")).IsEqualTo("1");
		}
		finally
		{
			await ConnectionService.Disconnect(creator.Handle);
		}
	}
}
