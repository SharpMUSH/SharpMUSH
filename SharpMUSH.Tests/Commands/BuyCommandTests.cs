using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Tests.Commands;

/// <summary>SharpMUSH tracks no money, so buy says so, as money() does.</summary>
public class BuyCommandTests : ServerTestBase
{
	[Test]
	public async Task BuySaysMoneyIsNotTracked()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "Buyer");
		try
		{
			await CmdAs(actor.DbRef, actor.Handle, "buy sword");
			await WebAppFactoryArg.QueueBarrierAsync();

			await Assert.That(Notifications.For(actor.DbRef)).Contains(ErrorMessages.Notifications.BuyCommandNotSupported);
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}
}
