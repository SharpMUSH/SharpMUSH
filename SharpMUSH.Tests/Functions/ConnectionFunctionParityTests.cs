using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Concurrent;

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
		(await Parser.FunctionParse(MarkupText.Plain(code)))!.Message.ToPlainText();

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
	[Arguments("host(NoSuchPlayerAnywhere)", ErrorMessages.Returns.NoSuchPlayer)]
	[Arguments("ipaddr(NoSuchPlayerAnywhere)", ErrorMessages.Returns.NoSuchPlayer)]
	[Arguments("hidden(NoSuchPlayerAnywhere)", ErrorMessages.Returns.NoSuchPlayer)]
	[Arguments("doing(NoSuchPlayerAnywhere)", "")]
	[Arguments("ports(NoSuchPlayerAnywhere)", "")]
	[Arguments("ssl(NoSuchPlayerAnywhere)", "#-1 NOT CONNECTED")]
	[Arguments("terminfo(NoSuchPlayerAnywhere)", "#-1 NOT CONNECTED")]
	[Arguments("width(NoSuchPlayerAnywhere)", "78")]
	[Arguments("height(NoSuchPlayerAnywhere)", "24")]
	public async Task UnmatchedName_AnswersInEachFunctionsOwnCurrency(string code, string expected)
		=> await Assert.That(await EvaluateAsync(code)).IsEqualTo(expected);

	/// <summary>
	/// A descriptor number nothing is listening on. The integer functions answer as for an unmatched
	/// name — PennMUSH runs both through <c>lookup_desc</c> — and host()/ipaddr() give the one answer
	/// they also give a descriptor the caller may not see.
	/// </summary>
	[Test]
	[Arguments("conn(999999999)", "-1")]
	[Arguments("idle(999999999)", "-1")]
	[Arguments("recv(999999999)", "-1")]
	[Arguments("sent(999999999)", "-1")]
	[Arguments("cmds(999999999)", "-1")]
	[Arguments("host(999999999)", ErrorMessages.Returns.NoSuchDescriptorOrPermissionDenied)]
	[Arguments("ipaddr(999999999)", ErrorMessages.Returns.NoSuchDescriptorOrPermissionDenied)]
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
	/// For the functions that do not check their argument first, an empty name is a name that matches
	/// no player: <c>lookup_player("")</c> and <c>match_result("")</c> both find nothing, so the caller is
	/// not taken to mean themselves. Observed on a live PennMUSH 80a1d5b, as God and as a mortal.
	/// </summary>
	[Test]
	[Arguments("conn()", "-1")]
	[Arguments("idle()", "-1")]
	[Arguments("cmds()", "-1")]
	[Arguments("recv()", "-1")]
	[Arguments("sent()", "-1")]
	[Arguments("ssl()", "#-1 NOT CONNECTED")]
	[Arguments("pueblo()", "#-1 NOT CONNECTED")]
	[Arguments("doing()", "")]
	public async Task EmptyName_MatchesNobody(string code, string expected)
		=> await Assert.That(await EvaluateAsync(code)).IsEqualTo(expected);

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
	/// Reading another player's connection without See_All. Asked by player, the refusal comes before
	/// anything about the connection, so it can say <c>#-1 PERMISSION DENIED</c> without revealing
	/// whether they are connected. The integer functions keep PennMUSH's <c>-1</c>.
	/// </summary>
	[Test]
	[Arguments("host", ErrorMessages.Returns.PermissionDenied)]
	[Arguments("ipaddr", ErrorMessages.Returns.PermissionDenied)]
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

		// Addressed by dbref, because CreateTestPlayerAsync names its player <prefix>_<millis>_<random>
		// and hands back only the DBRef. A lookup on the bare prefix would resolve by partial match —
		// or, once several runs have created one, resolve an earlier run's disconnected player, which
		// reads as "not connected" rather than "refused". Only ssl() tells those two apart, so it is
		// the one case that would fail, and it would be blaming the wrong thing.
		var targetRef = await TestIsolationHelpers.CreateTestPlayerAsync(
			services, mediator, $"ParityTarget{function}");
		var onlookerRef = await TestIsolationHelpers.CreateTestPlayerAsync(
			services, mediator, $"ParityOnlooker{function}");
		var handle = await ConnectAsync(targetRef);
		try
		{
			var asMortal = WebAppFactoryArg.FunctionParserFor(onlookerRef);
			var result = (await asMortal.FunctionParse(MarkupText.Plain($"{function}(#{targetRef.Number})")))!
				.Message.ToPlainText();

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

	/// <summary>
	/// <c>lookup_desc</c>'s descriptor-number branch (<c>src/bsd.c:6634-6641</c>) answers only a Priv_Who
	/// caller, or a caller asking about their own descriptor. A mortal who names someone else's descriptor
	/// finds nothing, so every function answers as it does for a number nobody is on. Observed on a live
	/// PennMUSH 80a1d5b, a mortal naming another mortal's descriptor.
	/// </summary>
	[Test]
	[Arguments("conn", "-1")]
	[Arguments("idle", "-1")]
	[Arguments("cmds", "-1")]
	[Arguments("recv", "-1")]
	[Arguments("sent", "-1")]
	[Arguments("ssl", "#-1 NOT CONNECTED")]
	[Arguments("pueblo", "#-1 NOT CONNECTED")]
	[Arguments("terminfo", "#-1 NOT CONNECTED")]
	[Arguments("width", "78")]
	[Arguments("height", "24")]
	[NotInParallel(nameof(ConnectionFunctionParityTests))]
	public async Task WithoutSeeAll_AnotherPlayersDescriptorNumberFindsNothing(string function, string expected)
	{
		var (targetRef, onlookerRef) = await TargetAndOnlookerAsync($"DescNumber{function}");
		var handle = await ConnectAsync(targetRef);
		try
		{
			await Assert.That(await EvaluateAsAsync(onlookerRef, $"{function}({handle})")).IsEqualTo(expected);
			await Assert.That(await EvaluateAsync($"{function}({handle})")).IsNotEqualTo(expected);
		}
		finally
		{
			await WebAppFactoryArg.Services.GetRequiredService<IConnectionService>().Disconnect(handle);
		}
	}

	/// <summary>
	/// Asked by name, <c>lookup_desc</c> finds another player's connection for anyone, and only the
	/// functions that read an address or traffic count refuse it afterwards. Observed on a live PennMUSH
	/// 80a1d5b: a mortal's <c>conn()</c>, <c>idle()</c>, <c>pueblo()</c>, <c>width()</c> and
	/// <c>terminfo()</c> answer about another connected mortal.
	/// </summary>
	[Test]
	[Arguments("pueblo", "0")]
	[Arguments("width", "100")]
	[Arguments("height", "40")]
	[NotInParallel(nameof(ConnectionFunctionParityTests))]
	public async Task WithoutSeeAll_AnotherPlayerAskedByNameIsFound(string function, string expected)
	{
		var (targetRef, onlookerRef) = await TargetAndOnlookerAsync($"ByName{function}");
		var handle = await ConnectAsync(targetRef);
		try
		{
			await Assert.That(await EvaluateAsAsync(onlookerRef, $"{function}(#{targetRef.Number})"))
				.IsEqualTo(expected);
		}
		finally
		{
			await WebAppFactoryArg.Services.GetRequiredService<IConnectionService>().Disconnect(handle);
		}
	}

	/// <summary>
	/// <c>lookup_desc</c> skips a connection hidden with <c>@hide</c> unless the caller is Priv_Who
	/// (<c>src/bsd.c:6655-6660</c>), so a hidden player is not connected as far as a mortal can tell.
	/// PennMUSH 80a1d5b, live, with a mortal asking about a player who had run <c>@hide/on</c>. host()
	/// and ipaddr() stay the permission error they give for any other player.
	/// </summary>
	[Test]
	[Arguments("conn", "-1")]
	[Arguments("idle", "-1")]
	[Arguments("ssl", "#-1 NOT CONNECTED")]
	[Arguments("pueblo", "#-1 NOT CONNECTED")]
	[Arguments("terminfo", "#-1 NOT CONNECTED")]
	[Arguments("width", "78")]
	[NotInParallel(nameof(ConnectionFunctionParityTests))]
	public async Task AHiddenConnectionIsNotConnectedExceptToSeeAll(string function, string expected)
	{
		var (targetRef, onlookerRef) = await TargetAndOnlookerAsync($"Hidden{function}");
		var handle = await ConnectAsync(targetRef, hidden: true);
		try
		{
			await Assert.That(await EvaluateAsAsync(onlookerRef, $"{function}(#{targetRef.Number})"))
				.IsEqualTo(expected);
			await Assert.That(await EvaluateAsync($"{function}(#{targetRef.Number})")).IsNotEqualTo(expected);
		}
		finally
		{
			await WebAppFactoryArg.Services.GetRequiredService<IConnectionService>().Disconnect(handle);
		}
	}

	private async Task<string> EvaluateAsAsync(DBRef executor, string code) =>
		(await WebAppFactoryArg.FunctionParserFor(executor).FunctionParse(MarkupText.Plain(code)))!
			.Message.ToPlainText();

	private async Task<(DBRef Target, DBRef Onlooker)> TargetAndOnlookerAsync(string label)
	{
		var services = WebAppFactoryArg.Services;
		var mediator = services.GetRequiredService<IMediator>();
		return (await TestIsolationHelpers.CreateTestPlayerAsync(services, mediator, $"Target{label}"),
			await TestIsolationHelpers.CreateTestPlayerAsync(services, mediator, $"Onlooker{label}"));
	}

	private async Task<long> ConnectAsync(DBRef player, string? terminalType = null, long idleMilliseconds = 0,
		bool hidden = false)
	{
		var connectionService = WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var metadata = new ConcurrentDictionary<string, string>(new Dictionary<string, string>
		{
			["ConnectionStartTime"] = now.ToString(),
			["LastConnectionSignal"] = (now - idleMilliseconds).ToString(),
			["InternetProtocolAddress"] = "127.0.0.1",
			["HostName"] = "localhost",
			["ConnectionType"] = "telnet",
			["PresenceClass"] = PresenceClasses.Play,
			["WIDTH"] = "100",
			["HEIGHT"] = "40",
			["Hidden"] = hidden ? "1" : "0"
		});

		if (terminalType is not null)
		{
			metadata["TerminalType"] = terminalType;
		}

		return await TestIsolationHelpers.ConnectTestHandleAsync(connectionService, player, "telnet", metadata);
	}
}
