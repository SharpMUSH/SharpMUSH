using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Concurrent;
using System.Text;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// What PennMUSH's connection functions answer when they cannot answer properly. Each of them fails
/// in its own currency — <c>fun_conn</c> counts seconds so it fails with "-1", <c>fun_hostname</c>
/// returns a string so it fails with "#-1", <c>fun_width</c> is a formatting helper so it falls
/// through to the caller's default — and the differences are not decoration: softcode wraps these in
/// arithmetic and in string comparisons, and an error string where a number was promised propagates.
/// Values read from <c>src/bsd.c</c>.
/// </summary>
public class ConnectionFunctionParityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	private async Task<string> EvaluateAsync(string code) =>
		(await Parser.FunctionParse(MarkupText.Plain(code)))!.Message!.ToPlainText();

	/// <summary>
	/// A name that matches no player at all. PennMUSH's <c>lookup_desc</c> returns NULL and each
	/// function supplies its own answer for that; none of them says "#-1 NO MATCH", and none of them
	/// says anything to the caller, because these are functions.
	/// </summary>
	[Test]
	[Arguments("conn(NoSuchPlayerAnywhere)", "-1")]
	[Arguments("idle(NoSuchPlayerAnywhere)", "-1")]
	[Arguments("recv(NoSuchPlayerAnywhere)", "-1")]
	[Arguments("sent(NoSuchPlayerAnywhere)", "-1")]
	[Arguments("cmds(NoSuchPlayerAnywhere)", "-1")]
	[Arguments("host(NoSuchPlayerAnywhere)", "#-1")]
	[Arguments("ipaddr(NoSuchPlayerAnywhere)", "#-1")]
	[Arguments("hidden(NoSuchPlayerAnywhere)", "#-1")]
	[Arguments("doing(NoSuchPlayerAnywhere)", "")]
	[Arguments("ports(NoSuchPlayerAnywhere)", "")]
	[Arguments("ssl(NoSuchPlayerAnywhere)", "#-1 NOT CONNECTED")]
	[Arguments("terminfo(NoSuchPlayerAnywhere)", "#-1 NOT CONNECTED")]
	[Arguments("width(NoSuchPlayerAnywhere)", "78")]
	[Arguments("height(NoSuchPlayerAnywhere)", "24")]
	public async Task UnmatchedName_AnswersInEachFunctionsOwnCurrency(string code, string expected)
		=> await Assert.That(await EvaluateAsync(code)).IsEqualTo(expected);

	/// <summary>
	/// A descriptor number nothing is listening on. Same answers as an unmatched name — PennMUSH runs
	/// both through <c>lookup_desc</c> and cannot tell them apart by the time it returns NULL.
	/// </summary>
	[Test]
	[Arguments("conn(999999999)", "-1")]
	[Arguments("idle(999999999)", "-1")]
	[Arguments("recv(999999999)", "-1")]
	[Arguments("sent(999999999)", "-1")]
	[Arguments("cmds(999999999)", "-1")]
	[Arguments("host(999999999)", "#-1")]
	[Arguments("ipaddr(999999999)", "#-1")]
	[Arguments("ssl(999999999)", "#-1 NOT CONNECTED")]
	[Arguments("terminfo(999999999)", "#-1 NOT CONNECTED")]
	[Arguments("width(999999999)", "78")]
	[Arguments("height(999999999)", "24")]
	public async Task UnusedDescriptor_AnswersTheSameAsAnUnmatchedName(string code, string expected)
		=> await Assert.That(await EvaluateAsync(code)).IsEqualTo(expected);

	/// <summary>
	/// <c>fun_terminfo</c>, <c>fun_width</c> and <c>fun_height</c> are the three that check the
	/// argument before looking anything up. The rest let an empty name fall through the match and fail
	/// as an ordinary miss, which is why they are not listed here.
	/// </summary>
	[Test]
	[Arguments("terminfo()")]
	[Arguments("width()")]
	[Arguments("height()")]
	public async Task EmptyArgument_IsItsOwnError(string code)
		=> await Assert.That(await EvaluateAsync(code))
			.IsEqualTo(ErrorMessages.Returns.FunctionRequiresOneArgument);

	/// <summary>
	/// The second argument is the fallback, and it is used for every failure after the argument check —
	/// PennMUSH reaches <c>args[1]</c> whenever lookup_desc found nothing or the dimension is zero.
	/// </summary>
	[Test]
	[Arguments("width(NoSuchPlayerAnywhere,120)", "120")]
	[Arguments("height(NoSuchPlayerAnywhere,40)", "40")]
	public async Task Dimensions_FallThroughToTheCallersDefault(string code, string expected)
		=> await Assert.That(await EvaluateAsync(code)).IsEqualTo(expected);

	/// <summary>
	/// Reading another player's connection without See_All. PennMUSH folds this into the same answer
	/// as "no such descriptor" for the string and integer functions — whether the descriptor exists is
	/// itself the thing being withheld — and only <c>fun_ssl</c> spends a distinct permission error on
	/// it, because it has already said "#-1 NOT CONNECTED" for the other case.
	/// </summary>
	[Test]
	[Arguments("host", "#-1")]
	[Arguments("ipaddr", "#-1")]
	[Arguments("recv", "-1")]
	[Arguments("sent", "-1")]
	[Arguments("cmds", "-1")]
	[Arguments("ssl", "#-1 PERMISSION DENIED")]
	[NotInParallel(nameof(ConnectionFunctionParityTests))]
	public async Task WithoutSeeAll_AnotherPlayersConnectionIsRefusedInKind(string function, string expected)
	{
		var services = WebAppFactoryArg.Services;
		var mediator = services.GetRequiredService<IMediator>();
		var connectionService = services.GetRequiredService<IConnectionService>();

		// A name per case, because every run creates its own target and a shared name would let one run
		// resolve an earlier run's disconnected player. The target is then addressed by dbref rather
		// than by name: pmatch() and the player-name match behind it do not resolve a player by name in
		// this harness at all (num() finds the same player that pmatch() misses), which is its own bug
		// and would silence this one — every function here answers a failed match and a refused read
		// identically, and only ssl() tells them apart.
		var targetRef = await TestIsolationHelpers.CreateTestPlayerAsync(
			services, mediator, $"ParityTarget{function}");
		var onlookerRef = await TestIsolationHelpers.CreateTestPlayerAsync(
			services, mediator, $"ParityOnlooker{function}");
		var handle = await ConnectAsync(targetRef);
		try
		{
			var asMortal = WebAppFactoryArg.FunctionParserFor(onlookerRef);
			var result = (await asMortal.FunctionParse(MarkupText.Plain($"{function}(#{targetRef.Number})")))!
				.Message!.ToPlainText();

			await Assert.That(result).IsEqualTo(expected);
		}
		finally
		{
			await connectionService.Disconnect(handle);
		}
	}

	/// <summary>
	/// <c>lookup_desc</c> walks the whole connected list and keeps the greatest <c>last_time</c>, so a
	/// player with two clients open is asked about the one they are actually using. Taking whichever
	/// connection the dictionary yielded first got this right only by chance.
	/// </summary>
	[Test, NotInParallel(nameof(ConnectionFunctionParityTests))]
	public async Task TwoConnections_AnswerFromTheLeastIdleOne()
	{
		var services = WebAppFactoryArg.Services;
		var mediator = services.GetRequiredService<IMediator>();
		var connectionService = services.GetRequiredService<IConnectionService>();

		var playerRef = await TestIsolationHelpers.CreateTestPlayerAsync(services, mediator, "TwoClientPlayer");
		var idleHandle = await ConnectAsync(playerRef, terminalType: "IdleClient", idleMilliseconds: 600_000);
		var activeHandle = await ConnectAsync(playerRef, terminalType: "ActiveClient", idleMilliseconds: 0);
		try
		{
			await Assert.That(await EvaluateAsync($"terminfo(#{playerRef.Number})")).StartsWith("ActiveClient");
		}
		finally
		{
			await connectionService.Disconnect(idleHandle);
			await connectionService.Disconnect(activeHandle);
		}
	}

	private static long _handleSeq = 960_000;

	private async Task<long> ConnectAsync(DBRef player, string? terminalType = null, long idleMilliseconds = 0)
	{
		var connectionService = WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
		var handle = Interlocked.Increment(ref _handleSeq);
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var metadata = new ConcurrentDictionary<string, string>(new Dictionary<string, string>
		{
			["ConnectionStartTime"] = now.ToString(),
			["LastConnectionSignal"] = (now - idleMilliseconds).ToString(),
			["InternetProtocolAddress"] = "127.0.0.1",
			["HostName"] = "localhost",
			["ConnectionType"] = "telnet",
			["PresenceClass"] = PresenceClasses.Play
		});

		if (terminalType is not null)
		{
			metadata["TerminalType"] = terminalType;
		}

		await connectionService.Register(handle, "127.0.0.1", "localhost", "telnet",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8, metadata);
		await connectionService.Bind(handle, player);
		return handle;
	}
}
