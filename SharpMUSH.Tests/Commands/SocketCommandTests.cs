using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.Core;
using OneOf;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Text;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// PennMUSH's socket commands (src/bsd.c <c>do_command</c>) are answered above the
/// <c>d-&gt;connected</c> branch, so every one of them works both at the connect screen and in game.
/// SharpMUSH implemented only WHO, CONNECT and QUIT of that set: <c>INFO</c>, <c>MSSP-REQUEST</c> and
/// the descriptor-state commands answered "Huh?" once logged in and "no such command" before, even
/// though the shipped helpfile documented them. These tests pin both halves of that contract.
/// </summary>
[NotInParallel]
public class SocketCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private INotifyService Notify => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();

	/// <summary>A registered but unbound handle — a client sitting on the connect screen.</summary>
	private async ValueTask<long> AnonymousHandleAsync()
	{
		var handle = Random.Shared.NextInt64(900_000, 999_999);
		await ConnectionService.Register(handle, "localhost", "localhost", "telnet",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		return handle;
	}

	private async ValueTask<long> LoggedInHandleAsync(string namePrefix)
		=> (await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, namePrefix)).Handle;

	// --- INFO --------------------------------------------------------------------------------

	[Test]
	public async Task InfoAnswersAtTheConnectScreen()
	{
		var handle = await AnonymousHandleAsync();

		var reply = await RunForLastAsync(handle, "INFO");

		await Assert.That(reply).IsNotNull();
		await Assert.That(reply!).Contains("### Begin INFO 1.1");
		await Assert.That(reply).Contains("### End INFO");
		await Assert.That(reply).Contains("Name: ");
		await Assert.That(reply).Contains("Connected: ");
		await Assert.That(reply).Contains("Size: ");
	}

	/// <summary>
	/// The reported bug: INFO is a socket command, so logging in must not take it away.
	/// </summary>
	[Test]
	public async Task InfoAnswersWhileConnected()
	{
		var handle = await LoggedInHandleAsync("InfoConn");

		await Assert.That(await RunForLastAsync(handle, "INFO")).IsNotNull().And.Contains("### Begin INFO");
	}

	// --- MSSP-REQUEST ------------------------------------------------------------------------

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task MsspRequestAnswersInBothConnectionStates(bool loggedIn)
	{
		var handle = loggedIn ? await LoggedInHandleAsync("MsspConn") : await AnonymousHandleAsync();

		var reply = await RunForLastAsync(handle, "MSSP-REQUEST");

		await Assert.That(reply).IsNotNull();
		await Assert.That(reply!).Contains("MSSP-REPLY-START");
		// Terminated even with no admin-defined mssp entries — a crawler reads until this sentinel.
		await Assert.That(reply).Contains("MSSP-REPLY-END");
		await Assert.That(reply).Contains("NAME\t");
		await Assert.That(reply).Contains("PLAYERS\t");
		await Assert.That(reply).Contains("CODEBASE\tSharpMUSH");
		await Assert.That(reply).Contains("FAMILY\tTinyMUD");
	}

	// --- VERSION -----------------------------------------------------------------------------

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task VersionAnswersInBothConnectionStates(bool loggedIn)
	{
		var handle = loggedIn ? await LoggedInHandleAsync("VerConn") : await AnonymousHandleAsync();

		await Assert.That(await RunForLastAsync(handle, "VERSION")).IsNotNull().And.Contains("You are connected to");
	}

	// --- IDLE --------------------------------------------------------------------------------

	/// <summary>
	/// PennMUSH echoes whatever follows IDLE, consuming one separating space, and stays silent when
	/// nothing follows. Keepalive clients rely on the echo to prove the socket is still live.
	/// </summary>
	[Test]
	public async Task IdleEchoesItsArgumentAndIsOtherwiseSilent()
	{
		var withArgument = await LoggedInHandleAsync("IdleEcho");
		await Assert.That(await RunForLastAsync(withArgument, "IDLE keepalive")).IsEqualTo("keepalive");

		var bare = await AnonymousHandleAsync();
		await Assert.That(await RunAsync(bare, "IDLE")).IsEmpty();
	}

	/// <summary>
	/// IDLE must not count as activity — that is the entire point of it in PennMUSH, where the
	/// command is handled above the lines that stamp <c>d-&gt;last_time</c> and bump <c>d-&gt;cmds</c>.
	/// </summary>
	[Test]
	public async Task IdleDoesNotBumpTheCommandCount()
	{
		var handle = await LoggedInHandleAsync("IdleQuiet");

		await RunAsync(handle, "VERSION");
		var before = ConnectionService.Get(handle)!.CommandCount;

		await RunAsync(handle, "IDLE");

		await Assert.That(ConnectionService.Get(handle)!.CommandCount).IsEqualTo(before);
	}

	// --- SCREENWIDTH / SCREENHEIGHT / PROMPT_NEWLINES ------------------------------------------

	[Test]
	[Arguments("SCREENWIDTH 132", "WIDTH", "132")]
	[Arguments("SCREENHEIGHT 50", "HEIGHT", "50")]
	[Arguments("PROMPT_NEWLINES 1", "PROMPT_NEWLINES", "1")]
	[Arguments("PROMPT_NEWLINES 0", "PROMPT_NEWLINES", "0")]
	public async Task DescriptorSettingsWriteTheirMetadataSilently(string input, string key, string expected)
	{
		var handle = await LoggedInHandleAsync("Descriptor");

		var said = await RunAsync(handle, input);

		await Assert.That(ConnectionService.Get(handle)!.Metadata.GetValueOrDefault(key)).IsEqualTo(expected);
		await Assert.That(said).IsEmpty();
	}

	/// <summary>
	/// The setting belongs to the socket that typed it. A player with two clients open who resizes
	/// one must not have the other's width rewritten — the bug the old OUTPUTPREFIX had, which walked
	/// the player's connections and took whichever was listed first.
	/// </summary>
	[Test]
	public async Task DescriptorSettingsDoNotLeakToTheSamePlayersOtherConnection()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "TwoClients");

		var second = Random.Shared.NextInt64(900_000, 999_999);
		await ConnectionService.Register(second, "localhost", "localhost", "telnet",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		await ConnectionService.Bind(second, player.DbRef);

		await RunAsync(second, "SCREENWIDTH 200");

		await Assert.That(ConnectionService.Get(second)!.Metadata.GetValueOrDefault("WIDTH")).IsEqualTo("200");
		await Assert.That(ConnectionService.Get(player.Handle)!.Metadata.ContainsKey("WIDTH")).IsFalse();
	}

	// --- OUTPUTPREFIX / OUTPUTSUFFIX -----------------------------------------------------------

	/// <summary>
	/// PennMUSH sets these silently on the typing descriptor and accepts them before login, so a
	/// screen-scraping client can bracket the connect screen too.
	/// </summary>
	[Test]
	public async Task OutputPrefixIsSetSilentlyOnTheConnectScreenAndClearsWhenEmpty()
	{
		var handle = await AnonymousHandleAsync();

		await RunAsync(handle, "OUTPUTPREFIX >>");
		await Assert.That(ConnectionService.Get(handle)!.Metadata.GetValueOrDefault("OutputPrefix")).IsEqualTo(">>");

		await RunAsync(handle, "OUTPUTPREFIX");
		await Assert.That(ConnectionService.Get(handle)!.Metadata.ContainsKey("OutputPrefix")).IsFalse();
	}

	// --- SOCKSET -------------------------------------------------------------------------------

	[Test]
	public async Task SocksetWithNoArgumentReportsTheDescriptorSettings()
	{
		var handle = await LoggedInHandleAsync("SocksetShow");

		var report = await RunForLastAsync(handle, "SOCKSET");

		await Assert.That(report).IsNotNull();
		await Assert.That(report!).Contains("Width");
		await Assert.That(report).Contains("Height");
		await Assert.That(report).Contains("Terminal Type");
		await Assert.That(report).Contains("Prompt Newlines");
	}

	[Test]
	public async Task SocksetSetsAnOptionAndConfirmsIt()
	{
		var handle = await LoggedInHandleAsync("SocksetSet");

		await Assert.That(await RunForLastAsync(handle, "SOCKSET WIDTH=100")).IsEqualTo("SocksetWidthSet");
		await Assert.That(ConnectionService.Get(handle)!.Metadata.GetValueOrDefault("WIDTH")).IsEqualTo("100");
	}

	[Test]
	// The expectations are resource keys, not English: every one of these answers goes through
	// NotifyLocalized, and the key names the branch the command took more precisely than its wording.
	[Arguments("SOCKSET WIDTH=-1", "SocksetWidthNeedsPositiveInteger")]
	[Arguments("SOCKSET WIDTH=wide", "SocksetWidthNeedsPositiveInteger")]
	[Arguments("SOCKSET NOSUCHOPTION=1", "SocksetInvalidOptionFormat")]
	[Arguments("SOCKSET WIDTH", "SocksetNeedsOptionAndValue")]
	public async Task SocksetRejectsBadInput(string input, string expected)
	{
		var handle = await LoggedInHandleAsync("SocksetBad");

		await Assert.That(await RunForLastAsync(handle, input)).IsEqualTo(expected);
	}

	// --- DOING / SESSION at the connect screen --------------------------------------------------

	/// <summary>
	/// PennMUSH routes WHO, DOING and SESSION to the same <c>dump_users()</c> before login, and only
	/// separates them once connected.
	/// </summary>
	[Test]
	[Arguments("DOING")]
	[Arguments("SESSION")]
	public async Task DoingAndSessionShowTheWhoListingAtTheConnectScreen(string input)
	{
		var handle = await AnonymousHandleAsync();

		var listing = await RunForLastAsync(handle, input);

		await Assert.That(listing).IsNotNull();
		await Assert.That(listing!).Contains("Player Name");
		await Assert.That(listing).Contains("Doing");
	}

	// --- helpers --------------------------------------------------------------------------------

	/// <summary>
	/// Runs one line of input and returns only what that line produced. Registering a handle already
	/// sends it the connect screen, so the notifications are windowed around the command rather than
	/// read from the whole session — otherwise "this command says nothing" can never be asserted.
	/// Both <c>Notify</c> and <c>NotifyLocalized</c> are collected: "Huh?" arrives on the latter, and
	/// a test that watched only the former would read a rejected command as silence.
	/// </summary>
	private async ValueTask<string[]> RunAsync(long handle, string input)
	{
		var before = Notify.ReceivedCalls().Count();

		await Parser.CommandParse(handle, ConnectionService, MModule.single(input));

		return Notify.ReceivedCalls().Skip(before)
			.Where(call => call.GetArguments() is [long h, ..] && h == handle)
			.Select(TextOf)
			.OfType<string>()
			.ToArray();
	}

	private async ValueTask<string?> RunForLastAsync(long handle, string input)
		=> (await RunAsync(handle, input)).LastOrDefault();

	private static string? TextOf(ICall call) => call.GetMethodInfo().Name switch
	{
		nameof(INotifyService.Notify) when call.GetArguments() is [_, OneOf<MString, string> message, ..]
			=> message.Match(ms => ms.ToPlainText(), s => s),
		// The localized overload carries a resource key rather than text; the key is what a test wants
		// to see, since it names the branch the command took.
		nameof(INotifyService.NotifyLocalized) when call.GetArguments() is [_, string key, ..] => key,
		_ => null
	};
}
