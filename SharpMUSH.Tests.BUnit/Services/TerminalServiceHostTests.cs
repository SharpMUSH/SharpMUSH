using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
	/// Mirrors Program.cs's DI shape: the concrete facade is the only thing actually constructed;
	/// the interface registration aliases to it via <c>GetRequiredService&lt;TerminalServiceHost&gt;</c>.
	/// Both registrations must resolve to the exact same object, or a consumer resolving through the
	/// interface would end up pointed at a second, un-recreated facade.
	/// </summary>
	[Test]
	public async Task Concrete_and_interface_registrations_resolve_to_the_same_instance()
	{
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddTransient<IWebSocketClientService>(_ => Substitute.For<IWebSocketClientService>());
		services.AddSingleton(sp => new TerminalServiceHost(
			() => new TerminalService(
				sp.GetRequiredService<IWebSocketClientService>(),
				sp.GetRequiredService<ILogger<TerminalService>>())));
		services.AddSingleton<ITerminalService>(sp => sp.GetRequiredService<TerminalServiceHost>());

		var provider = services.BuildServiceProvider();

		var concrete = provider.GetRequiredService<TerminalServiceHost>();
		var viaInterface = provider.GetRequiredService<ITerminalService>();

		await Assert.That(ReferenceEquals(concrete, viaInterface)).IsTrue();
	}
}
