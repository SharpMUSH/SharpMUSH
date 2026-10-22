using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>SESSION [&lt;pattern&gt;]</c>, PennMUSH <c>cmd_session</c> (<c>cmds.c</c>) and <c>do_who_session</c>
/// (<c>bsd.c</c>).
/// </summary>
public class SessionCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private FixedConnectionTraffic Traffic => WebAppFactoryArg.Services.GetRequiredService<FixedConnectionTraffic>();

	private Task<TestIsolationHelpers.TestPlayer> PlayerAsync(string prefix) =>
		TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);

	private Task AsGod(string command) =>
		WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain(command)).AsTask();

	private async Task<string[]> LinesFrom(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = WebAppFactoryArg.Notifications.CountFor(player.DbRef);
		await WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle)
			.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		return [.. WebAppFactoryArg.Notifications.For(player.DbRef).Skip(before).SelectMany(message => message.Split('\n'))];
	}

	private async Task<TestIsolationHelpers.TestPlayer> WizardAsync()
	{
		var wizard = await PlayerAsync("SessWiz");
		await AsGod($"@set {wizard.DbRef}=WIZARD");
		return wizard;
	}

	[Test]
	public async ValueTask WizardSeesEachConnectionsBytes()
	{
		var wizard = await WizardAsync();
		var target = await PlayerAsync("SessBytes");
		Traffic.Set(new ConnectionTraffic(target.Handle, 1234, 56789, 17));

		var lines = await LinesFrom(wizard, $"SESSION {target.Name}");

		await Assert.That(lines.Any(line => line.Contains("Player Name") && line.Contains("Sent") && line.Contains("Recv") && line.Contains("Pend"))).IsTrue();
		var row = lines.Single(line => line.StartsWith(target.Name, StringComparison.Ordinal));
		await Assert.That(row.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^3..]).IsEquivalentTo(["1234", "56789", "17"]);
	}

	[Test]
	public async ValueTask ConnectionWithoutCountsShowsDashes()
	{
		var wizard = await WizardAsync();
		var target = await PlayerAsync("SessNoBytes");

		var lines = await LinesFrom(wizard, $"SESSION {target.Name}");

		var row = lines.Single(line => line.StartsWith(target.Name, StringComparison.Ordinal));
		await Assert.That(row.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^3..]).IsEquivalentTo(["-", "-", "-"]);
	}

	[Test]
	public async ValueTask PatternLimitsTheListing()
	{
		var wizard = await WizardAsync();
		var shown = await PlayerAsync("SessShown");
		var left = await PlayerAsync("SessLeft");

		var prefix = await LinesFrom(wizard, $"SESSION {shown.Name}");
		var wild = await LinesFrom(wizard, $"SESSION *{shown.Name[4..]}");

		foreach (var lines in new[] { prefix, wild })
		{
			await Assert.That(lines.Any(line => line.StartsWith(shown.Name, StringComparison.Ordinal))).IsTrue();
			await Assert.That(lines.Any(line => line.StartsWith(left.Name, StringComparison.Ordinal))).IsFalse();
			await Assert.That(lines.Any(line => line.StartsWith(wizard.Name, StringComparison.Ordinal))).IsFalse();
		}
	}

	[Test]
	public async ValueTask MortalGetsTheDoingListing()
	{
		var mortal = await PlayerAsync("SessMortal");
		Traffic.Set(new ConnectionTraffic(mortal.Handle, 4321, 8765, 0));

		var lines = await LinesFrom(mortal, $"SESSION {mortal.Name}");

		await Assert.That(lines.Any(line => line.Contains("Player Name") && line.Contains("Doing"))).IsTrue();
		await Assert.That(lines.Any(line => line.Contains("Pend"))).IsFalse();
		await Assert.That(lines.Any(line => line.Contains("8765"))).IsFalse();
	}
}
