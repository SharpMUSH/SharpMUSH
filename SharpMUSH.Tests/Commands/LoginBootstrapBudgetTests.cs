using System.Text;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
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
		var player = (await mediator.Send(new SharpMUSH.Library.Queries.Database.GetObjectNodeQuery(Factory.ExecutorDBRef))).Known.AsPlayer;
		var handle = Random.Shared.NextInt64(80000000, 90000000);
		await connections.Register(handle, "localhost", "localhost", "telnet", _ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask, () => Encoding.UTF8);
		var events = Substitute.For<IEventService>();
		var bus = Substitute.For<IMessageBus>();
		var notify = Substitute.For<INotifyService>();
		var data = Substitute.For<IExpandedObjectDataService>();
		data.GetExpandedServerDataAsync<MotdData>().Returns(new MotdData("login-motd", "wizard-motd", null, null));
		var passwords = Substitute.For<IPasswordService>();
		passwords.PasswordIsValid(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);
		var timer = new ManualDeadline();
		using var budget = new ExecutionBudget(TimeSpan.FromMinutes(1), default, timer);
		var hookReached = false;
		events.TriggerEventAsync(Arg.Any<IMUSHCodeParser>(), "PLAYER`CONNECT", Arg.Any<SharpMUSH.Library.Models.DBRef?>(), Arg.Any<string[]>())
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
			await notify.Received(1).Notify(handle, Arg.Is<SharpMessage>(x => x.IsT1 && x.AsT1 == "login-motd"), null, INotifyService.NotificationType.Announce);
			await Assert.That(connections.Get(handle)!.Ref).IsNotNull();
			if (checkPreferences) await bus.Received(1).Publish(Arg.Is<UpdatePlayerPreferencesMessage>(x => x.Handle == handle), Arg.Any<CancellationToken>());
			else await notify.Received(1).Notify(handle, Arg.Is<SharpMessage>(x => x.IsT1 && x.AsT1 == "wizard-motd"), null, INotifyService.NotificationType.Announce);
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
