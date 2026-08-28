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

		await Assert.That(await RunForLastAsync(handle, "SOCKSET WIDTH=100")).IsEqualTo("Width set.");
		await Assert.That(ConnectionService.Get(handle)!.Metadata.GetValueOrDefault("WIDTH")).IsEqualTo("100");
	}

	[Test]
	[Arguments("SOCKSET WIDTH=-1", "Width expects a positive integer.")]
	[Arguments("SOCKSET WIDTH=wide", "Width expects a positive integer.")]
	[Arguments("SOCKSET NOSUCHOPTION=1", "@sockset option 'NOSUCHOPTION' is not a valid option.")]
	[Arguments("SOCKSET WIDTH", "You must give an option and a value.")]
	public async Task SocksetRejectsBadInputWithPennMushWording(string input, string expected)
	{
		var handle = await LoggedInHandleAsync("SocksetBad");

		await Assert.That(await RunForLastAsync(handle, input)).IsEqualTo(expected);
	}

	/// <summary>
	/// PennMUSH parses these with parse_integer and does not validate: a non-numeric argument yields
	/// zero and is stored silently, and no error is reported to a client that is very often a script.
	/// Pinned because "invalid input is rejected" would be the natural but wrong assumption here.
	/// </summary>
	[Test]
	[Arguments("SCREENWIDTH wide", "WIDTH", "0")]
	[Arguments("SCREENHEIGHT tall", "HEIGHT", "0")]
	[Arguments("SCREENWIDTH", "WIDTH", "0")]
	[Arguments("PROMPT_NEWLINES yes", "PROMPT_NEWLINES", "0")]
	public async Task DescriptorSettingsTakeUnparseableValuesAsZeroWithoutComplaining(
		string input, string key, string expected)
	{
		var handle = await LoggedInHandleAsync("BadDescriptor");

		var said = await RunAsync(handle, input);

		await Assert.That(ConnectionService.Get(handle)!.Metadata.GetValueOrDefault(key)).IsEqualTo(expected);
		await Assert.That(said).IsEmpty();
	}

	/// <summary>
	/// The SOCKSET route and the direct command route share no code, so the option engine's
	/// validation must not be assumed to cover the direct one.
	/// </summary>
	[Test]
	public async Task SocksetRejectsAWidthTheDirectCommandWouldHaveAccepted()
	{
		var handle = await LoggedInHandleAsync("SocksetVsDirect");

		await RunAsync(handle, "SCREENWIDTH wide");
		await Assert.That(ConnectionService.Get(handle)!.Metadata.GetValueOrDefault("WIDTH")).IsEqualTo("0");

		await Assert.That(await RunForLastAsync(handle, "SOCKSET WIDTH=wide"))
			.IsEqualTo("Width expects a positive integer.");
		await Assert.That(ConnectionService.Get(handle)!.Metadata.GetValueOrDefault("WIDTH")).IsEqualTo("0");
	}

	/// <summary>
	/// PennMUSH runs both routes through set_userstring, so a whitespace-only value clears rather than
	/// storing spaces. Without this the two routes disagreed: SOCKSET stored "   " and Show() reported
	/// a prefix as set, while the bare command cleared it.
	/// </summary>
	[Test]
	public async Task SocksetClearsAWhitespaceOnlyPrefixJustAsTheCommandDoes()
	{
		var handle = await LoggedInHandleAsync("PrefixBlank");

		await RunAsync(handle, "OUTPUTPREFIX >>");
		await Assert.That(await RunForLastAsync(handle, "SOCKSET OUTPUTPREFIX=   "))
			.IsEqualTo("OUTPUTPREFIX cleared.");
		await Assert.That(ConnectionService.Get(handle)!.Metadata.ContainsKey("OutputPrefix")).IsFalse();
	}

	// --- DOING / SESSION at the connect screen --------------------------------------------------

	/// <summary>
	/// PennMUSH routes WHO, DOING and SESSION to the same <c>dump_users()</c> before login, and only
	/// separates them once connected.
	/// </summary>
	[Test]
	[Arguments("DOING")]
	[Arguments("SESSION")]
	[Arguments("DOINGfoo")]
	[Arguments("SESSIONfoo")]
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
	/// The recorder captures both <c>Notify</c> and <c>NotifyLocalized</c>, which matters because
	/// "Huh?" arrives on the latter: a window that watched only the former would read a rejected
	/// command as silence.
	/// </summary>
	private async ValueTask<string[]> RunAsync(long handle, string input)
	{
		var before = WebAppFactoryArg.Notifications.CountForHandle(handle);

		await Parser.CommandParse(handle, ConnectionService, MModule.single(input));

		return [.. WebAppFactoryArg.Notifications.ForHandle(handle).Skip(before)];
	}

	private async ValueTask<string?> RunForLastAsync(long handle, string input)
		=> (await RunAsync(handle, input)).LastOrDefault();
}
