using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class ListenPatternMatcherTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IListenPatternMatcher ListenPatternMatcher =>
		WebAppFactoryArg.Services.GetRequiredService<IListenPatternMatcher>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	[Test]
	[Category("NeedsSetup")]
	[Skip("Integration test - requires database with objects and ^-listen attributes configured")]
	public async ValueTask MatchListenPatternsAsync_WithNoMonitorFlag_ReturnsEmpty()
	{
		// This test would require:
		// 1. An object without MONITOR flag
		// 2. ^-listen pattern attributes on the object
		// 3. Verification that no patterns match (because MONITOR is not set)
		await ValueTask.CompletedTask;
	}

	/// <summary>
	/// A wildcard <c>^</c>-pattern binds the whole match as group 0 and each star after it, in
	/// pattern order - the registers ProcessListenPatternsAsync hands the attribute as %0..%N.
	/// </summary>
	[Test]
	public async ValueTask MatchListenPatternsAsync_WithMatchingPattern_ReturnsMatch()
	{
		var created = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "ListenMatch");
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&LISTEN1 #{created.Number}=^* says *:think %0"));

		var listener = (await Mediator.Send(new GetObjectNodeQuery(created))).Known;
		var speaker = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Known;

		var matches = await ListenPatternMatcher.MatchListenPatternsAsync(listener, "God says hello", speaker);

		await Assert.That(matches).HasSingleItem();
		await Assert.That(matches[0].Attribute.Name).IsEqualTo("LISTEN1");
		await Assert.That(matches[0].Behavior).IsEqualTo(ListenBehavior.AHear);
		await Assert.That(matches[0].CapturedGroups).IsEquivalentTo(new[] { "God says hello", "God", "hello" });
	}

	[Test]
	[Category("NeedsSetup")]
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
	[Category("NeedsSetup")]
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
	[Category("NeedsSetup")]
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
