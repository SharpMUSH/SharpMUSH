using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// The bundled plus-config package, installed at first boot: a hand-made system declares its settings as
/// DATA`CONFIG`&lt;NAME&gt; with leaves under each, registers with +config/register, and +config checks and
/// writes values back to the system's object. Each test registers a system of its own.
/// </summary>
public class PlusConfigIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;

	private async Task<string> God(string command) =>
		(await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command))).Message.ToPlainText()?.Trim() ?? string.Empty;

	/// <summary>What <paramref name="player"/> was told while <paramref name="command"/> ran.</summary>
	private async Task<string> As(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = Notifications.CountFor(player.DbRef);
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		await WebAppFactoryArg.QueueBarrierAsync();
		return string.Join("\n", Notifications.For(player.DbRef).Skip(before));
	}

	/// <summary>A connected player in a room of its own.</summary>
	private async Task<TestIsolationHelpers.TestPlayer> Player(string prefix)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		var home = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName($"{prefix}Room"), god));
		return await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, home);
	}

	/// <summary>A thing declaring one setting of each kind the tests use, and the system name it is registered as.</summary>
	private async Task<(string Object, string System)> SystemAsync(TestIsolationHelpers.TestPlayer staff)
	{
		var obj = await God($"think create({TestIsolationHelpers.GenerateUniqueName("CfgSys")})");
		string[] leaves =
		[
			"DATA`CONFIG=A system under test.",
			"DATA`CONFIG`HOURS=14400", "DATA`CONFIG`HOURS`TYPE=duration", "DATA`CONFIG`HOURS`DEFAULT=14400",
			"DATA`CONFIG`HOURS`MIN=600", "DATA`CONFIG`HOURS`DESCRIPTION=How long a status lasts.",
			"DATA`CONFIG`COLUMNS=2", "DATA`CONFIG`COLUMNS`TYPE=integer", "DATA`CONFIG`COLUMNS`MIN=1", "DATA`CONFIG`COLUMNS`MAX=4",
			"DATA`CONFIG`ORDER=name", "DATA`CONFIG`ORDER`TYPE=choice", "DATA`CONFIG`ORDER`CHOICES=name idle",
			"DATA`CONFIG`KEYS=QUOTE", "DATA`CONFIG`KEYS`TYPE=list", "DATA`CONFIG`KEYS`PATTERN=^[A-Z]+$",
			"DATA`CONFIG`KEYS`DEFAULT=QUOTE", "DATA`CONFIG`KEYS`MIN=1", "DATA`CONFIG`KEYS`MAX=3",
			"DATA`CONFIG`OPEN=0", "DATA`CONFIG`OPEN`TYPE=boolean",
			"DATA`CONFIG`NAME=", "DATA`CONFIG`NAME`TYPE=text", "DATA`CONFIG`NAME`MAX=10",
			"DATA`CONFIG`MATCH=", "DATA`CONFIG`MATCH`TYPE=pattern",
		];
		foreach (var leaf in leaves)
		{
			var (attribute, value) = (leaf[..leaf.IndexOf('=')], leaf[(leaf.IndexOf('=') + 1)..]);
			await God($"&{attribute} {obj}={value}");
		}

		var system = TestIsolationHelpers.GenerateUniqueName("cfg").ToLowerInvariant();
		await Assert.That(await As(staff, $"+config/register {system}={obj}")).Contains($"{system} is ");
		return (obj, system);
	}

	[Test]
	public async Task ValuesAreCheckedAgainstTheirTypeAndStoredOnTheSystemsObject()
	{
		var staff = await Player("CfStaff");
		await God($"@permission/allow #{staff.DbRef.Number}=config.admin");
		var (obj, system) = await SystemAsync(staff);
		try
		{
			var listing = await As(staff, $"+config {system}");
			await Assert.That(listing).Contains("A system under test.").And.Contains("hours").And.Contains("4h")
				.And.Contains("How long a status lasts.");
			await Assert.That(await As(staff, "+config")).Contains(system);

			// A duration is read the way stringsecs() reads it and kept in seconds.
			await Assert.That(await As(staff, $"+config {system}/hours=2h")).Contains($"{system}/hours is now 2h, was 4h.");
			await Assert.That(await God($"think get({obj}/DATA`CONFIG`HOURS)")).IsEqualTo("7200");
			await Assert.That(await As(staff, $"+config {system}/hours=5m")).Contains("It must be at least 10m.");
			await Assert.That(await As(staff, $"+config {system}/hours=soon")).Contains("Give a length of time");
			await Assert.That(await As(staff, $"+config {system}")).Contains("hours*");

			await Assert.That(await As(staff, $"+config {system}/columns=7")).Contains("It must be at most 4.");
			await Assert.That(await As(staff, $"+config {system}/columns=two")).Contains("Give a whole number.");
			await Assert.That(await As(staff, $"+config {system}/order=IDLE")).Contains($"{system}/order is now idle, was name.");
			await Assert.That(await As(staff, $"+config {system}/order=x")).Contains("Choose name or idle.");
			await Assert.That(await As(staff, $"+config {system}/open=yes")).Contains($"{system}/open is now yes, was no.");
			await Assert.That(await God($"think get({obj}/DATA`CONFIG`OPEN)")).IsEqualTo("1");

			// What is typed is stored as typed, never evaluated.
			await Assert.That(await As(staff, $"+config {system}/name=[add(1,2)]")).Contains($"{system}/name is now [add(1,2)], was none.");
			await Assert.That(await God($"think strlen(get({obj}/DATA`CONFIG`NAME))")).IsEqualTo("10");
			await Assert.That(await As(staff, $"+config {system}/name=abcdefghijk")).Contains("Keep it to 10 characters.");
			await Assert.That(await As(staff, $"+config {system}/match=^(a")).Contains("not a regular expression");
			await Assert.That(await As(staff, $"+config {system}/match=^[a-z]+$")).Contains($"{system}/match is now ^[a-z]+$");

			// Lists: each word once, in the order given, and /add, /remove and /reset.
			await Assert.That(await As(staff, $"+config {system}/keys=QUOTE bad")).Contains("bad is not allowed.");
			await Assert.That(await As(staff, $"+config/add {system}/keys=PREFS QUOTE")).Contains($"{system}/keys is now QUOTE, PREFS, was QUOTE.");
			await Assert.That(await As(staff, $"+config/remove {system}/keys=quote")).Contains($"{system}/keys is now PREFS, was QUOTE, PREFS.");
			await Assert.That(await As(staff, $"+config/reset {system}/keys")).Contains($"{system}/keys is now QUOTE, was PREFS.");
			await Assert.That(await As(staff, $"+config/add {system}/order=name")).Contains("+config/add works on lists.");
			await Assert.That(await As(staff, $"+config {system}/keys")).Contains("Words, each matching ^[A-Z]+$, from 1 to 3 items");
			await Assert.That(await As(staff, $"+config/remove {system}/keys=QUOTE")).Contains("It must be at least 1 item.");

			await Assert.That(await As(staff, $"+config {system}/nope=1")).Contains($"{system} has no setting called nope.");
			await Assert.That(await As(staff, "+config nosuchsystem")).Contains("No system is called nosuchsystem.");
			await Assert.That(await As(staff, "+config/bogus")).Contains("+config has no /bogus switch.");
		}
		finally
		{
			await As(staff, $"+config/unregister {system}");
		}
	}

	[Test]
	public async Task OnlyConfigAdminChangesASettingAndTheSystemHearsOfIt()
	{
		var staff = await Player("CpStaff");
		var mortal = await Player("CpMortal");
		await God($"@permission/allow #{staff.DbRef.Number}=config.admin");
		var (obj, system) = await SystemAsync(staff);
		try
		{
			// Anyone may look; changing needs config.admin.
			await Assert.That(await As(mortal, $"+config {system}/columns")).Contains("A whole number from 1 to 4");
			await Assert.That(await As(mortal, $"+config {system}/columns=3")).Contains("needs the config.admin permission.");
			await Assert.That(await As(mortal, $"+config/register x={obj}")).Contains("needs the config.admin permission.");

			// A package's registration leaves with its package, never by hand: scene is installed at first boot.
			await Assert.That(await As(staff, "+config/unregister scene")).Contains("scene is registered by its package");

			// A CHECK on the system's object has the last word, and ACONFIG hears every change.
			await God($"&DATA`CONFIG`COLUMNS`CHECK {obj}=CHECK`COLUMNS");
			await God($"&CHECK`COLUMNS {obj}=if(eq(%0,3),Three is unlucky.)");
			await God($"&ACONFIG {obj}=@pemit %<by>=Heard %<setting> go from %<previous> to %<value>.");
			await Assert.That(await As(staff, $"+config {system}/columns=3")).Contains("Three is unlucky.");
			await Assert.That(await As(staff, $"+config {system}/columns=4")).Contains("Heard columns go from 2 to 4.");

			await Assert.That(await As(staff, $"+config/unregister {system}")).Contains($"{system} is no longer registered.");
			await Assert.That(await As(staff, $"+config {system}")).Contains($"No system is called {system}.");
			await Assert.That(await God($"think get({obj}/DATA`CONFIG`COLUMNS)")).IsEqualTo("4");
		}
		finally
		{
			await As(staff, $"+config/unregister {system}");
		}
	}
}
