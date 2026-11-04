using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Functions;

public class PortalRefreshTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private async Task<string> Evaluate(string call) => (await Factory.FunctionParser.EvaluateAsync(MarkupText.Plain(call))).ToPlainText();

	[Test]
	[Arguments("portalrefresh(sheet)", "")]
	[Arguments("portalrefresh(sheet,me)", "")]
	[Arguments("portalrefresh(Not A Slug)", ErrorMessages.Returns.InvalidApplication)]
	public async Task Staff_ask_an_application_to_refresh(string call, string expected) =>
		await Assert.That(await Evaluate(call)).IsEqualTo(expected);

	/// <summary>A clock that moves only when told; it runs the one delayed send when its time comes.</summary>
	private sealed class Clock : TimeProvider
	{
		private DateTimeOffset _now = new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);
		private (TimerCallback Tick, object? State, DateTimeOffset Due)? _waiting;
		public override DateTimeOffset GetUtcNow() => _now;
		public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
		{
			_waiting = (callback, state, _now + dueTime);
			return Substitute.For<ITimer>();
		}
		public void Advance(TimeSpan by)
		{
			_now += by;
			if (_waiting is { } waiting && waiting.Due <= _now)
			{
				_waiting = null;
				waiting.Tick(waiting.State);
			}
		}
	}

	private static (SignalRPortalApplicationNotifier Notifier, IGameHubClient Group, Clock Clock) Notifier(string group)
	{
		var hub = Substitute.For<IHubContext<GameHub, IGameHubClient>>();
		var client = Substitute.For<IGameHubClient>();
		hub.Clients.Group(group).Returns(client);
		var clock = new Clock();
		return (new SignalRPortalApplicationNotifier(hub, clock), client, clock);
	}

	[Test]
	public async Task A_refresh_reaches_the_group_of_the_app_or_of_one_characters_copy()
	{
		var (notifier, group, _) = Notifier("app:sheet:#5:100");
		await notifier.RefreshAsync("sheet", "#5:100");
		await group.Received(1).ReceiveAppRefresh("sheet:#5:100");
	}

	[Test]
	public async Task Asks_within_a_second_become_one_refresh_sent_when_the_second_ends()
	{
		var (notifier, group, clock) = Notifier("app:sheet");
		await notifier.RefreshAsync("sheet", null);
		await notifier.RefreshAsync("sheet", null);
		await notifier.RefreshAsync("sheet", null);
		await group.Received(1).ReceiveAppRefresh("sheet");

		clock.Advance(TimeSpan.FromSeconds(1));
		await Task.Delay(50);
		await group.Received(2).ReceiveAppRefresh("sheet");
	}
}
