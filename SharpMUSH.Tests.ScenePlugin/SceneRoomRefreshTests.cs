using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Scene.Commands;
using Scene = SharpMUSH.Plugins.Scene.Models.Scene;

namespace SharpMUSH.Tests.ScenePlugin;

/// <summary>
/// A scene change the Play page shows fires <c>ROOM`CONTENTS</c> with the cause <c>scene</c> for the rooms
/// it touched, and nothing for a change it does not show.
/// </summary>
public class SceneRoomRefreshTests
{
	private static readonly DBRef Enactor = new(7);

	private static Scene SceneIn(string? room, string id = "42") =>
		new(id, "active", true, false, null, 0, 0, 0, "#7", "Owner", "#7", "Owner", room, "Yard", new Dictionary<string, string>());

	private sealed class OneScene(Scene scene) : SceneServiceStub
	{
		public override Task<Found<Scene>> GetSceneAsync(string sceneId) => Task.FromResult<Found<Scene>>(scene);
	}

	private static (IMUSHCodeParser Parser, IEventService Events) Rig()
	{
		var events = Substitute.For<IEventService>();
		var services = new ServiceCollection().AddSingleton(events).BuildServiceProvider();
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.ServiceProvider.Returns(services);
		parser.CurrentState.Returns(ParserState.RootFor(Enactor));
		return (parser, events);
	}

	private static Task Fired(IEventService events, IMUSHCodeParser parser, string room) =>
		events.Received(1).TriggerEventAsync(parser, SharpEvents.RoomContents, Enactor,
			Arg.Is<string[]>(a => a.SequenceEqual(new[] { room, SceneRoomRefresh.Cause }))).AsTask();

	[Test]
	public async Task A_status_change_refreshes_the_scenes_room()
	{
		var (parser, events) = Rig();

		await SceneRoomRefresh.AfterSetAsync(parser, "Status", "#12", SceneIn("#12"));

		await Fired(events, parser, "#12");
	}

	[Test]
	public async Task A_move_refreshes_the_room_it_left_and_the_room_it_entered()
	{
		var (parser, events) = Rig();

		await SceneRoomRefresh.AfterSetAsync(parser, "room", "#12", SceneIn("#13"));

		await Fired(events, parser, "#12");
		await Fired(events, parser, "#13");
	}

	[Test]
	public async Task A_key_the_room_does_not_show_refreshes_nothing()
	{
		var (parser, events) = Rig();

		await SceneRoomRefresh.AfterSetAsync(parser, "summary", "#12", SceneIn("#12"));

		await events.DidNotReceiveWithAnyArgs().TriggerEventAsync(default!, default!, default, default(string[])!);
	}

	[Test]
	public async Task A_cast_change_refreshes_the_scenes_room()
	{
		var (parser, events) = Rig();

		await SceneRoomRefresh.AfterMembershipAsync(parser, new OneScene(SceneIn("#12")), "42");

		await Fired(events, parser, "#12");
	}

	/// <summary>The portal focuses on every pose it sends; a focus that did not move must cost no refresh.</summary>
	[Test]
	public async Task A_focus_that_did_not_move_refreshes_nothing()
	{
		var (parser, events) = Rig();

		await SceneRoomRefresh.AfterFocusAsync(parser, new OneScene(SceneIn("#12")), "#7", "42", "42");

		await events.DidNotReceiveWithAnyArgs().TriggerEventAsync(default!, default!, default, default(string[])!);
	}

	/// <summary>The write has committed by the time this runs; a failing handler must not turn it into an error.</summary>
	[Test]
	public async Task A_failing_event_does_not_reach_the_caller()
	{
		var (parser, events) = Rig();
		events.TriggerEventAsync(default!, default!, default, default(string[])!)
			.ReturnsForAnyArgs(ValueTask.FromException(new InvalidOperationException("boom")));

		await SceneRoomRefresh.RefreshAsync(parser, "#12");
	}
}
