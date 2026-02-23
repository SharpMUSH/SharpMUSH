using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class ListenPatternMatcherTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IListenPatternMatcher ListenPatternMatcher =>
		WebAppFactoryArg.Services.GetRequiredService<IListenPatternMatcher>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	[Skip("Integration test - requires database with objects and ^-listen attributes configured")]
	public async ValueTask MatchListenPatternsAsync_WithNoMonitorFlag_ReturnsEmpty()
	{
		// This test would require:
		// 1. An object without MONITOR flag
		// 2. ^-listen pattern attributes on the object
		// 3. Verification that no patterns match (because MONITOR is not set)
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Integration test - requires database with ^-listen patterns")]
	public async ValueTask MatchListenPatternsAsync_WithMatchingPattern_ReturnsMatch()
	{
		// This test would require:
		// 1. An object with MONITOR flag set
		// 2. ^-listen pattern attributes (e.g., ^*says*)
		// 3. Message that matches the pattern
		// 4. Verification that match is returned with captured groups
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Integration test - requires database with AAHEAR flag")]
	public async ValueTask MatchListenPatternsAsync_WithAAHEARFlag_MatchesForAnySpeaker()
	{
		// This test would require:
		// 1. An object with ^-listen pattern
		// 2. Attribute with AAHEAR flag set
		// 3. Verification that pattern matches for both self and others
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Integration test - requires database with AMHEAR flag")]
	public async ValueTask MatchListenPatternsAsync_WithAMHEARFlag_MatchesOnlyForSelf()
	{
		// This test would require:
		// 1. An object with ^-listen pattern
		// 2. Attribute with AMHEAR flag set
		// 3. Verification that pattern only matches when speaker is the listener itself
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Integration test - requires database with default ^-listen behavior")]
	public async ValueTask MatchListenPatternsAsync_WithDefaultBehavior_MatchesOnlyForOthers()
	{
		// This test would require:
		// 1. An object with ^-listen pattern
		// 2. Attribute without AAHEAR or AMHEAR flags (default behavior)
		// 3. Verification that pattern only matches when speaker is NOT the listener
		await ValueTask.CompletedTask;
	}
}
