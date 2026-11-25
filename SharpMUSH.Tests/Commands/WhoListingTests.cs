using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;
using System.Globalization;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// The pattern, order and <c>@doing</c> text of <c>WHO</c>, <c>DOING</c> and <c>SESSION</c>, as PennMUSH's
/// <c>dump_users</c>, <c>do_who_mortal</c>, <c>do_who_admin</c> and <c>do_who_session</c> (<c>src/bsd.c</c>) have them.
/// Each test lists only its own players by giving their shared, unique name prefix as the pattern.
/// </summary>
public class WhoListingTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private async Task<TestIsolationHelpers.TestPlayer> PlayerAsync(string name)
	{
		var player = await TestIsolationHelpers.CreateNamedTestPlayerAsync(WebAppFactoryArg.Services, Mediator, name);
		return new TestIsolationHelpers.TestPlayer(player,
			await TestIsolationHelpers.ConnectTestHandleAsync(ConnectionService, player), name);
	}

	/// <summary>Opens the player's connection <paramref name="secondsAgo"/> seconds ago.</summary>
	private void OpenedAgo(TestIsolationHelpers.TestPlayer player, int secondsAgo)
		=> ConnectionService.Update(player.Handle, "ConnectionStartTime",
			DateTimeOffset.UtcNow.AddSeconds(-secondsAgo).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));

	/// <summary>The lines one command gave <paramref name="viewer"/>, whether sent to its object or its connection.</summary>
	private async Task<string[]> LinesFrom(TestIsolationHelpers.TestPlayer viewer, string command)
	{
		var notifications = WebAppFactoryArg.Notifications;
		var toObject = notifications.CountFor(viewer.DbRef);
		var toHandle = notifications.CountForHandle(viewer.Handle);
		await WebAppFactoryArg.CommandParserFor(viewer.DbRef, viewer.Handle)
			.CommandParse(viewer.Handle, ConnectionService, MarkupText.Plain(command));
		return
		[
			.. notifications.For(viewer.DbRef).Skip(toObject)
				.Concat(notifications.ForHandle(viewer.Handle).Skip(toHandle))
				.SelectMany(message => message.Split('\n'))
		];
	}

	private static string[] Rows(string[] lines, string prefix)
		=> [.. lines.Where(line => line.StartsWith(prefix, StringComparison.Ordinal))];

	[Test]
	public async ValueTask WhoListsOnlyThePlayersItsPatternNames()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("WhoPat");
		var viewer = await PlayerAsync($"{token}Viewer");
		var other = await PlayerAsync($"{token}Other");

		var prefix = await LinesFrom(viewer, $"WHO {token}V");
		var wild = await LinesFrom(viewer, $"WHO *{token[3..]}Oth*");

		await Assert.That(Rows(prefix, token).Select(row => row.Split(' ')[0])).IsEquivalentTo([viewer.Name]);
		await Assert.That(Rows(wild, token).Select(row => row.Split(' ')[0])).IsEquivalentTo([other.Name]);
	}

	/// <summary>The footer counts every player the viewer may see, whatever the pattern leaves out.</summary>
	[Test]
	[Arguments("WHO")]
	[Arguments("DOING")]
	public async ValueTask FooterCountsThePlayersThePatternLeavesOut(string command)
	{
		var token = TestIsolationHelpers.GenerateUniqueName("WhoCount" + command[0]);
		var viewer = await PlayerAsync($"{token}Viewer");
		await PlayerAsync($"{token}Other");

		var lines = await LinesFrom(viewer, $"{command} {token}V");

		await Assert.That(Rows(lines, token)).Count().IsEqualTo(1);
		var footer = lines.Last(line => line.Contains(" players ", StringComparison.Ordinal));
		var counted = int.Parse(new string([.. footer.SkipWhile(c => !char.IsAsciiDigit(c)).TakeWhile(char.IsAsciiDigit)]),
			CultureInfo.InvariantCulture);
		await Assert.That(counted).IsGreaterThanOrEqualTo(2);
	}

	/// <summary>At the connect screen PennMUSH's <c>dump_users</c> reads the pattern as a name prefix only.</summary>
	[Test]
	public async ValueTask WhoAtTheConnectScreenTakesThePatternAsAPrefix()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("WhoLogin");
		var listed = await PlayerAsync($"{token}Listed");
		var handle = await TestIsolationHelpers.RegisterTestHandleAsync(ConnectionService);

		async Task<string[]> WhoAsync(string command)
		{
			var before = WebAppFactoryArg.Notifications.CountForHandle(handle);
			await WebAppFactoryArg.CommandParser.CommandParse(handle, ConnectionService, MarkupText.Plain(command));
			return [.. WebAppFactoryArg.Notifications.ForHandle(handle).Skip(before).SelectMany(message => message.Split('\n'))];
		}

		await Assert.That(Rows(await WhoAsync($"WHO {token}"), token).Select(row => row.Split(' ')[0]))
			.IsEquivalentTo([listed.Name]);
		await Assert.That(Rows(await WhoAsync($"WHO *{token[3..]}*"), token)).IsEmpty();
	}

	[Test]
	[Arguments("WHO")]
	[Arguments("DOING")]
	[Arguments("SESSION")]
	public async ValueTask ListingsShowTheNewestConnectionFirst(string command)
	{
		var token = TestIsolationHelpers.GenerateUniqueName("WhoOrd" + command[0]);
		var viewer = await PlayerAsync($"{token}Viewer");
		if (command == "SESSION")
		{
			await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {viewer.DbRef}=WIZARD"));
		}

		var oldest = await PlayerAsync($"{token}Oldest");
		var newest = await PlayerAsync($"{token}Newest");
		var middle = await PlayerAsync($"{token}Middle");
		OpenedAgo(viewer, 400);
		OpenedAgo(oldest, 300);
		OpenedAgo(middle, 200);
		OpenedAgo(newest, 100);

		var rows = Rows(await LinesFrom(viewer, $"{command} {token}"), token);

		await Assert.That(rows.Select(row => row.Split(' ')[0]))
			.IsEquivalentTo([newest.Name, middle.Name, oldest.Name, viewer.Name], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	/// <summary>
	/// PennMUSH's <c>get_doing</c> shows a line break, tab or other control character in <c>@doing</c> as a
	/// space, so a player's row stays one line. <c>&amp;</c> stores the value unevaluated, and the attribute keeps it.
	/// </summary>
	[Test]
	[Arguments("WHO")]
	[Arguments("DOING")]
	public async ValueTask DoingStaysOnTheRowsLine(string command)
	{
		var token = TestIsolationHelpers.GenerateUniqueName("WhoDo" + command[0]);
		var viewer = await PlayerAsync($"{token}Viewer");
		await LinesFrom(viewer, "&DOING me=first%rsecond%tthird");

		var lines = await LinesFrom(viewer, $"{command} {token}");

		var row = Rows(lines, token).Single();
		await Assert.That(row).EndsWith("first second third");
		await Assert.That(lines.Any(line => line.StartsWith("second", StringComparison.Ordinal))).IsFalse();
		await Assert.That((await LinesFrom(viewer, "think get(me/DOING)")).Any(line => line == "first%rsecond%tthird")).IsTrue();
	}
}
