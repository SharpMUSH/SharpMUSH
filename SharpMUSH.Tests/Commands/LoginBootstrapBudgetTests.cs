using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Tests.Commands;

public class LoginBootstrapBudgetTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	[Arguments(true, true)]
	[Arguments(true, false)]
	[Arguments(false, true)]
	[Arguments(false, false)]
	public async Task ConnectInitializesPreferencesAndMotdBeforeHookDeadline(bool expire, bool checkPreferences)
	{
		var services = Factory.Services;
		var connections = services.GetRequiredService<IConnectionService>();
		var mediator = services.GetRequiredService<IMediator>();
		// A wizard of this test's own, standing in a room of its own: the wizard MOTD applies, and its
		// connect announcement reaches nobody else's players.
		var playerRef = await TestIsolationHelpers.CreateTestPlayerAsync(services, mediator, "LoginBudget");
		var room = await Factory.CommandParser.CommandParse(1, connections,
			MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName("LoginBudgetRoom")}"));
		await Factory.CommandParser.CommandParse(1, connections, MarkupText.Plain($"@tel {playerRef}={room.Message.ToPlainText().Trim()}"));
		await Factory.CommandParser.CommandParse(1, connections, MarkupText.Plain($"@set {playerRef}=WIZARD"));
		var player = (await mediator.Send(new SharpMUSH.Library.Queries.Database.GetObjectNodeQuery(playerRef))).Expect<SharpPlayer>();
		var handle = await TestIsolationHelpers.RegisterTestHandleAsync(connections, "telnet");
		var events = Substitute.For<IEventService>();
		var bus = Substitute.For<IMessageBus>();
		var notify = Substitute.For<INotifyService>();
		var data = Substitute.For<IExpandedObjectDataService>();
		data.GetExpandedServerDataAsync<MotdData>().Returns(new MotdData("login-motd", "wizard-motd", null, null));
		var passwords = Substitute.For<IPasswordService>();
		passwords.PasswordIsValid(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
		var timer = new ManualDeadline();
		using var budget = new ExecutionBudget(TimeSpan.FromMinutes(1), default, timer);
		var hookReached = false;
		events.TriggerEventAsync("PLAYER`CONNECT", Arg.Any<SharpMUSH.Library.Models.DBRef?>(), Arg.Any<string[]>())
			.Returns(_ => { hookReached = true; if (expire) timer.Fire(); budget.ThrowIfExceeded(); return ValueTask.CompletedTask; });
		var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(services,
			events, bus, notify, data, passwords);
		var parser = Factory.CommandParser.FromState(Factory.CommandParser.CurrentState with
		{
			Handle = handle,
			Arguments = new Dictionary<string, CallState> { ["0"] = new CallState(player.Object.Name + " password") }
		});
		try
		{
			using var scope = budget.Enter();
			try { await commands.Connect(parser, new SharpCommandAttribute { Name = "CONNECT" }); }
			catch (OperationCanceledException) when (budget.IsExpired) { }
			await Assert.That(hookReached).IsTrue();
			await Assert.That(budget.IsExpired).IsEqualTo(expire);
			await notify.Received(1).Notify(handle, Arg.Is<SharpMessage>(x => TestHelpers.MessageIsString(x, "login-motd")), null, INotifyService.NotificationType.Announce);
			await Assert.That(connections.Get(handle)!.Ref).IsNotNull();
			if (checkPreferences) await bus.Received(1).Publish(Arg.Is<UpdatePlayerPreferencesMessage>(x => x.Handle == handle), Arg.Any<CancellationToken>());
			else await notify.Received(1).Notify(handle, Arg.Is<SharpMessage>(x => TestHelpers.MessageIsString(x, "wizard-motd")), null, INotifyService.NotificationType.Announce);
		}
		finally { await connections.Disconnect(handle); }
	}

	/// <summary>
	/// The look a login ends with has a <c>queue_entry_cpu_time</c> limit of its own: however long the
	/// server's login work before it took, the player still sees the room and is not told
	/// "CPU usage exceeded." for work that was not theirs.
	/// </summary>
	[Test]
	public async Task LookAfterLoginIsNotChargedForTheLoginWorkBeforeIt()
	{
		var services = Factory.Services;
		var connections = services.GetRequiredService<IConnectionService>();
		var mediator = services.GetRequiredService<IMediator>();
		var playerRef = await TestIsolationHelpers.CreateTestPlayerAsync(services, mediator, "LoginLook");
		var roomName = TestIsolationHelpers.GenerateUniqueName("LoginLookRoom");
		var room = await Factory.CommandParser.CommandParse(1, connections, MarkupText.Plain($"@dig {roomName}"));
		await Factory.CommandParser.CommandParse(1, connections, MarkupText.Plain($"@tel {playerRef}={room.Message.ToPlainText().Trim()}"));
		// The teleport's own look shows the room too; only what the login says counts.
		var heardBeforeLogin = Factory.Notifications.CountFor(playerRef);
		var player = (await mediator.Send(new SharpMUSH.Library.Queries.Database.GetObjectNodeQuery(playerRef))).Expect<SharpPlayer>();
		var handle = await TestIsolationHelpers.RegisterTestHandleAsync(connections, "telnet");
		var events = Substitute.For<IEventService>();
		var notify = Substitute.For<INotifyService>();
		var passwords = Substitute.For<IPasswordService>();
		passwords.PasswordIsValid(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
		var timer = new ManualDeadline();
		using var budget = new ExecutionBudget(TimeSpan.FromMinutes(1), default, timer);
		// The login work runs the line's own deadline out before the look starts.
		events.TriggerEventAsync("PLAYER`CONNECT", Arg.Any<SharpMUSH.Library.Models.DBRef?>(), Arg.Any<string[]>())
			.Returns(_ => { timer.Fire(); return ValueTask.CompletedTask; });
		var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(services,
			events, Substitute.For<IMessageBus>(), notify, Substitute.For<IExpandedObjectDataService>(), passwords);
		var parser = Factory.CommandParser.FromState(Factory.CommandParser.CurrentState with
		{
			Handle = handle,
			Arguments = new Dictionary<string, CallState> { ["0"] = new CallState(player.Object.Name + " password") }
		});
		try
		{
			using (budget.Enter())
			{
				await commands.Connect(parser, new SharpCommandAttribute { Name = "CONNECT" });
			}

			await Assert.That(budget.IsExpired).IsTrue();
			await Assert.That(Factory.Notifications.For(playerRef).Skip(heardBeforeLogin)
				.Any(line => line.Contains(roomName, StringComparison.Ordinal))).IsTrue();
			await notify.DidNotReceive().NotifyLocalized(Arg.Any<SharpMUSH.Library.Models.DBRef>(), "CpuUsageExceeded",
				Arg.Any<AnySharpObject?>(), Arg.Any<object[]>());
		}
		finally { await connections.Disconnect(handle); }
	}

	private sealed class ManualDeadline : TimeProvider
	{
		private Action? _fire;
		public void Fire() => _fire!();
		public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
		{
			_fire = () => callback(state);
			return new Timer();
		}
		private sealed class Timer : ITimer
		{
			public bool Change(TimeSpan dueTime, TimeSpan period) => true;
			public void Dispose() { }
			public ValueTask DisposeAsync() => ValueTask.CompletedTask;
		}
	}
}
