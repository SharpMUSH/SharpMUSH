using System.Text;
using MarkupString;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.RenderingWorker.Services;
using SharpMUSH.SocketServer.Models;
using SharpMUSH.SocketServer.Services;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Services;

public partial class GameMessageServiceTests
{
	[Test]
	[Arguments(false, false)]
	[Arguments(true, false)]
	[Arguments(false, true)]
	[Arguments(true, true)]
	public async Task UpgradingTheLegacyMessagesObjectKeepsTheTelnetMenu(bool customizedConnect, bool throughBrokenVersion)
	{
		var installer = WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();
		var registry = (IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
		var manifests = new PackageManifestService();
		var id = "messages-upgrade-" + Guid.NewGuid().ToString("N")[..8];
		var previous = manifests.ParseManifest(LegacyMessagesManifest).Expect<ParsedPackageManifest>().Manifest
			with
		{ Name = id, Settings = null };
		var next = manifests.ParseManifest(BundledPackages.ManifestYaml(GameMessages.PackageId)).Expect<ParsedPackageManifest>().Manifest
			with
		{ Name = id, Settings = null };
		PackageApplyRequest Request(IReadOnlyList<PackageConflictDecision> decisions) => new(
			new PackageApplySource("https://github.com/SharpMUSH/SharpMUSH", "examples/packages/messages", "test", "main"),
			new Dictionary<string, string>(), decisions);

		(await installer.ApplyAsync(previous, Request([]))).Expect<PackageApplyResult>();
		try
		{
			var objid = (await registry.GetPackageObjectsAsync(id)).Single().Objid;
			var holder = (await Mediator.Send(new GetObjectNodeQuery(DBRef.Parse(objid)))).Expect<AnySharpObject>();
			if (customizedConnect)
			{
				var connect = previous.Objects.Single().Attributes["CONNECT"].Value;
				await AttributeService.SetAttributeAsync(holder, holder, "CONNECT", MarkupText.Plain(connect + "%rLocal greeting."));
				await AttributeService.SetAttributeAsync(holder, holder, "MOTD",
					MarkupText.Plain(previous.Objects.Single().Attributes["MOTD"].Value + "%rLocal MOTD."));
				await AttributeService.SetAttributeAsync(holder, holder, "NEWUSER",
					MarkupText.Plain(previous.Objects.Single().Attributes["NEWUSER"].Value + "%r[u(FUN`COMMAND,login)]account"));
			}

			if (throughBrokenVersion)
			{
				var oldHelpers = new HashSet<string>(["FUN`MENU", "FUN`SECTION", "FUN`LIST", "FUN`COMMAND", "FUN`ONLINE"]);
				var broken = next with
				{
					Version = new PackageVersion(1, 2, 0),
					Objects = next.Objects.Select(o => o with
					{
						Attributes = o.Attributes.Where(a => !oldHelpers.Contains(a.Key)).ToDictionary()
					}).ToArray()
				};
				var brokenPlan = await installer.PlanAsync(broken);
				(await installer.ApplyAsync(broken, Request(KeepLocalConflicts(brokenPlan)))).Expect<PackageApplyResult>();
			}

			var plan = await installer.PlanAsync(next);
			var connectChange = plan.Attributes.Single(a => a.Attribute == "CONNECT");
			var expected = (customizedConnect, throughBrokenVersion) switch
			{
				(false, false) => PackageAttributeAction.AutoUpgrade,
				(true, false) => PackageAttributeAction.Conflict,
				(false, true) => PackageAttributeAction.NoChange,
				(true, true) => PackageAttributeAction.KeepLocal
			};
			await Assert.That(connectChange.Action).IsEqualTo(expected);
			var decisions = KeepLocalConflicts(plan);
			(await installer.ApplyAsync(next, Request(decisions))).Expect<PackageApplyResult>();

			var shown = (await Build(holder.Object().DBRef.Number).RenderAsync(GameMessage.Connect, 0)).Expect<MString>();
			var wire = Encoding.UTF8.GetString(new MarkupOutputRenderer().Render(MarkupTextSerializer.Serialize(shown),
				new RenderContext("telnet", new ProtocolCapabilities(Width: 78), null)).Data);
			await Assert.That(wire).Contains("Welcome to");
			await Assert.That(wire).Contains("Have an account?");
			await Assert.That(wire).Contains("<name-or-email> <password>");
			await Assert.That(wire).Contains("who is online");
			if (customizedConnect)
			{
				await Assert.That(wire).Contains("Local greeting.");
				var motd = (await Build(holder.Object().DBRef.Number).RenderAsync(GameMessage.Motd, 0, holder)).Expect<MString>().ToPlainText();
				await Assert.That(motd).Contains("Welcome to");
				await Assert.That(motd).Contains("connected.");
				await Assert.That(motd).Contains("Local MOTD.");
				var newUser = (await Build(holder.Object().DBRef.Number).RenderAsync(GameMessage.NewUser, 0)).Expect<MString>().ToPlainText();
				await Assert.That(newUser).Contains("Your account is ready");
				await Assert.That(newUser).Contains("create your first character");
				await Assert.That(newUser).Contains("login   account");
			}
		}
		finally
		{
			(await installer.UninstallAsync(id)).Expect<Success>();
		}
	}

	private static PackageConflictDecision[] KeepLocalConflicts(PackageChangeset plan) => plan.Attributes
		.Where(a => a.Action == PackageAttributeAction.Conflict)
		.Select(a => new PackageConflictDecision(a.TargetRef, a.Attribute, PackageConflictResolution.KeepMine)).ToArray();

	// Actual 1.1.2 manifest before the helper rename in #1954; settings are disabled above to isolate the world.
	private const string LegacyMessagesManifest = """
		format: "1.10"
		package: messages
		version: 1.1.2
		authors: [SharpMUSH]
		description: "The Messages object: the connect screen, the MOTDs and the other server messages as softcode."
		license: MIT
		keywords: [messages, motd, connect, login]
		category: Essentials
		requires_server: ">=0.1"
		settings:
		  messages_object: "{{messages}}"
		objects:
		  - ref: messages
		    type: thing
		    name: Messages
		    flags: [no_command]
		    powers: [Send_OOB]
		    attributes:
		      CONNECT: |-
		        [flex(,item(figure(/assets/logo.png,The SharpMUSH logo,ansi(#00f5b7,v(LOGO))),33),item(u(FUN`MENU),,40))]
		      LOGO: |2-
		                  ==         ==
		                 ====       ====
		               =======     =======
		              =========   =========
		             =========== ===========
		            =================== =====
		          ======-  ===========   ======
		         ======     =========     ======
		        ======   =   =======   =   ======
		        ======   ==   =====   ==   ======
		         =====   ===   ===   ===   =====
		         =====   ====   =   ====   =====
		          ====   =====     =====   ====
		           ===    ====     ====    ===
		           ===     ===     ===-    ===
		            ==     ===     ===     ==
		             =      ==     ==      =
		                     =     =
		      FUN`MENU: |-
		        [@@(The text beside the logo. A way in that the game has turned off is left out.)]Welcome to [ansi(h,mudname())]!%r[if(strmatch(config(player_creation),Yes),%r[u(FUN`SECTION,New here?)]%r[fields(u(FUN`LIST),u(FUN`COMMAND,register),<name> \[email\] <password>,u(FUN`COMMAND,make),<character> <password>)])][if(strmatch(config(guests),Yes),%r[u(FUN`SECTION,Just looking?)]%r[fields(u(FUN`LIST),u(FUN`COMMAND,connect),guest)])]%r[u(FUN`SECTION,Have an account?)]%r[fields(u(FUN`LIST),u(FUN`COMMAND,login),<name-or-email> <password>,u(FUN`COMMAND,play),<character>,u(FUN`COMMAND,claim),<character> <password>)]%r[u(FUN`SECTION,Playing a character?)]%r[fields(u(FUN`LIST),u(FUN`COMMAND,connect),<character> <password>)]%r[u(FUN`SECTION,Any time)]%r[fields(u(FUN`LIST),u(FUN`COMMAND,WHO),who is online,u(FUN`COMMAND,QUIT),leave the game)]
		      FUN`SECTION: |-
		        [@@(%0 heading: a light rule with the heading at its left)][rule(ansi(h,%0),,json(object,border,json(string,single),title,json(string,left)))]
		      FUN`LIST: |-
		        [@@(The options of a list of commands: the arguments after two spaces)][json(object,sep,json(string,%b%b))]
		      FUN`COMMAND: |-
		        [@@(%0 command: padded to the longest, so every list's arguments start in the same column)][ljust(%0,8)]
		      FUN`ONLINE: |-
		        [@@(How many players are connected, in a sentence)][setq(n,words(lwho()))][if(eq(%qn,1),You are the only one connected.,%qn players are connected.)]
		      MOTD: |-
		        %r[u(FUN`SECTION,Welcome to [mudname()])]%rHello, [name(%#)]. [u(FUN`ONLINE)]%rType [cmdlink(help,help,Read the help)] for the commands, or [cmdlink(WHO,WHO,See who is online)] to see who is here.
		      WIZMOTD: |-
		        %r[notice(STAFF,Nothing new for staff. Leave a note here with @wizmotd <message>.,muted)]
		      GUEST: |-
		        %r[notice(GUEST,You are playing [name(%#)]\, a temporary guest character.)]%rType [cmdlink(help,help,Read the help)] for the commands.[if(strmatch(config(player_creation),Yes),%bTo keep a character\, QUIT and register an account.)]
		      NEWUSER: |-
		        %r[u(FUN`SECTION,Your account is ready)]%r[fields(u(FUN`LIST),make <character> <password>,create your first character,claim <character> <password>,add a character you already have,play <character>,enter the game as one of them)]
		      REGISTER: |-
		        %r[notice(,[mudname()] is not taking new accounts.,warn)][if(strmatch(config(guests),Yes),%rYou can still look around: connect guest.)]
		      DOWN: |-
		        %r[notice(,[mudname()] is closed to logins for now. Please try again later.,warn)]
		      QUIT: |-
		        %rGoodbye from [ansi(h,mudname())]. Come back soon!
		""";
}
