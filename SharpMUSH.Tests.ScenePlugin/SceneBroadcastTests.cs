using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Messaging.NATS;
using SharpMUSH.Plugins.Scene.Commands;

namespace SharpMUSH.Tests.ScenePlugin;

/// <summary>
/// The live feed is best-effort. Every caller publishes AFTER its pose mutation has committed, so a
/// messaging failure that escaped the publisher would turn a recorded pose into an error: the softcode
/// caller would get no pose id, and a retry would record the pose twice.
/// </summary>
public class SceneBroadcastTests
{
	[Test]
	public async Task An_unreachable_NATS_server_does_not_fail_the_publish()
	{
		// Port 1 on loopback: nothing listens there, so the connect is refused.
		var services = new ServiceCollection()
			.AddSingleton(new NatsOptions { Url = "nats://127.0.0.1:1" })
			.BuildServiceProvider();
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.ServiceProvider.Returns(services);

		await SceneBroadcast.PublishSceneEventAsync(parser, "42", "pose", null);
	}
}
