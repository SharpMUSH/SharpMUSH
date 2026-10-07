using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
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

	/// <summary>
	/// A wildcard <c>^</c>-pattern binds each star in pattern order, beginning at %0.
	/// </summary>
	[Test]
	public async ValueTask MatchListenPatternsAsync_WithMatchingPattern_ReturnsMatch()
	{
		var created = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "ListenMatch");
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&LISTEN1 #{created.Number}=^* says *:think %0"));

		var listener = (await Mediator.Send(new GetObjectNodeQuery(created))).Expect<AnySharpObject>();
		var speaker = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>();

		var matches = await ListenPatternMatcher.MatchListenPatternsAsync(listener, "God says hello", speaker);

		await Assert.That(matches).HasSingleItem();
		await Assert.That(matches[0].Attribute.Name).IsEqualTo("LISTEN1");
		await Assert.That(matches[0].Behavior).IsEqualTo(ListenBehavior.AHear);
		await Assert.That(matches[0].CapturedGroups).IsEquivalentTo(new[] { "God", "hello" });
	}
}
