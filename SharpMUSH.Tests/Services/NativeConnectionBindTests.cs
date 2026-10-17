using System.Text;
using Mediator;
using NSubstitute;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class NativeConnectionBindTests
{
	[Test]
	public async Task AuthorityExpiryWhilePublicationIsBlockedPreventsTheActualBinding()
	{
		var connections = new ConnectionService(Substitute.For<IPublisher>());
		await connections.Register(1, "127.0.0.1", "localhost", "telnet", _ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask, () => Encoding.UTF8);
		var current = connections.Get(1)!;
		var now = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
		var expiry = now.AddMinutes(2);
		await current.Binding.PublicationGate.WaitAsync();
		Task<bool> binding;
		try
		{
			binding = connections.BindAuthorizedAsync(1, new DBRef(10, 1000), () => Task.FromResult(now < expiry)).AsTask();
			await Assert.That(binding.IsCompleted).IsFalse();
			now = expiry;
		}
		finally { current.Binding.PublicationGate.Release(); }
		await Assert.That(await binding).IsFalse();
		await Assert.That(connections.Get(1)!.Ref).IsNull();
		await Assert.That(connections.Get(1)!.State).IsEqualTo(IConnectionService.ConnectionState.Connected);
	}

	[Test]
	public async Task AuthorizedNativeBindingUsesNormalStateAndNotifiesOnce()
	{
		var connections = new ConnectionService(Substitute.For<IPublisher>());
		await connections.Register(1, "127.0.0.1", "localhost", "telnet", _ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask, () => Encoding.UTF8);
		var notifications = 0;
		connections.ListenState(_ => notifications++);
		var target = new DBRef(10, 1000);
		await Assert.That(await connections.BindAuthorizedAsync(1, target, () => Task.FromResult(true))).IsTrue();
		await Assert.That(connections.Get(1)!.Ref).IsEqualTo(target);
		await Assert.That(connections.Get(1)!.State).IsEqualTo(IConnectionService.ConnectionState.LoggedIn);
		await Assert.That(notifications).IsEqualTo(1);
	}
}
