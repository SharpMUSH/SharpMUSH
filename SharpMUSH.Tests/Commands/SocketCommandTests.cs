using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;
using System.Text;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// PennMUSH's socket commands (src/bsd.c <c>do_command</c>) are answered above the
/// <c>d-&gt;connected</c> branch, so every one of them works both at the connect screen and in game.
/// SharpMUSH implemented only WHO, CONNECT and QUIT of that set: <c>INFO</c>, <c>MSSP-REQUEST</c> and
/// the descriptor-state commands answered "Huh?" once logged in and "no such command" before, even
/// though the shipped helpfile documented them. These tests pin both halves of that contract.
/// </summary>
public class SocketCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	/// <summary>A registered but unbound handle — a client sitting on the connect screen.</summary>
	private async ValueTask<long> AnonymousHandleAsync()
	{
		var handle = TestIsolationHelpers.GenerateUniqueHandle();
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

		var reply = await RunForLineAsync(handle, "INFO", "### Begin INFO");

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

		await Assert.That(await RunForLineAsync(handle, "INFO", "### Begin INFO")).IsNotNull().And.Contains("### Begin INFO");
	}

	// --- MSSP-REQUEST ------------------------------------------------------------------------

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task MsspRequestAnswersInBothConnectionStates(bool loggedIn)
	{
		var handle = loggedIn ? await LoggedInHandleAsync("MsspConn") : await AnonymousHandleAsync();

		var reply = await RunForLineAsync(handle, "MSSP-REQUEST", "MSSP-REPLY-START");

		await Assert.That(reply).IsNotNull();
		await Assert.That(reply!).Contains("MSSP-REPLY-START");
		// Terminated even with no admin-defined mssp entries — a crawler reads until this sentinel.
		await Assert.That(reply).Contains("MSSP-REPLY-END");
		await Assert.That(reply).Contains("NAME\t");
		await Assert.That(reply).Contains("PLAYERS\t");
		await Assert.That(reply).Contains("CODEBASE\tSharpMUSH");
		await Assert.That(reply).Contains("FAMILY\tTinyMUD");
		// The same report as the telnet option: a value per line, the preferred charset last.
		await Assert.That(reply).Contains("CHARSET\tISO-8859-1\nCHARSET\tUTF-8");
		await Assert.That(reply).Contains("GMCP\t1");
	}

	// --- VERSION -----------------------------------------------------------------------------

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task VersionAnswersInBothConnectionStates(bool loggedIn)
	{
		var handle = loggedIn ? await LoggedInHandleAsync("VerConn") : await AnonymousHandleAsync();

		await Assert.That(await RunForLineAsync(handle, "VERSION", "You are connected to")).IsNotNull().And.Contains("You are connected to");
	}

	// --- SCREENREADER ------------------------------------------------------------------------

	/// <summary>
	/// For a client with no MTTS option: bare SCREENREADER pins the mode on, off pins it off even over
	/// MTTS, and auto hands it back to what the client said. It answers at the connect screen too.
	/// </summary>
	[Test]
	public async Task ScreenReaderPinsTheModeOnTheConnectionThatTypedIt()
	{
		var handle = await AnonymousHandleAsync();
		var metadata = ConnectionService.Get(handle)!.Metadata;

		await Assert.That(await RunAsync(handle, "SCREENREADER")).Contains("Screen reader set to 'on'");
		await Assert.That(TerminalCapabilityReader.Read(metadata).ScreenReader).IsTrue();

		metadata[TerminalCapabilityReader.TerminalTypesKey] = "MUDLET\tANSI\tSCREEN_READER";
		await Assert.That(await RunAsync(handle, "SCREENREADER off")).Contains("Screen reader set to 'off'");
		await Assert.That(TerminalCapabilityReader.Read(metadata).ScreenReader).IsFalse();

		await RunAsync(handle, "SCREENREADER auto");
		await Assert.That(metadata.ContainsKey(TerminalCapabilityReader.ScreenReaderKey)).IsFalse();
		await Assert.That(TerminalCapabilityReader.Read(metadata).ScreenReader).IsTrue();

		await Assert.That(await RunAsync(handle, "SCREENREADER maybe")).Contains("Unknown setting. Valid settings: 'on', 'off', 'auto'.");
	}

	/// <summary>SOCKSET reaches the same setting and reports it.</summary>
	[Test]
	public async Task SocksetScreenReaderIsTheSameSetting()
	{
		var handle = await LoggedInHandleAsync("SockReader");

		await RunAsync(handle, "SOCKSET screenreader=on");
		await Assert.That(ConnectionService.Get(handle)!.Metadata[TerminalCapabilityReader.ScreenReaderKey]).IsEqualTo("1");
		await Assert.That(await RunForLineAsync(handle, "SOCKSET", "Screen Reader")).IsNotNull().And.Contains("Screen Reader:   on ");
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
		var keepalive = TestIsolationHelpers.GenerateUniqueName("keepalive");
		await Assert.That(await RunAsync(withArgument, $"IDLE {keepalive}")).Contains(keepalive);

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
		// At the connect screen: no @wall or GAME: broadcast reaches it, so its output can be asserted empty (#1247).
		var handle = await AnonymousHandleAsync();

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

		var second = TestIsolationHelpers.GenerateUniqueHandle();
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

		var report = await RunForLineAsync(handle, "SOCKSET", "Prompt Newlines");

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

		await Assert.That(await RunAsync(handle, "SOCKSET WIDTH=100")).Contains("Width set.");
		await Assert.That(ConnectionService.Get(handle)!.Metadata.GetValueOrDefault("WIDTH")).IsEqualTo("100");
	}

	/// <summary>
	/// The terminal-feature settings: each is stored and shown; "auto" hands a link back to what the terminal
	/// reported, and pictures stay off until set. An unknown value is refused rather than stored.
	/// </summary>
	[Test]
	public async Task SocksetPinsTerminalFeatures()
	{
		var handle = await LoggedInHandleAsync("SocksetFeatures");
		var metadata = ConnectionService.Get(handle)!.Metadata;

		await Assert.That(await RunAsync(handle, "SOCKSET HYPERLINKS=on")).Contains("Hyperlinks set to 'on'");
		await Assert.That(await RunAsync(handle, "SOCKSET GRAPHICS=blocks")).Contains("Graphics set to 'blocks'");
		await Assert.That(metadata.GetValueOrDefault("HYPERLINKS")).IsEqualTo("1");
		await Assert.That(metadata.GetValueOrDefault("GRAPHICS")).IsEqualTo("blocks");
		await Assert.That(await RunForLineAsync(handle, "SOCKSET", "Graphics")).IsNotNull().And.Contains("blocks");
		await Assert.That(await RunForLineAsync(handle, "SOCKSET", "Hyperlinks")).IsNotNull().And.Contains("on");

		await Assert.That(await RunAsync(handle, "SOCKSET GRAPHICS=auto")).Contains("Graphics set to 'auto'");
		await Assert.That(metadata.GetValueOrDefault("GRAPHICS")).IsEqualTo("auto");
		await Assert.That(await RunForLineAsync(handle, "SOCKSET", "Graphics")).IsNotNull().And.Contains("auto (blocks)");

		await Assert.That(await RunAsync(handle, "SOCKSET GRAPHICS=off")).Contains("Graphics set to 'off'");
		await Assert.That(metadata.ContainsKey("GRAPHICS")).IsFalse().Because("pictures are off until turned on");

		await Assert.That(await RunAsync(handle, "SOCKSET ANIMATION=on")).Contains("Animation set to 'on'");
		await Assert.That(metadata.GetValueOrDefault("ANIMATION")).IsEqualTo("1");
		await Assert.That(await RunAsync(handle, "SOCKSET TERMINAL=Windows-Terminal")).Contains("Terminal set to 'windows-terminal'");
		await Assert.That(await RunForLineAsync(handle, "SOCKSET", "Terminal ")).IsNotNull().And.Contains("Windows Terminal");
		await Assert.That(await RunAsync(handle, "SOCKSET TERMINAL=auto")).Contains("Terminal set to 'auto'");
		await Assert.That(metadata.ContainsKey("TERMINAL")).IsFalse();
		await Assert.That((await RunAsync(handle, "SOCKSET TERMINAL=teletype")).Any(line => line.StartsWith("Unknown terminal 'teletype'.")))
			.IsTrue();

		await Assert.That(await RunAsync(handle, "SOCKSET GRAPHICS=hologram"))
			.Contains("Unknown graphics setting. Valid settings: 'auto', 'detect', 'kitty', 'iterm2', 'sixel', 'blocks', 'off'.");
		await Assert.That(await RunAsync(handle, "SOCKSET COMMANDLINKS=maybe"))
			.Contains("Unknown setting. Valid settings: 'on', 'off', 'auto'.");
	}

	/// <summary>The character set is pinned, shown and handed back to the client with auto; an unknown one is refused.</summary>
	[Test]
	public async Task SocksetPinsTheCharset()
	{
		var handle = await LoggedInHandleAsync("SocksetCharset");
		var metadata = ConnectionService.Get(handle)!.Metadata;

		await Assert.That(await RunAsync(handle, "SOCKSET CHARSET=US-ASCII")).Contains("Charset set to 'ascii'");
		await Assert.That(metadata.GetValueOrDefault("CHARSET")).IsEqualTo("ascii");
		await Assert.That(TerminalPins.Of(metadata).Charset).IsEqualTo(TerminalCharsets.Ascii);
		await Assert.That(await RunForLineAsync(handle, "SOCKSET", "Charset")).IsNotNull().And.Contains("ascii");

		await Assert.That(await RunAsync(handle, "SOCKSET CHARSET=auto")).Contains("Charset set to 'auto'");
		await Assert.That(metadata.ContainsKey("CHARSET")).IsFalse();
		await Assert.That(await RunAsync(handle, "SOCKSET CHARSET=ebcdic"))
			.Contains("Unknown charset. Valid settings: 'utf-8', 'latin-1', 'ascii', 'auto'.");
	}

	[Test]
	[Arguments("SOCKSET WIDTH=-1", "Width expects a positive integer.")]
	[Arguments("SOCKSET WIDTH=wide", "Width expects a positive integer.")]
	[Arguments("SOCKSET NOSUCHOPTION=1", "@sockset option 'NOSUCHOPTION' is not a valid option.")]
	[Arguments("SOCKSET WIDTH", "You must give an option and a value.")]
	public async Task SocksetRejectsBadInputWithPennMushWording(string input, string expected)
	{
		var handle = await LoggedInHandleAsync("SocksetBad");

		await Assert.That(await RunAsync(handle, input)).Contains(expected);
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
		// At the connect screen, for the same reason as above (#1247).
		var handle = await AnonymousHandleAsync();

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

		await Assert.That(await RunAsync(handle, "SOCKSET WIDTH=wide"))
			.Contains("Width expects a positive integer.");
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
		await Assert.That(await RunAsync(handle, "SOCKSET OUTPUTPREFIX=   "))
			.Contains("OUTPUTPREFIX cleared.");
		await Assert.That(ConnectionService.Get(handle)!.Metadata.ContainsKey("OutputPrefix")).IsFalse();
	}

	// --- LOGOUT ---------------------------------------------------------------------------------

	/// <summary>
	/// PennMUSH <c>logout_sock</c>: the character is left but the socket is not. The connection must
	/// come back at the connect screen, still registered, with no player bound — which is what makes
	/// LOGOUT different from QUIT rather than a slower spelling of it.
	/// </summary>
	[Test]
	public async Task LogoutReleasesTheCharacterAndKeepsTheConnection()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LogoutKeep");

		await RunAsync(player.Handle, "LOGOUT");

		var connection = ConnectionService.Get(player.Handle);

		await Assert.That(connection).IsNotNull();
		await Assert.That(connection!.Ref).IsNull();
		await Assert.That(connection.State).IsEqualTo(IConnectionService.ConnectionState.Connected);
	}

	/// <summary>
	/// <c>welcome_user(d, 0)</c> closes out logout_sock, so the descriptor lands back on the same
	/// screen a new connection sees and can log in again without reconnecting.
	/// </summary>
	[Test]
	public async Task LogoutShowsTheConnectScreenAgain()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LogoutScreen");

		var said = await RunAsync(player.Handle, "LOGOUT");

		await Assert.That(said.Any(m => m.Contains("Welcome to SharpMUSH"))).IsTrue()
			.Because("logout_sock ends by showing the login screen a fresh connection would get");
	}

	/// <summary>
	/// A second login on the same socket must not inherit the first one's screen-scraping wrappers.
	/// PennMUSH clears output_prefix, output_suffix and the command count in logout_sock for exactly
	/// this reason.
	/// </summary>
	[Test]
	public async Task LogoutResetsTheDescriptorTheDepartingPlayerConfigured()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LogoutReset");

		await RunAsync(player.Handle, "OUTPUTPREFIX >>");
		await RunAsync(player.Handle, "OUTPUTSUFFIX <<");
		await Assert.That(ConnectionService.Get(player.Handle)!.Metadata.ContainsKey("OutputPrefix")).IsTrue();

		await RunAsync(player.Handle, "LOGOUT");

		var metadata = ConnectionService.Get(player.Handle)!.Metadata;
		await Assert.That(metadata.ContainsKey("OutputPrefix")).IsFalse();
		await Assert.That(metadata.ContainsKey("OutputSuffix")).IsFalse();
		await Assert.That(ConnectionService.Get(player.Handle)!.CommandCount).IsEqualTo(0);
	}

	/// <summary>
	/// After LOGOUT the connection is anonymous again, so the commands that are only reachable before
	/// login answer it and the in-game ones do not.
	/// </summary>
	[Test]
	public async Task LogoutLeavesTheConnectionAbleToLogInAgain()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LogoutRelogin");

		await RunAsync(player.Handle, "LOGOUT");

		// DOING only shows the WHO listing for a connection with no executor, so a listing here proves
		// the parser now treats this handle as sitting at the connect screen.
		var listing = await RunForLineAsync(player.Handle, "DOING", "Player Name");

		await Assert.That(listing).IsNotNull().And.Contains("Player Name");
	}

	/// <summary>
	/// PennMUSH logs "Logout, never connected. &lt;Connection not dropped&gt;" and does nothing else —
	/// no quit file, no state change, and above all no disturbance to the socket.
	/// </summary>
	[Test]
	public async Task LogoutAtTheConnectScreenIsASilentNoOp()
	{
		var handle = await AnonymousHandleAsync();

		var said = await RunAsync(handle, "LOGOUT");

		await Assert.That(said).IsEmpty();
		await Assert.That(ConnectionService.Get(handle)).IsNotNull();
		await Assert.That(ConnectionService.Get(handle)!.State)
			.IsEqualTo(IConnectionService.ConnectionState.Connected);
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

		var listing = await RunForLineAsync(handle, input, "Player Name");

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

		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain(input));

		return [.. WebAppFactoryArg.Notifications.ForHandle(handle).Skip(before)];
	}

	/// <summary>
	/// Runs one line and returns the line of its output that carries <paramref name="mark"/>, the text
	/// only that reply contains. A logged-in socket also gets every <c>@wall</c> and <c>GAME:</c>
	/// broadcast, so another test's broadcast can land in the window; picking the reply by its own
	/// text, not by position, keeps it out (#1247).
	/// </summary>
	private async ValueTask<string?> RunForLineAsync(long handle, string input, string mark)
		=> (await RunAsync(handle, input)).FirstOrDefault(line => line.Contains(mark));
}
