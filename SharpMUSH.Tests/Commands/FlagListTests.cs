using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>@flag/list</c> is the same <c>list_all_flags</c> as <c>@power/list</c> (<c>src/flags.c</c>): a
/// wildcard over the names, and disabled definitions listed for God only. <c>@flag/list</c> used to
/// ignore both.
/// </summary>
/// <remarks>
/// Adding and deleting a flag changes the counts <see cref="StatsCommandTests"/> asserts exactly, so
/// the two classes share a parallel constraint.
/// </remarks>
[NotInParallel("FlagAndPowerRegistry")]
public class FlagListTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private async Task<string> ListingFor(DBRef who, long handle, string command)
	{
		var before = WebAppFactoryArg.Notifications.CountFor(who);
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain(command));
		return string.Join("\n", WebAppFactoryArg.Notifications.For(who).Skip(before));
	}

	[Test]
	public async ValueTask Pattern_ListsOnlyTheFlagsItMatches()
	{
		var lister = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "FlagPattern");
		var listing = await ListingFor(lister.DbRef, lister.Handle, "@flag/list WIZ*");

		await Assert.That(listing).Contains("WIZARD");
		await Assert.That(listing).DoesNotContain("ROYALTY");
	}

	[Test]
	public async ValueTask DisabledFlag_IsListedForGodOnly()
	{
		var flag = $"FL_{Guid.NewGuid():N}"[..12].ToUpperInvariant();
		var god = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@flag/add {flag}=q"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@flag/disable {flag}"));
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "FlagList");

		try
		{
			await Assert.That(await ListingFor(god, 1, $"@flag/list {flag}")).Contains(flag);
			await Assert.That(await ListingFor(mortal.DbRef, mortal.Handle, $"@flag/list {flag}")).DoesNotContain(flag);
		}
		finally
		{
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@flag/delete {flag}"));
		}
	}
}
