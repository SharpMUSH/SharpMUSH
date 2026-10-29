using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Text;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// benchmark() with a <c>&lt;sendto&gt;</c> (<c>src/funmisc.c:1482</c>), and lwho()'s "offline" and
/// "all" lists, which walk the connections the way lports() does (<c>src/bsd.c:6575</c>).
/// </summary>
public class BenchmarkAndLwhoTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;

	private Task<TestIsolationHelpers.TestPlayer> Mortal(string prefix)
		=> TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);

	private async Task<string> AsGod(string expression)
		=> (await Parser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText().Trim();

	private async Task<string> As(TestIsolationHelpers.TestPlayer who, string expression)
		=> (await WebAppFactoryArg.CommandParserFor(who.DbRef, who.Handle)
			.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText().Trim();

	[Test]
	public async ValueTask SendtoGetsTheTimingsAndTheCallerTheLastResult()
	{
		var receiver = await Mortal("BenchSendto");

		await Assert.That(await AsGod($"benchmark(add(1,2),3,#{receiver.DbRef.Number})")).IsEqualTo("3");
		await Assert.That(Notifications.For(receiver.DbRef))
			.Contains(line => System.Text.RegularExpressions.Regex.IsMatch(line, @"^Average: \d+\.\d\d   Min: \d+   Max: \d+$"));
	}

	[Test]
	public async ValueTask SendtoMustBeSomeoneTheCallerMayPemit()
	{
		var caller = await Mortal("BenchCaller");
		var receiver = await Mortal("BenchHaven");
		await AsGod($"[set(#{receiver.DbRef.Number},HAVEN)]");

		await Assert.That(await As(caller, $"benchmark(add(1,2),3,#{receiver.DbRef.Number})")).IsEqualTo("#-1");
		await Assert.That(Notifications.For(receiver.DbRef)).DoesNotContain(line => line.StartsWith("Average: "));
	}

	[Test]
	public async ValueTask SendtoThatMatchesNothingIsAnError()
	{
		await Assert.That(await AsGod($"benchmark(add(1,2),3,{TestIsolationHelpers.GenerateUniqueName("BenchNobody")})"))
			.StartsWith("#-1");
	}

	[Test]
	public async ValueTask OfflineListsAnUnloggedConnectionAsMinusOne()
	{
		var player = await Mortal("LwhoOffline");
		var socket = TestIsolationHelpers.GenerateUniqueHandle();
		await ConnectionService.Register(socket, "localhost", "localhost", "telnet",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		try
		{
			var offline = (await AsGod("lwho(,offline)")).Split(' ');
			await Assert.That(offline).Contains("#-1");
			await Assert.That(offline).All(entry => entry == "#-1");

			var all = (await AsGod("lwho(,all)")).Split(' ');
			await Assert.That(all).Contains("#-1");
			await Assert.That(all).Contains($"#{player.DbRef.Number}");
		}
		finally
		{
			await ConnectionService.Disconnect(socket);
		}
	}

	[Test]
	public async ValueTask OfflineIsForPoweredCallersOnly()
	{
		var mortal = await Mortal("LwhoOfflineMortal");

		await Assert.That(await As(mortal, "lwho(,offline)")).IsEqualTo("#-1 PERMISSION DENIED: NEEDS GAME.WIZARD OR GAME.ROYALTY OR GAME.SEE_ALL");
	}
}
