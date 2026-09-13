using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public partial class SpeechTransformationTests
{
	[Test]
	[Arguments("puppet", false, false, false)]
	[Arguments("puppet", true, false, false)]
	[Arguments("puppet", false, true, false)]
	[Arguments("puppet", true, true, false)]
	[Arguments("owner", false, false, false)]
	[Arguments("owner", true, false, false)]
	[Arguments("puppet", true, false, true)]
	[Arguments("owner", true, false, true)]
	public async Task PuppetRelayUsesEffectiveFlagsAfterItsOwnPrefix(string flagSource, bool paranoid, bool self, bool noSpoof)
	{
		var owner = await Player();
		var speaker = self ? owner : await Player();
		var puppet = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "HeaderPuppet");
		await Admin($"@chown {puppet}={owner.DbRef}");
		await Flag(puppet, "PUPPET");
		var flagged = flagSource == "puppet" ? puppet : owner.DbRef;
		await Flag(flagged, "NOSPOOF");
		if (paranoid) await Flag(flagged, "PARANOID");
		var pipeline = await NotificationPipeline(owner.DbRef);
		var routing = ActivatorUtilities.CreateInstance<ListenerRoutingService>(Factory.Services, pipeline.Connections, pipeline.Bus);
		var node = await Node(puppet);
		await routing.ProcessNotificationAsync(new NotificationContext(puppet, (await node.Where()).Object().DBRef, []),
			"hello", await Node(speaker.DbRef), noSpoof ? INotifyService.NotificationType.NSPrivateEmit : INotifyService.NotificationType.PrivateEmit);
		var header = noSpoof || self && !paranoid ? "" : paranoid ? $"[{speaker.Name}(#{speaker.DbRef.Number})] " : $"[{speaker.Name}:] ";
		await Assert.That(string.Join("|", Output(pipeline.Bus))).IsEqualTo($"{node.Object().Name}> {header}hello");
	}

	[Test]
	public async Task ParanoidHeaderNamesTheActualOwnerOfAThingSpeaker()
	{
		var owner = await Player();
		var recipient = await Player();
		var speaker = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "OwnedSpeaker");
		await Admin($"@chown {speaker}={owner.DbRef}");
		await Flag(recipient.DbRef, "NOSPOOF");
		await Flag(recipient.DbRef, "PARANOID");
		var node = await Node(speaker);
		var pipeline = await NotificationPipeline(recipient.DbRef);
		await pipeline.Notify.Notify(recipient.DbRef, "hello", node, INotifyService.NotificationType.Emit);
		await Assert.That(string.Join("|", Output(pipeline.Bus))).IsEqualTo($"[{owner.Name}(#{owner.DbRef.Number})'s {node.Object().Name}(#{speaker.Number})] hello");
	}
}
