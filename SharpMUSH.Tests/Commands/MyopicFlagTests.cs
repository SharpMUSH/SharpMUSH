using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// PennMUSH's MYOPIC (<c>src/look.c</c>, <c>unparse_object_myopic</c>): a myopic player sees names without dbrefs
/// and flags, even on what they control.
/// </summary>
public class MyopicFlagTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();

	[Test]
	public async Task Inventory_ShowsBareNamesToAMyopicPlayer()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, Connections, "Myopic");
		var parser = Factory.CommandParserFor(player.DbRef, player.Handle);
		var thing = TestIsolationHelpers.GenerateUniqueName("MyopicThing");
		await parser.CommandParse(player.Handle, Connections, MarkupText.Plain($"@create {thing}"));

		await parser.CommandParse(player.Handle, Connections, MarkupText.Plain("inventory"));
		var sighted = Factory.Notifications.For(player.DbRef).Where(line => line.StartsWith(thing)).ToList();

		await parser.CommandParse(player.Handle, Connections, MarkupText.Plain("@set me=MYOPIC"));
		await parser.CommandParse(player.Handle, Connections, MarkupText.Plain("inventory"));
		var myopic = Factory.Notifications.For(player.DbRef).Where(line => line.StartsWith(thing)).ToList();

		await Assert.That(sighted).Contains(line => line.StartsWith($"{thing}(#"));
		await Assert.That(myopic).Contains(thing);
	}
}
