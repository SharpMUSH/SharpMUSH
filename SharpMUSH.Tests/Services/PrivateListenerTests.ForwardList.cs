using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// notify.c:1549-1554 and notify_list: an AUDIBLE object passes on what it hears to its @forwardlist,
/// prefixed by its @prefix or "From &lt;name&gt;, ". The recipient's @lock/forward admits the forwarder
/// (<c>Can_Forward</c>).
/// </summary>
public partial class PrivateListenerTests
{
	[Test]
	public async Task AudibleForwardListPassesOnWhatItHears()
	{
		var speaker = await Player();
		var recipient = await Player();
		var forwarder = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "Microphone");
		await Admin($"@set {forwarder}=AUDIBLE");
		await Admin($"@lock/forward {recipient.DbRef}={forwarder}");
		await Admin($"@forwardlist {forwarder}=#{recipient.DbRef.Number}");
		var name = (await Node(forwarder)).Object().Name;
		var pipeline = await Build(speaker, recipient.DbRef);
		RemoveOutputFraming(pipeline);

		await pipeline.Notify.Notify(forwarder, "testing one two", await Node(speaker.DbRef), INotifyService.NotificationType.Emit);

		await Assert.That(ForwardedOutput(pipeline)).Contains($"From {name}, testing one two");
	}

	[Test]
	public async Task ForwardListUsesThePrefixAndNeedsAudible()
	{
		var speaker = await Player();
		var recipient = await Player();
		var forwarder = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "PrefixedMicrophone");
		await Admin($"@lock/forward {recipient.DbRef}={forwarder}");
		await Admin($"@forwardlist {forwarder}=#{recipient.DbRef.Number}");
		await Admin($"@prefix {forwarder}=On air:");
		var pipeline = await Build(speaker, recipient.DbRef);
		RemoveOutputFraming(pipeline);

		await pipeline.Notify.Notify(forwarder, "not yet", await Node(speaker.DbRef), INotifyService.NotificationType.Emit);
		await Assert.That(ForwardedOutput(pipeline)).IsEmpty();

		await Admin($"@set {forwarder}=AUDIBLE");
		await pipeline.Notify.Notify(forwarder, "now live", await Node(speaker.DbRef), INotifyService.NotificationType.Emit);
		await Assert.That(ForwardedOutput(pipeline)).Contains("On air: now live");
	}

	[Test]
	public async Task ForwardListHonoursTheFilter()
	{
		var speaker = await Player();
		var recipient = await Player();
		var forwarder = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "FilteredMicrophone");
		await Admin($"@set {forwarder}=AUDIBLE");
		await Admin($"@lock/forward {recipient.DbRef}={forwarder}");
		await Admin($"@forwardlist {forwarder}=#{recipient.DbRef.Number}");
		await Admin($"@filter {forwarder}=*secret*");
		var pipeline = await Build(speaker, recipient.DbRef);
		RemoveOutputFraming(pipeline);

		await pipeline.Notify.Notify(forwarder, "a secret plan", await Node(speaker.DbRef), INotifyService.NotificationType.Emit);

		await Assert.That(ForwardedOutput(pipeline)).IsEmpty();
	}
}
