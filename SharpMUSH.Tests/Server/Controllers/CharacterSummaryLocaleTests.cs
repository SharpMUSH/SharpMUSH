using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// The roster carries each character's <c>LOCALE</c>, which <c>@locale</c> writes, so a portal tab can take the
/// character's language from the roster it already reads.
/// </summary>
public class CharacterSummaryLocaleTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private async Task<SharpPlayer> PlayerAsync(TestIsolationHelpers.TestPlayer player)
		=> (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Expect<SharpPlayer>();

	[Test]
	public async Task The_roster_carries_the_locale_at_locale_set()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "RosterLocale");
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@locale de-DE"));

		var summary = (await CharacterSummaryMapper.BuildSummariesAsync([await PlayerAsync(player)], mediator: Mediator)).Single();

		await Assert.That(summary.Locale).IsEqualTo("de-DE");
	}

	[Test]
	public async Task A_character_without_a_locale_carries_none()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "RosterNoLocale");

		var summary = (await CharacterSummaryMapper.BuildSummariesAsync([await PlayerAsync(player)], mediator: Mediator)).Single();

		await Assert.That(summary.Locale).IsNull();
	}
}
