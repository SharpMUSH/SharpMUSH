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
			"DATA`CONFIG`HOURS`LABEL=Status length", "DATA`CONFIG`HOURS`EXAMPLE=2h", "DATA`CONFIG`LABEL=Test system",
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
				.And.Contains("Status length.").And.Contains("Settings: Test system");
			await Assert.That(await As(staff, $"+config {system}/hours")).Contains("Name:").And.Contains("Status length")
				.And.Contains($"+config {system}/hours=2h");
			await Assert.That(await As(staff, $"+config {system}/label")).Contains($"{system} has no setting called label.");
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

	[Test]
	public async Task ATableKeepsItsEntriesOnItsStoreAndChangesOneFieldAtATime()
	{
		var staff = await Player("CtStaff");
		var mortal = await Player("CtMortal");
		await God($"@permission/allow #{staff.DbRef.Number}=config.admin");
		var (obj, system) = await SystemAsync(staff);
		var store = await God($"think create({TestIsolationHelpers.GenerateUniqueName("CfgStore")})");
		await God($"@set {store}=NO_COMMAND");
		string[] leaves =
		[
			"DATA`CONFIG`RANKS`TYPE=table", $"DATA`CONFIG`RANKS`STORE={store}", "DATA`CONFIG`RANKS`PATH=RANK",
			"DATA`CONFIG`RANKS`ENTRY=title", "DATA`CONFIG`RANKS`FIELDS=title pay", "DATA`CONFIG`RANKS`LABEL=Ranks",
			"DATA`CONFIG`RANKS`KEYPATTERN=^[a-z]+$",
			"DATA`CONFIG`RANKS`FIELD`TITLE`TYPE=text", "DATA`CONFIG`RANKS`FIELD`TITLE`MIN=1", "DATA`CONFIG`RANKS`FIELD`TITLE`LABEL=Title",
			"DATA`CONFIG`RANKS`FIELD`PAY`TYPE=integer", "DATA`CONFIG`RANKS`FIELD`PAY`MIN=0", "DATA`CONFIG`RANKS`FIELD`PAY`DEFAULT=10",
			"DATA`CONFIG`RANKS`FIELD`PAY`CHECK=CHECK`PAY", "CHECK`PAY=if(cand(strmatch(%2,recruit),gt(%0,20)),Recruits earn at most 20 %3.)",
			"ACONFIG=@pemit %<by>=Heard %<action> of %<entry>/%<field> on %<setting>.",
		];
		foreach (var leaf in leaves)
		{
			var (attribute, value) = (leaf[..leaf.IndexOf('=')], leaf[(leaf.IndexOf('=') + 1)..]);
			await God($"&{attribute} {obj}={value}");
		}
		try
		{
			await Assert.That(await As(staff, $"+config {system}")).Contains("0 entries").And.Contains("Entries with title and pay");
			await Assert.That(await As(staff, $"+config {system}/ranks")).Contains("No entries yet.");

			// An entry is added with its ENTRY field; the others start at their defaults, on the store.
			await Assert.That(await As(staff, $"+config/add {system}/ranks/recruit=Recruit")).Contains($"Added {system}/ranks/recruit.")
				.And.Contains($"Heard add of recruit/ on ranks.");
			await Assert.That(await God($"think get({store}/RANK`RECRUIT):[get({store}/RANK`RECRUIT`PAY)]")).IsEqualTo("Recruit:10");
			await Assert.That(await As(staff, $"+config/add {system}/ranks/Recruit2=x")).Contains("An entry's name must match ^[a-z]+$.");
			await Assert.That(await As(mortal, $"+config/add {system}/ranks/scout=Scout")).Contains("needs the config.admin permission.");

			// One field at a time, through its type and its CHECK, which hears the entry and field too.
			await Assert.That(await As(staff, $"+config {system}/ranks/recruit/pay=30")).Contains("Recruits earn at most 20 pay.");
			await Assert.That(await As(staff, $"+config {system}/ranks/recruit/pay=-1")).Contains("It must be at least 0.");
			await Assert.That(await As(staff, $"+config {system}/ranks/recruit/pay=15")).Contains($"{system}/ranks/recruit/pay is now 15, was 10.")
				.And.Contains("Heard set of recruit/pay on ranks.");
			await Assert.That(await As(staff, $"+config {system}/ranks/recruit/title=[add(1,2)]")).Contains("is now [add(1,2)], was Recruit.");
			await Assert.That(await God($"think strlen(get({store}/RANK`RECRUIT))")).IsEqualTo("10");
			await Assert.That(await As(staff, $"+config {system}/ranks/recruit/title=")).Contains("Write at least 1 character.");
			await Assert.That(await As(staff, $"+config {system}/ranks/recruit/rank=1")).Contains("Its fields are title and pay.");
			await Assert.That(await As(staff, $"+config {system}/ranks/nobody/pay=1")).Contains($"{system}/ranks has no entry called nobody.");
			await Assert.That(await As(staff, $"+config {system}/columns/x")).Contains("is one setting, not a table");
			await Assert.That(await As(staff, $"+config/reset {system}/ranks/recruit/pay")).Contains("is now 10, was 15.");
			await Assert.That(await As(mortal, $"+config {system}/ranks")).Contains("recruit").And.Contains("Title: [add(1,2)] · pay: 10");
			await Assert.That(await As(mortal, $"+config {system}/ranks/recruit")).Contains("Field").And.Contains("A whole number, at least 0");

			// FIXED keeps the entries as they are.
			await God($"&DATA`CONFIG`RANKS`FIXED {obj}=1");
			await Assert.That(await As(staff, $"+config/add {system}/ranks/scout=Scout")).Contains("has a fixed set of entries.");
			await Assert.That(await As(staff, $"+config/remove {system}/ranks/recruit")).Contains("has a fixed set of entries.");
			await God($"&DATA`CONFIG`RANKS`FIXED {obj}");

			await Assert.That(await As(staff, $"+config/remove {system}/ranks/recruit")).Contains($"Removed {system}/ranks/recruit.")
				.And.Contains("Heard remove of recruit/ on ranks.");
			await Assert.That(await God($"think lattr({store}/RANK`RECRUIT*)")).IsEqualTo(string.Empty);

			// +config writes only where nothing can run as a command, and only on its system's owner's objects.
			await God($"@set {store}=!NO_COMMAND");
			await Assert.That(await As(staff, $"+config {system}/ranks")).Contains("could run as commands");
			await God($"@set {store}=NO_COMMAND");
			await God($"@chown {store}=#{mortal.DbRef.Number}");
			await Assert.That(await As(staff, $"+config/add {system}/ranks/scout=Scout")).Contains("its STORE must have the same owner");
			await Assert.That(await God($"think lattr({store}/RANK`*)")).IsEqualTo(string.Empty);
		}
		finally
		{
			await As(staff, $"+config/unregister {system}");
		}
	}

	/// <summary>
	/// A new entry's own value goes through its field's CHECK like any other, and that CHECK hears which
	/// entry and field it is checking, as +help config writing promises.
	/// </summary>
	[Test]
	public async Task ANewEntrysValueIsCheckedWithItsFieldNamed()
	{
		var staff = await Player("CnStaff");
		await God($"@permission/allow #{staff.DbRef.Number}=config.admin");
		var (obj, system) = await SystemAsync(staff);
		var store = await God($"think create({TestIsolationHelpers.GenerateUniqueName("CfgStore")})");
		await God($"@set {store}=NO_COMMAND");
		string[] leaves =
		[
			"DATA`CONFIG`RANKS`TYPE=table", $"DATA`CONFIG`RANKS`STORE={store}", "DATA`CONFIG`RANKS`PATH=RANK",
			"DATA`CONFIG`RANKS`ENTRY=title", "DATA`CONFIG`RANKS`FIELDS=title", "DATA`CONFIG`RANKS`LABEL=Ranks",
			"DATA`CONFIG`RANKS`FIELD`TITLE`TYPE=text", "DATA`CONFIG`RANKS`FIELD`TITLE`LABEL=Title",
			"DATA`CONFIG`RANKS`FIELD`TITLE`CHECK=CHECK`TITLE", "CHECK`TITLE=if(strmatch(%2/%3,scout/title),,Checked %2/%3.)",
		];
		foreach (var leaf in leaves)
		{
			var (attribute, value) = (leaf[..leaf.IndexOf('=')], leaf[(leaf.IndexOf('=') + 1)..]);
			await God($"&{attribute} {obj}={value}");
		}
		try
		{
			await Assert.That(await As(staff, $"+config/add {system}/ranks/scout=Scout")).Contains($"Added {system}/ranks/scout.");
			await Assert.That(await God($"think get({store}/RANK`SCOUT)")).IsEqualTo("Scout");
		}
		finally
		{
			await As(staff, $"+config/unregister {system}");
		}
	}
}
