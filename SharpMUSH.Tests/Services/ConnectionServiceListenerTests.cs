using Mediator;
using NSubstitute;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using System.Text;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// State-change listeners register at any time: InputSessionService does it from its constructor, so the
/// first resolution of that singleton can land while another connection is binding. Notifying must not
/// trip over a registration that happens during it.
/// </summary>
public class ConnectionServiceListenerTests
{
	private static async Task<ConnectionService> Registered(long handle)
	{
		var service = new ConnectionService(Substitute.For<IPublisher>());
		await service.Register(handle, "127.0.0.1", "localhost", "telnet",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		return service;
	}

	[Test]
	public async Task ListenerRegisteredDuringANotificationDoesNotBreakIt()
	{
		var service = await Registered(1);
		var lateCalls = 0;
		var registered = false;
		service.ListenState(_ =>
		{
			if (registered) return;
			registered = true;
			service.ListenState(_ => lateCalls++);
		});

		await service.Bind(1, new DBRef(1));
		await Assert.That(service.Get(1)!.State).IsEqualTo(IConnectionService.ConnectionState.LoggedIn);

		// The late listener hears the next change, not the one it was registered during.
		await Assert.That(lateCalls).IsEqualTo(0);
		await service.Disconnect(1);
		await Assert.That(lateCalls).IsEqualTo(1);
	}
}
