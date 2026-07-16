using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// Covers <see cref="TerminalServiceHost"/> — the stable facade that lets a character switch dispose
/// and rebuild the terminal without any <c>@inject</c> site or <see cref="MushQueryService"/>'s
/// constructor-captured reference ever pointing at a dead instance.
/// </summary>
public class TerminalServiceHostTests
{
	[Test]
	public async Task RecreateAsync_disposes_the_previous_inner()
	{
		var first = Substitute.For<ITerminalService>();
		var queue = new Queue<ITerminalService>([first, Substitute.For<ITerminalService>()]);
		var sut = new TerminalServiceHost(() => queue.Dequeue());

		await sut.RecreateAsync();

		await first.Received(1).DisposeAsync();
	}

	[Test]
	public async Task Events_re_raise_from_the_current_inner_after_a_recreate()
	{
		var first = Substitute.For<ITerminalService>();
		var second = Substitute.For<ITerminalService>();
		var queue = new Queue<ITerminalService>([first, second]);
		var sut = new TerminalServiceHost(() => queue.Dequeue());

		// Subscriber taken BEFORE the recreate — the @inject sites' situation exactly.
		var seen = 0;
		sut.ConnectionStateChanged += _ => seen++;

		await sut.RecreateAsync();
		second.ConnectionStateChanged += Raise.Event<Action<bool>>(true);

		await Assert.That(seen).IsEqualTo(1);
	}

	[Test]
	public async Task RecreateAsync_detaches_the_old_inner_so_it_no_longer_reaches_the_facade()
	{
		var first = Substitute.For<ITerminalService>();
		var second = Substitute.For<ITerminalService>();
		var queue = new Queue<ITerminalService>([first, second]);
		var sut = new TerminalServiceHost(() => queue.Dequeue());

		var seen = 0;
		sut.ConnectionStateChanged += _ => seen++;

		await sut.RecreateAsync();

		// Raise on the OLD inner, after it has been replaced — a still-attached handler here would
		// mean a half-disposed old terminal keeps delivering lines/state changes alongside the new one.
		first.ConnectionStateChanged += Raise.Event<Action<bool>>(true);

		await Assert.That(seen).IsEqualTo(0);
	}

	[Test]
	public async Task Calls_forward_to_the_current_inner()
	{
		var first = Substitute.For<ITerminalService>();
		var second = Substitute.For<ITerminalService>();
		var queue = new Queue<ITerminalService>([first, second]);
		var sut = new TerminalServiceHost(() => queue.Dequeue());
		await sut.RecreateAsync();

		await sut.SendAsync("look");

		await second.Received(1).SendAsync("look");
		await first.DidNotReceive().SendAsync("look");
	}

	/// <summary>
	/// <see cref="TerminalServiceHost.OobChannels"/> must hand out the same stable proxy object across
	/// a recreate — <c>Play.razor</c> subscribes to <c>ChannelUpdated</c> once at init and holds that
	/// reference for its lifetime. This pins both halves: the object identity never changes, AND a
	/// subscription taken on the pre-recreate proxy still hears updates raised on the post-recreate
	/// inner's store.
	/// </summary>
	[Test]
	public async Task OobChannels_is_stable_across_a_recreate_and_repoints_to_the_new_inner()
	{
		var firstOob = Substitute.For<IOobChannelStore>();
		var secondOob = Substitute.For<IOobChannelStore>();
		var first = Substitute.For<ITerminalService>();
		var second = Substitute.For<ITerminalService>();
		first.OobChannels.Returns(firstOob);
		second.OobChannels.Returns(secondOob);
		var queue = new Queue<ITerminalService>([first, second]);
		var sut = new TerminalServiceHost(() => queue.Dequeue());

		// Taken BEFORE the recreate, exactly like Play.razor's by-reference subscription.
		var oobBeforeRecreate = sut.OobChannels;
		string? seen = null;
		oobBeforeRecreate.ChannelUpdated += package => seen = package;

		await sut.RecreateAsync();

		await Assert.That(ReferenceEquals(sut.OobChannels, oobBeforeRecreate)).IsTrue();

		// Raised on the SECOND inner's store — the pre-recreate subscriber must still hear it.
		secondOob.ChannelUpdated += Raise.Event<Action<string>>("some-package");

		await Assert.That(seen).IsEqualTo("some-package");
	}

	/// <summary>
	/// Pins the base-constructor variance conversion (<c>Func&lt;IPlayTerminalService&gt;</c> passed
	/// to a base constructor expecting <c>Func&lt;ITerminalService&gt;</c>) and confirms
	/// <see cref="PlayTerminalServiceHost"/> gets the same recreate/dispose/forward mechanics as the
	/// base class, since it has no dedicated overrides of its own.
	/// </summary>
	[Test]
	public async Task PlayTerminalServiceHost_recreate_disposes_old_and_forwards_calls_to_the_new_inner()
	{
		var first = Substitute.For<IPlayTerminalService>();
		var second = Substitute.For<IPlayTerminalService>();
		var queue = new Queue<IPlayTerminalService>([first, second]);
		var sut = new PlayTerminalServiceHost(() => queue.Dequeue());

		await sut.RecreateAsync();

		await first.Received(1).DisposeAsync();

		await sut.SendAsync("look");

		await second.Received(1).SendAsync("look");
		await first.DidNotReceive().SendAsync("look");
	}

	/// <summary>
	/// Exercises the REAL <see cref="TerminalServiceCollectionExtensions.AddTerminalServices"/>
	/// production method (the one <c>Program.cs</c> actually calls), not a hand-rebuilt mirror of it.
	/// A hand-rebuilt <c>ServiceCollection</c> would only prove MS DI's alias-via-<c>GetRequiredService</c>
	/// mechanism works — a framework property, not this app's registration. Calling the real extension
	/// method means a regression in <c>Program.cs</c>'s actual DI shape (e.g. switching to
	/// <c>AddSingleton&lt;ITerminalService, TerminalServiceHost&gt;()</c>, which constructs two separate
	/// instances) fails THIS test.
	/// </summary>
	[Test]
	public async Task AddTerminalServices_aliases_interfaces_to_the_same_singletons_and_keeps_the_two_connections_independent()
	{
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddTerminalServices();

		var provider = services.BuildServiceProvider();

		var commandConcrete = provider.GetRequiredService<TerminalServiceHost>();
		var commandViaInterface = provider.GetRequiredService<ITerminalService>();
		var playConcrete = provider.GetRequiredService<PlayTerminalServiceHost>();
		var playViaInterface = provider.GetRequiredService<IPlayTerminalService>();

		await Assert.That(ReferenceEquals(commandConcrete, commandViaInterface)).IsTrue();
		await Assert.That(ReferenceEquals(playConcrete, playViaInterface)).IsTrue();
		await Assert.That(ReferenceEquals(commandConcrete, playConcrete)).IsFalse();
	}
}
