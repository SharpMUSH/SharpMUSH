using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit;

/// <summary>The tab's character sessions for a test that registers its own play terminal.</summary>
internal static class TestCharacterSessions
{
	/// <summary>
	/// One session over the <see cref="PlayTerminalServiceHost"/> the test registered as
	/// <see cref="IPlayTerminalService"/>, with the test's comm feed and member lists when it registered them. Leaves a
	/// registry the test registered itself alone.
	/// </summary>
	public static IServiceCollection AddTestCharacterSessions(this IServiceCollection services)
	{
		services.TryAddSingleton(sp => new CharacterSessionRegistry(new CharacterSession(
			Host(sp.GetRequiredService<IPlayTerminalService>()),
			sp.GetService<ICommFeed>() ?? Substitute.For<ICommFeed>(),
			sp.GetService<IChannelWho>() ?? Substitute.For<IChannelWho>())));
		return services;
	}

	private static PlayTerminalServiceHost Host(IPlayTerminalService terminal) =>
		terminal as PlayTerminalServiceHost ?? new PlayTerminalServiceHost(() => terminal);
}
