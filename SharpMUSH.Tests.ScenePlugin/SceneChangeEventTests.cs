using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Scene.Commands;
using Scene = SharpMUSH.Plugins.Scene.Models.Scene;

namespace SharpMUSH.Tests.ScenePlugin;

/// <summary>
/// A write that changes a scene's key other than its status fires <c>SCENE`CHANGE</c> with the key and what it held
/// before; a write to the status, or of the value the key already holds, fires nothing.
/// </summary>
public class SceneChangeEventTests
{
	private static readonly DBRef Enactor = new(7);

	private static Scene Scheduled(long? at, string title = "Harbour Night") =>
		new("42", "scheduled", true, false, at, 0, 0, 0, "#7", "Owner", "#7", "Owner", null, "",
			new Dictionary<string, string> { ["title"] = title });

	private static (IMUSHCodeParser Parser, IEventService Events) Rig()
	{
		var events = Substitute.For<IEventService>();
		var services = new ServiceCollection().AddSingleton(events).BuildServiceProvider();
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.ServiceProvider.Returns(services);
		parser.CurrentState.Returns(ParserState.RootFor(Enactor));
		return (parser, events);
	}

	private static async Task SetAsync(IMUSHCodeParser parser, Scene before, string key, Scene after)
	{
		var previous = await SceneChangeEvent.ValueBeforeAsync(new FixedSceneService(before), before.Id, key);
		await SceneChangeEvent.AfterSetAsync(parser, key, previous, after);
	}

	[Test]
	public async Task A_rescheduled_scene_fires_with_its_old_time()
	{
		var (parser, events) = Rig();

		await SetAsync(parser, Scheduled(1_792_000_000_000), "ScheduledFor", Scheduled(1_792_086_400_000));

		await events.Received(1).TriggerEventAsync(SharpEvents.SceneChange, Enactor,
			Arg.Is<string[]>(a => a.SequenceEqual(new[] { "42", "scheduledfor", "1792000000000" })));
	}

	[Test]
	public async Task A_renamed_scene_fires_with_its_old_title()
	{
		var (parser, events) = Rig();

		await SetAsync(parser, Scheduled(null), "title", Scheduled(null, "Market Day"));

		await events.Received(1).TriggerEventAsync(SharpEvents.SceneChange, Enactor,
			Arg.Is<string[]>(a => a.SequenceEqual(new[] { "42", "title", "Harbour Night" })));
	}

	[Test]
	public async Task A_time_set_for_the_first_time_fires_with_an_empty_previous()
	{
		var (parser, events) = Rig();

		await SetAsync(parser, Scheduled(null), "scheduledfor", Scheduled(1_792_000_000_000));

		await events.Received(1).TriggerEventAsync(SharpEvents.SceneChange, Enactor,
			Arg.Is<string[]>(a => a.SequenceEqual(new[] { "42", "scheduledfor", "" })));
	}

	[Test]
	public async Task The_value_it_already_holds_fires_nothing()
	{
		var (parser, events) = Rig();

		await SetAsync(parser, Scheduled(1_792_000_000_000), "scheduledfor", Scheduled(1_792_000_000_000));

		await events.DidNotReceiveWithAnyArgs().TriggerEventAsync(default!, default, default(string[])!);
	}

	/// <summary>The status has SCENE`STATUS of its own.</summary>
	[Test]
	public async Task A_status_change_fires_nothing()
	{
		var (parser, events) = Rig();

		await SetAsync(parser, Scheduled(null), "status", Scheduled(null) with { Status = "active" });

		await events.DidNotReceiveWithAnyArgs().TriggerEventAsync(default!, default, default(string[])!);
	}

	[Test]
	public async Task A_missing_scene_fires_nothing()
	{
		var (parser, events) = Rig();

		var previous = await SceneChangeEvent.ValueBeforeAsync(new FixedSceneService(), "42", "title");
		await SceneChangeEvent.AfterSetAsync(parser, "title", previous, Scheduled(null));

		await events.DidNotReceiveWithAnyArgs().TriggerEventAsync(default!, default, default(string[])!);
	}
}
