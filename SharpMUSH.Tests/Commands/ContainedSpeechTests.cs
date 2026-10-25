namespace SharpMUSH.Tests.Commands;

/// <summary>A player hears what the objects it carries say, as PennMUSH's na_loc notifies the location first.</summary>
public class ContainedSpeechTests : ServerTestBase
{
	[Test]
	[Arguments("pose {0}", "Gadget {0}")]
	[Arguments("say {0}", "Gadget says, \"{0}\"")]
	[Arguments("@emit {0}", "{0}")]
	public async Task HolderHearsCarriedObject(string command, string expected)
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "Holder");
		var token = TestIsolationHelpers.GenerateUniqueName("carried");
		try
		{
			await CmdAs(actor.DbRef, actor.Handle, "@create Gadget");
			await CmdAs(actor.DbRef, actor.Handle, "@force Gadget=" + string.Format(command, token));
			await WebAppFactoryArg.QueueBarrierAsync();

			await Assert.That(Notifications.For(actor.DbRef)).Contains(string.Format(expected, token));
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}
}
