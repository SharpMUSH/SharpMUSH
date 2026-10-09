using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.ParserInterfaces;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// What <c>+help</c> answers, driven through the command parser rather than asserted against
/// registry rows: every claim worth defending here is about what a reader sees.
/// </summary>
[NotInParallel]
public class PlusHelpIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IPackageRegistryService Registry =>
		(IPackageRegistryService)WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	private static readonly string Tag = Guid.NewGuid().ToString("N")[..8];

	private long? _readerHandle;
	private long? _staffHandle;

	/// <summary>
	/// A plain mortal — every reading test drives this. Never God: #1 passes every lock, so a
	/// permission regression would render perfectly and still pass.
	/// </summary>
	private async Task<long> ReaderAsync()
	{
		_readerHandle ??= await CreatePlayerAsync($"Read{Tag}");
		return _readerHandle.Value;
	}

	/// <summary>A wizard, for the staff verbs.</summary>
	private async Task<long> StaffAsync()
	{
		_staffHandle ??= await CreatePlayerAsync($"Staff{Tag}", wizard: true);
		return _staffHandle.Value;
	}

	private readonly ConcurrentDictionary<long, DBRef> _actors = new();

	private async Task<CallState> God1(string command) =>
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	/// <summary>
	/// What the reader on <paramref name="handle"/> is SHOWN: a $-command answers with @pemit, so
	/// CommandParse returns empty and the output comes from the notification recorder. The suite
	/// shares three characters because every player it creates widens the window ProfileApiTests'
	/// whole-database read has to survive.
	/// </summary>
	private async Task<IReadOnlyList<string>> RunAs(long handle, string command)
	{
		var actor = _actors[handle];
		var before = Notifications.CountFor(actor);
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain(command));
		return [.. Notifications.For(actor).Skip(before)];
	}

	private static string Joined(IReadOnlyList<string> lines) => string.Join("\n", lines);

	/// <summary>Creates and connects a player; returns the handle it is connected on.</summary>
	private async Task<long> CreatePlayerAsync(string name, bool wizard = false)
	{
		await God1($"@pcreate {name}=pw-{Tag}-1");
		var dbref = (await God1($"think [pmatch({name})]")).Message?.ToPlainText()?.Trim() ?? string.Empty;
		if (!DBRef.TryParse(dbref, out var parsed) || parsed is null)
		{
			throw new InvalidOperationException($"Failed to create player {name}; pmatch returned '{dbref}'.");
		}

		if (wizard)
		{
			await God1($"@set {dbref}=WIZARD");
		}

		var handle = await TestIsolationHelpers.ConnectTestHandleAsync(ConnectionService, parsed.Value);
		_actors[handle] = parsed.Value;
		return handle;
	}

	/// <summary>
	/// One of the package's three objects: <c>librarian</c> (registry, commands, rendering, no
	/// content), <c>plus_help_own</c> and <c>game_help</c> (each a source with a HELP tree).
	/// </summary>
	private async Task<string> ObjectAsync(string reference)
	{
		var objects = await Registry.GetPackageObjectsAsync("plus-help");
		return DBRef.Parse(objects.Single(o => o.Ref == reference).Objid).ToString();
	}

	private Task<string> LibrarianAsync() => ObjectAsync("librarian");

	/// <summary>
	/// $-commands match only from the enactor's room or the master room, and other suites move
	/// package objects around; put it back before every test that types a command.
	/// </summary>
	private async Task PutLibrarianInMasterRoomAsync() =>
		await God1($"@teleport {await LibrarianAsync()}=#2");

	// ── The package itself ──────────────────────────────────────────────────

	/// <summary>
	/// It installs at first boot, unlike wiki-reader: the stock helpfiles already promise players a
	/// <c>+help</c>, so a game that has not opted into anything still has to answer it.
	/// </summary>
	[Test]
	public async Task IsInstalledAtFirstBoot_WithTheLibrarianInTheMasterRoom()
	{
		var installed = await Registry.GetInstalledPackageAsync("plus-help");
		await Assert.That(installed.Value).IsTypeOf<InstalledPackageRecord>().Because("plus-help installs at first boot");

		var librarian = await LibrarianAsync();
		var powers = (await God1($"think [powers({librarian})]")).Message?.ToPlainText() ?? string.Empty;
		await Assert.That(powers).Contains("See_All")
			.Because("the librarian reads HELP trees on other packages' objects, scene's WIZARD one included");
	}

	/// <summary>A package's topics are readable without anything registering them at runtime.</summary>
	[Test]
	public async Task ContributingPackages_RegisterThemselvesByAttachingASourceLeaf()
	{
		var librarian = await LibrarianAsync();

		var sceneAttached = (await Registry.GetManagedAttributesAsync("scene"))
			.Where(m => m.Attribute.StartsWith("SRC`", StringComparison.Ordinal))
			.ToList();

		await Assert.That(sceneAttached).IsNotEmpty()
			.Because("scene contributes topics, so it attaches SRC`SCENE to the librarian");
		await Assert.That(sceneAttached.Single().Objid).IsEqualTo(librarian)
			.Because("the registration has to land on the librarian, not on scene's own object");

		// Installed softcode never holds a raw dbref: a {{ref}} becomes [v(PM`REFS`NAME)], recalled
		// against the object it lives on. So the leaf is EVALUATED to get the object out of it.
		var registered = (await God1($"think [u({librarian}/SRC`SCENE)]")).Message?.ToPlainText()?.Trim() ?? "";
		await Assert.That(registered).StartsWith("#")
			.Because("the leaf resolves to the object carrying scene's HELP tree");
	}

	/// <summary>
	/// The claim the whole registry design rests on: because a contributor registers by ATTACHING a
	/// SRC leaf to the librarian, the package manager maintains the registry. Install writes the
	/// leaf; uninstall clears it, because uninstall clears a package's managed attributes on objects
	/// it does not own. Nothing registers or deregisters at runtime, so the registry cannot drift
	/// from what is installed.
	///
	/// <para>wiki-reader is the package to prove it with: it ships uninstalled, so this can install
	/// and remove it without changing what the rest of the session's game looks like.</para>
	/// </summary>
	[Test]
	public async Task UninstallingAContributor_TakesItsRegistrationWithIt()
	{
		var librarian = await LibrarianAsync();
		var installer = WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();
		var controller = new PackagesController(
			WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageSourceService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageManifestService>(),
			installer,
			WebAppFactoryArg.Services.GetRequiredService<IPackageAuthoringService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageOperationRunner>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Server.Services.PluginUploadStore>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IAuditLog>());

		// Asked through lattr() rather than get(): the librarian's SOURCE LIST is what the feature
		// reads, and it is the key an install invalidates. A per-attribute get() of a leaf that did
		// not exist yet keeps answering empty after the install that creates it.
		async Task<string> SourcesAsync() =>
			(await God1($"think [lattr({librarian}/SRC`*)]")).Message?.ToPlainText() ?? "";

		await Assert.That(await SourcesAsync()).DoesNotContain("WIKI-READER")
			.Because("wiki-reader ships uninstalled, so it contributes nothing yet");

		try
		{
			var applied = await controller.Apply(
				new ApplyRequest(BundledPackages.RemoteName, "wiki-reader", null, null, null),
				CancellationToken.None);
			await Assert.That(applied.Result).IsTypeOf<OkObjectResult>();

			await Assert.That(await SourcesAsync()).Contains("WIKI-READER")
				.Because("installing a contributor writes its SRC leaf on the librarian");
		}
		finally
		{
			await installer.UninstallAsync("wiki-reader", force: true, CancellationToken.None);
		}

		await Assert.That(await SourcesAsync()).DoesNotContain("WIKI-READER")
			.Because("uninstalling a contributor must take its registration with it, or the librarian "
				+ "would keep offering topics that no longer exist");
	}

	/// <summary>
	/// Every registered source must resolve to a DIFFERENT object. <c>PM`REFS</c> namespaces only the
	/// cross-package <c>{{pkg/ref}}</c> form, so two contributors that both name their object
	/// <c>help</c> share one leaf and the later install steals the earlier's topics.
	/// </summary>
	[Test]
	public async Task EverySourceResolvesToItsOwnObject()
	{
		var librarian = await LibrarianAsync();

		var sources = (await God1($"think [lattr({librarian}/SRC`*)]")).Message!.ToPlainText()
			.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		await Assert.That(sources.Length).IsGreaterThan(1).Because("there are several contributors to collide");

		var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
		var seen = 0;
		foreach (var leaf in sources)
		{
			var obj = (await God1($"think [u({librarian}/{leaf})]")).Message!.ToPlainText().Trim();

			// A leaf whose object is gone contributes nothing and is not an error — that is the
			// documented degradation for a force-removed contributor, and another suite installs and
			// uninstalls wiki-reader while this runs. Only what actually resolves is checked.
			if (!obj.StartsWith('#'))
			{
				continue;
			}

			seen++;
			await Assert.That(resolved.ContainsKey(obj)).IsFalse()
				.Because($"{leaf} resolves to {obj}, which {resolved.GetValueOrDefault(obj)} already claims — "
					+ "two packages have picked the same ref name and share one PM`REFS entry");
			resolved[obj] = leaf;
		}

		await Assert.That(seen).IsGreaterThan(1).Because("the check is only meaningful with several live sources");
	}

	// ── Reading ─────────────────────────────────────────────────────────────

	[Test]
	public async Task TheIndex_ListsEverySourceThatHasTopics()
	{
		await PutLibrarianInMasterRoomAsync();
		var said = Joined(await RunAs(await ReaderAsync(), "+help"));

		await Assert.That(said).Contains("plus-help").Because("the librarian's own topics are a source like any other");
		await Assert.That(said).Contains("scene").Because("scene contributes topics and must appear in the index");
		await Assert.That(said).Contains("+help/search");
		await Assert.That(said).DoesNotContain("#-1");
	}

	/// <summary>The motivating case in the issue: a player who finds the scene browser can learn the verbs.</summary>
	[Test]
	public async Task APackagesTopic_IsReadableWithNoRegistrationStep()
	{
		await PutLibrarianInMasterRoomAsync();
		var said = Joined(await RunAs(await ReaderAsync(), "+help scene"));

		await Assert.That(said).Contains("+scene/join")
			.Because("the scene topic is what tells a player the verb exists");
		await Assert.That(said).DoesNotContain("Huh?");
		await Assert.That(said).DoesNotContain("#-1");
	}

	[Test]
	public async Task AQualifiedName_ReadsTheSameTopicAsTheBareOne()
	{
		await PutLibrarianInMasterRoomAsync();
		var bare = Joined(await RunAs(await ReaderAsync(), "+help write"));
		var qualified = Joined(await RunAs(await ReaderAsync(), "+help plus-help/write"));

		await Assert.That(bare).Contains("+help/write");
		await Assert.That(qualified).Contains("+help/write");
		await Assert.That(bare.Split('\n').Where(l => l.TrimStart().StartsWith('|'))).IsEmpty()
			.Because("a topic has no side borders, so a copied line carries none");
	}

	/// <summary>
	/// A miss points across rather than rendering the engine's entry, which would cost +help the
	/// ability to answer "does this GAME document this?".
	/// </summary>
	[Test]
	public async Task AMiss_PointsAtTheEnginesHelpInsteadOfRenderingIt()
	{
		await PutLibrarianInMasterRoomAsync();
		var said = Joined(await RunAs(await ReaderAsync(), "+help @pemit"));

		await Assert.That(said).Contains("No local topic");
		await Assert.That(said).Contains("help @pemit").Because("the miss has to name the command that would work");
		await Assert.That(said).DoesNotContain("Emits a message")
			.Because("+help must not silently render the engine's own entry");
	}

	[Test]
	public async Task Search_MatchesTopicBodiesAsWellAsNames()
	{
		await PutLibrarianInMasterRoomAsync();
		var said = Joined(await RunAs(await ReaderAsync(), "+help/search markdown"));

		await Assert.That(said).Contains("plus-help/write")
			.Because("the write topic's body says its text is markdown, and search reads bodies");
	}

	[Test]
	public async Task List_NarrowsToOneSource_AndRefusesOneThatIsNotRegistered()
	{
		await PutLibrarianInMasterRoomAsync();
		var listed = Joined(await RunAs(await ReaderAsync(), "+help/list plus-help"));
		await Assert.That(listed).Contains("plus-help/sources");
		await Assert.That(listed).DoesNotContain("scene/");

		var refused = Joined(await RunAs(await ReaderAsync(), "+help/list nosuchsource"));
		await Assert.That(refused).Contains("No such help source");
	}

	/// <summary>
	/// A listing's summary is the topic's first paragraph rendered, not its markdown source:
	/// no code-span backticks or bold asterisks, and a topic opening with a heading is summarised
	/// by the paragraph under it.
	/// </summary>
	[Test]
	public async Task List_SummariesAreRenderedMarkdown()
	{
		await PutLibrarianInMasterRoomAsync();
		var listed = Joined(await RunAs(await ReaderAsync(), "+help/list plus-help"));

		await Assert.That(listed).Contains("+help is this game's own help");
		await Assert.That(listed).DoesNotContain("`").And.DoesNotContain("**");
	}

	/// <summary>
	/// Every shipped topic must EVALUATE — a body runs through <c>u()</c>, so an unescaped
	/// <c>[</c>, <c>(</c> or <c>)</c> ends the expression, markdown code span or not. Asserted
	/// because <c>FUN`GET`RTEXT</c> falls back to the stored text on failure: the topic still
	/// renders, just without any of its evaluated content.
	/// </summary>
	[Test]
	public async Task EveryShippedTopicEvaluates()
	{
		await PutLibrarianInMasterRoomAsync();
		var librarian = await LibrarianAsync();

		var report = (await God1(
			$"think [iter(u({librarian}/FUN`GET`RECORDS),"
			+ $"[u({librarian}/FUN`GET`RNAME,%i0)]=[if(strmatch(u([extract(%i0,2,1,:)]/[extract(%i0,3,1,:)]),#-1*),BROKEN,ok)]"
			+ ",%b,%b)]")).Message!.ToPlainText();

		var entries = report.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		await Assert.That(entries.Length).IsGreaterThan(5).Because("there are topics to check");

		var broken = entries.Where(e => e.EndsWith("=BROKEN", StringComparison.Ordinal)).ToList();
		await Assert.That(broken).IsEmpty()
			.Because($"these topic bodies do not evaluate: {string.Join(", ", broken)}");
	}

	// ── Navigation ──────────────────────────────────────────────────────────

	/// <summary>
	/// Subtopics are DERIVED from the tree, never declared, so they cannot drift: HELP`SCENE`JOIN is
	/// already a child of HELP`SCENE and nothing in scene's manifest lists it.
	/// </summary>
	[Test]
	public async Task ATopicWithSubtopics_ListsThemFromTheTree()
	{
		await PutLibrarianInMasterRoomAsync();
		var said = Joined(await RunAs(await ReaderAsync(), "+help scene"));

		await Assert.That(said).Contains("Subtopics:");
		foreach (var child in new[] { "join", "pitch", "pose", "privacy", "schedule" })
		{
			await Assert.That(said).Contains(child).Because($"'scene {child}' is a child of 'scene'");
		}
	}

	/// <summary>A topic with no children must not print an empty Subtopics line.</summary>
	[Test]
	public async Task ALeafTopic_HasNoSubtopicsLine()
	{
		await PutLibrarianInMasterRoomAsync();
		var said = Joined(await RunAs(await ReaderAsync(), "+help scene join"));

		await Assert.That(said).DoesNotContain("Subtopics:");
		await Assert.That(said).Contains("See also:").Because("it declares cross-references instead");
	}

	/// <summary>
	/// A body is evaluated, so the optional parts of a syntax line are written \[ \]: unescaped, [&lt;id&gt;]
	/// ran as a function call and the reader saw an error where the syntax should be.
	/// </summary>
	[Test]
	public async Task OptionalArguments_AreShownNotRun()
	{
		await PutLibrarianInMasterRoomAsync();
		var said = Joined(await RunAs(await ReaderAsync(), "+help scene schedule"));

		await Assert.That(said).Contains("+scene/pause [<id>][=<when>]");
		await Assert.That(said).Contains("+scene/start [<id>]");
		await Assert.That(said).DoesNotContain("#-1");
	}

	/// <summary>
	/// A subtopic is shown by its short name but must RUN the full one — the reader clicking "join"
	/// under "scene" wants "+help scene join", not "+help join", which resolves to nothing.
	/// </summary>
	[Test]
	public async Task ASubtopicLink_RunsTheFullTopicName()
	{
		await PutLibrarianInMasterRoomAsync();
		var shown = await ShownAsync(await ReaderAsync(), "+help scene");

		await Assert.That(shown.Render(MarkupFormat.Pueblo)).Contains("<A XCH_CMD=\"+help scene/scene join\" XCH_HINT=\"+help scene/scene join\">join</A>")
			.Because("a subtopic is labelled by its short name but runs the QUALIFIED full name");
		await Assert.That(shown.Render(MarkupFormat.Mxp)).Contains("<SEND HREF=\"+help scene/scene join\" HINT=\"+help scene/scene join\">join</SEND>")
			.Because("an MXP client gets MXP's own command link, not Pueblo's");
	}

	/// <summary>
	/// The front page as a reader saw it: every link in it — a source row, and a <c>[topic]</c>
	/// cross-reference in the index text, which runs through the markdown LINK template as the reader
	/// — is a link in each client's own dialect, and none is a tag written into the text, which every
	/// client showed literally as <c>&lt;a xch_cmd=…&gt;</c>.
	/// </summary>
	[Test]
	public async Task TheFrontPage_LinksInEachClientsDialect_AndPrintsNoTagAsText()
	{
		await PutLibrarianInMasterRoomAsync();
		var shown = await ShownAsync(await ReaderAsync(), "+help");

		await Assert.That(shown.ToPlainText()).DoesNotContain("<a ")
			.Because("a tag in the text reaches every client as literal text");
		await Assert.That(shown.Render(MarkupFormat.Mxp)).DoesNotContain("XCH_CMD")
			.Because("XCH_CMD is Pueblo's; an MXP client cannot follow it");
		await Assert.That(shown.Render(MarkupFormat.Mxp)).Contains("<SEND HREF=\"+help/list scene\"")
			.Because("a source row is a link to its listing");
		await Assert.That(shown.Render(MarkupFormat.Pueblo)).Contains("<A XCH_CMD=\"+help write\"")
			.Because("[write] in the index text is a +help cross-reference, made by helplink() for a reader without Send_OOB");
	}

	/// <summary>Every raw notification <paramref name="command"/> sent the reader, as one string of markup.</summary>
	private async Task<MString> ShownAsync(long handle, string command)
	{
		var actor = _actors[handle];
		var before = Notifications.RawCountFor(actor);
		await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain(command));
		return MarkupText.Join(MarkupText.Plain("\n"), Notifications.RawFor(actor).Skip(before)
			.Select(m => m switch
			{
				MString markup => markup,
				string text => MarkupText.Plain(text),
			}));
	}

	/// <summary>
	/// See-also is declared in a parallel SEE tree, because a pointer at another SOURCE cannot be
	/// derived. The names render as written and each is clickable.
	/// </summary>
	[Test]
	public async Task SeeAlso_ComesFromTheDeclaredSeeTree()
	{
		await PutLibrarianInMasterRoomAsync();
		var said = Joined(await RunAs(await ReaderAsync(), "+help pot"));

		await Assert.That(said).Contains("See also:");
		await Assert.That(said).Contains("scene pose").Because("a multi-word name survives the | split");
	}

	// ── The front page ──────────────────────────────────────────────────────

	/// <summary>
	/// The front page renders the "index" TOPIC above the source list, without topic counts.
	/// </summary>
	[Test]
	public async Task TheIndex_RendersTheIndexTopic_AndCountsNothing()
	{
		await PutLibrarianInMasterRoomAsync();
		var said = Joined(await RunAs(await ReaderAsync(), "+help"));

		await Assert.That(said).Contains("Available help");
		await Assert.That(said).Contains("this game's own help").Because("the index topic is rendered");
		await Assert.That(said).Contains("scene").Because("an installed source is listed");
		await Assert.That(said).DoesNotContain(" topics").Because("the front page carries no counts");
	}

	/// <summary>
	/// Because the front page is a topic, a game replaces it by writing its own: the game source
	/// outranks a package's, so "index" resolves to the game's. No separate mechanism.
	/// </summary>
	[Test]
	public async Task TheGameCanReplaceTheFrontPage_ByWritingItsOwnIndexTopic()
	{
		await PutLibrarianInMasterRoomAsync();
		try
		{
			await RunAs(await StaffAsync(), "+help/write index=Welcome to the game. Ask staff anything.");
			var said = Joined(await RunAs(await ReaderAsync(), "+help"));

			await Assert.That(said).Contains("Welcome to the game");
			await Assert.That(said).DoesNotContain("this game's own help")
				.Because("the game's index outranks the package's");
		}
		finally
		{
			await RunAs(await StaffAsync(), "+help/delete index");
		}

		var restored = Joined(await RunAs(await ReaderAsync(), "+help"));
		await Assert.That(restored).Contains("this game's own help")
			.Because("deleting the override hands the front page back to the package");
	}

	// ── Writing ─────────────────────────────────────────────────────────────

	[Test]
	public async Task Write_IsRefusedForAMortal()
	{
		await PutLibrarianInMasterRoomAsync();
		var said = Joined(await RunAs(await ReaderAsync(), "+help/write policy=Mortals may not write help."));

		await Assert.That(said).Contains("Staff only");

		var stored = (await God1($"think [get({await ObjectAsync("game_help")}/HELP`POLICY)]")).Message?.ToPlainText()?.Trim() ?? "";
		await Assert.That(stored).IsEmpty().Because("the refusal must not have written anything");
	}

	/// <summary>
	/// A body is stored as typed and evaluated when READ. The command line is itself evaluated once
	/// on the way in, as <c>&amp;attr obj=...</c> is, so staff escape what is meant for the reader —
	/// and it is the READER's name that comes back, not the writer's.
	/// </summary>
	[Test]
	public async Task Write_StoresTheBodyVerbatim_AndItEvaluatesForTheReader()
	{
		await PutLibrarianInMasterRoomAsync();
		var readerName = $"Read{Tag}";
		await ReaderAsync();

		var wrote = Joined(await RunAs(await StaffAsync(), @"+help/write applying=Ask for \[name(%%#)\] at the gate."));
		await Assert.That(wrote).Contains("Wrote game/applying");

		var stored = (await God1($"think [get({await ObjectAsync("game_help")}/HELP`APPLYING)]")).Message?.ToPlainText() ?? string.Empty;
		await Assert.That(stored).Contains("[name(%#)]")
			.Because("the escaped code must reach the attribute unresolved; resolving it at write time would freeze the writer's name into the topic");

		var read = Joined(await RunAs(await ReaderAsync(), "+help applying"));
		await Assert.That(read).Contains(readerName)
			.Because("a topic is evaluated for the reader, so [name(%#)] is the reader's own name");

		await RunAs(await StaffAsync(), "+help/delete applying");
	}

	/// <summary>A topic named "0" is a topic, and so is a body of "0".</summary>
	[Test]
	public async Task Write_AcceptsZeroAsATopicNameAndAsABody()
	{
		await PutLibrarianInMasterRoomAsync();
		var wrote = Joined(await RunAs(await StaffAsync(), "+help/write 0=0"));

		await Assert.That(wrote).Contains("Wrote game/0");

		var stored = (await God1($"think [get({await ObjectAsync("game_help")}/HELP`0)]")).Message?.ToPlainText()?.Trim() ?? "";
		await Assert.That(stored).IsEqualTo("0");

		await RunAs(await StaffAsync(), "+help/delete 0");
	}

	/// <summary>
	/// A topic name becomes an attribute path, so the PATH is what <c>valid(attrname,…)</c> checks —
	/// the engine's rule, not a character class restating it here.
	/// </summary>
	[Test]
	[Arguments("bad``name", "a doubled backtick is not an attribute name")]
	[Arguments("`leading", "nor is a leading one")]
	public async Task Write_RefusesATopicNameAnAttributeCannotBeCalled(string topic, string why)
	{
		await PutLibrarianInMasterRoomAsync();
		var said = Joined(await RunAs(await StaffAsync(), $"+help/write {topic}=Should never be stored."));

		await Assert.That(said).Contains("storable as an attribute name").Because(why);
	}

	/// <summary>
	/// Storability is not the whole rule: <c>+help</c> reserves three characters of its own, and a
	/// name carrying one would be written and then be unreachable by the syntax that reads it.
	/// </summary>
	[Test]
	[Arguments("bad*name", "* is the wildcard +help <topic> matches on")]
	[Arguments("bad?name", "so is ?")]
	[Arguments("bad/name", "/ separates the source from the topic")]
	public async Task Write_RefusesATopicNameUsingACharacterHelpReserves(string topic, string why)
	{
		await PutLibrarianInMasterRoomAsync();
		var said = Joined(await RunAs(await StaffAsync(), $"+help/write {topic}=Should never be stored."));

		await Assert.That(said).Contains("cannot contain").Because(why);
	}

	/// <summary>
	/// Two sources claiming one name list the qualified candidates rather than picking by install
	/// order — except that the game's own topic outranks a package's, which is the one case where a
	/// bare name still resolves.
	/// </summary>
	[Test]
	public async Task ACollision_ListsCandidates_UnlessTheGameOwnsOneOfThem()
	{
		await PutLibrarianInMasterRoomAsync();

		// A second source claiming a name plus-help already uses.
		var rival = (await God1($"@create Rival Help {Tag}")).Message?.ToPlainText()?.Trim();
		await God1($"&HELP {rival}=A rival source.");
		await God1($"&HELP`SOURCES {rival}=The rival's own take on sources.");
		await RunAs(await StaffAsync(), $"+help/source rival={rival}");

		var ambiguous = Joined(await RunAs(await StaffAsync(), "+help sources"));
		await Assert.That(ambiguous).Contains("match")
			.Because("two sources claim 'sources', so neither may be picked silently");
		await Assert.That(ambiguous).Contains("plus-help/sources");
		await Assert.That(ambiguous).Contains("rival/sources");

		// The game's own word wins outright.
		await RunAs(await StaffAsync(), "+help/write sources=The game has the last word.");
		var resolved = Joined(await RunAs(await StaffAsync(), "+help sources"));
		await Assert.That(resolved).Contains("last word")
			.Because("a game-authored topic outranks any number of package ones");

		await RunAs(await StaffAsync(), "+help/delete sources");
		await RunAs(await StaffAsync(), "+help/unsource rival");
		await God1($"@destroy {rival}");
	}

	// ── Finding a topic by what a player types ──────────────────────────────

	/// <summary>
	/// A player types the command they saw, or a word from it. A leading + and a command's switch are
	/// read as the topic they name, and a command finds the topic that shows it in code.
	/// </summary>
	[Test]
	[Arguments("+help +scene", "scene/scene")]
	[Arguments("+help +scene/join", "scene/scene join")]
	[Arguments("+help event", "scene/scene")]
	[Arguments("+help +events", "scene/scene schedule")]
	[Arguments("+help join", "scene/scene join")]
	public async Task ACommandOrAWordFromOne_FindsItsTopic(string command, string title)
	{
		await PutLibrarianInMasterRoomAsync();
		var said = Joined(await RunAs(await ReaderAsync(), command));

		await Assert.That(said).Contains($"< {title} >").Because($"{command} is about {title}");
		await Assert.That(said).DoesNotContain("No local topic");
	}

	/// <summary>Several matches list the way +help/list does, titled with how they were found.</summary>
	[Test]
	[Arguments("+help sce", "Topics with 'sce' in their names")]
	[Arguments("+help scene*", "Topics matching 'scene*'")]
	public async Task SeveralMatches_AreListedAsAListing(string command, string title)
	{
		await PutLibrarianInMasterRoomAsync();
		var said = Joined(await RunAs(await ReaderAsync(), command));

		await Assert.That(said).Contains(title);
		await Assert.That(said).Contains("scene/scene pitch");
		await Assert.That(said).Contains("page 1 of 1");
		await Assert.That(said).DoesNotContain("Here are the entries");
	}

	[Test]
	public async Task AMiss_OffersBothSearches_InSingleSpaces()
	{
		await PutLibrarianInMasterRoomAsync();
		var said = Joined(await RunAs(await ReaderAsync(), $"+help zq{Tag}"));

		await Assert.That(said).Contains($"No local topic 'zq{Tag}'. Try help zq{Tag} or +help/search zq{Tag}.");
	}

	/// <summary>A page is a last switch, a whole number from 1; anything else is answered, not Huh?.</summary>
	[Test]
	[Arguments("+help/list/0", "Usage: +help/list[/<page>] [<source>]")]
	[Arguments("+help/list/abc", "+help has no /list/abc switch.")]
	[Arguments("+help/search/0 scene", "Usage: +help/search[/<page>] <text>")]
	[Arguments("+help/search", "Usage: +help/search[/<page>] <text>")]
	[Arguments("+help/sources foo", "Usage: +help/sources")]
	[Arguments("+help/sources/2", "+help/sources takes no page number.")]
	[Arguments("+help/0 scene*", "Usage: +help[/<page>] <topic>")]
	[Arguments("+help/list/999", "you asked for page 999.")]
	[Arguments("+help/2 scene*", "There is one page — you asked for page 2.")]
	[Arguments("+help/2 plus-help/write", "There is one page — you asked for page 2.")]
	public async Task AMistypedArgument_IsAnsweredWithTheUsageLine(string command, string usage)
	{
		await PutLibrarianInMasterRoomAsync();
		var said = Joined(await RunAs(await ReaderAsync(), command));

		await Assert.That(said).Contains(usage);
		await Assert.That(said).DoesNotContain("Huh?");
		await Assert.That(said).DoesNotContain("page 0");
	}

	/// <summary>
	/// The page is a last switch after any other: +help/list/2, +help/l/2, and +help/2 for a topic several
	/// match. Each page ends with the command for the next, and the last with none.
	/// </summary>
	[Test]
	public async Task APage_IsALastSwitch()
	{
		await PutLibrarianInMasterRoomAsync();
		var staff = await StaffAsync();
		var reader = await ReaderAsync();
		var marker = $"pg{Tag}";
		const int topics = 20;
		for (var i = 1; i <= topics; i++)
		{
			await RunAs(staff, $"+help/write {marker} {i:D2}=Page test {marker}.");
		}

		try
		{
			var first = Joined(await RunAs(reader, $"+help/search {marker}"));
			var pages = int.Parse(System.Text.RegularExpressions.Regex.Match(first, @"page 1 of (\d+)").Groups[1].Value);
			await Assert.That(pages).IsGreaterThan(1).Because($"{topics} topics are more than one screen");
			await Assert.That(first).Contains($"+help/search/2 {marker}");

			var second = Joined(await RunAs(reader, $"+help/se/2 {marker}"));
			await Assert.That(second).Contains($"page 2 of {pages}");
			var last = Joined(await RunAs(reader, $"+help/search/{pages} {marker}"));
			await Assert.That(last).Contains($"page {pages} of {pages}");
			await Assert.That(last).DoesNotContain($"+help/search/{pages + 1}");

			await Assert.That(Joined(await RunAs(reader, $"+help/2 {marker}*"))).Contains($"page 2 of {pages}")
				.Because("+help/<n> <topic> pages a topic several match");
		}
		finally
		{
			for (var i = 1; i <= topics; i++)
			{
				await RunAs(staff, $"+help/delete {marker} {i:D2}");
			}
		}
	}

	/// <summary>A switch may be cut to any beginning no other switch has; one that names none or several says so.</summary>
	[Test]
	public async Task ASwitch_MayBeCutShort_AndAMistypedOneIsAnswered()
	{
		await PutLibrarianInMasterRoomAsync();
		var reader = await ReaderAsync();

		await Assert.That(Joined(await RunAs(reader, "+help/l plus-help"))).Contains("plus-help/sources");
		await Assert.That(Joined(await RunAs(reader, "+help/lis plus-help"))).Contains("plus-help/sources");
		await Assert.That(Joined(await RunAs(reader, "+help/so"))).Contains("+help/so could be /sources or /source.");
		await Assert.That(Joined(await RunAs(reader, "+help/bogus"))).Contains("+help has no /bogus switch.");
		await Assert.That(Joined(await RunAs(reader, "+help/"))).Contains("+help/ needs a switch.");

		var switches = Joined(await RunAs(reader, "+help/bogus"));
		await Assert.That(switches).Contains("/list /search /sources");
		await Assert.That(switches).DoesNotContain("/write").Because("a reader is offered the switches a reader can use");
	}

	/// <summary>
	/// A line whose brackets do not close cannot be evaluated, so nothing in it runs. It used to say
	/// nothing at all.
	/// </summary>
	[Test]
	public async Task ALineThatDoesNotParse_IsAnswered()
	{
		await PutLibrarianInMasterRoomAsync();
		var said = Joined(await RunAs(await ReaderAsync(), "+help/search ["));

		await Assert.That(said).Contains("PARSER FAILURE");
	}

	/// <summary>The write topic's examples are shown as written, not evaluated for the reader.</summary>
	[Test]
	public async Task TheWriteTopic_ShowsItsExamplesAsWritten()
	{
		await PutLibrarianInMasterRoomAsync();
		var said = Joined(await RunAs(await ReaderAsync(), "+help write"));

		await Assert.That(said).Contains("[tone(info,...)] and [name(%#)] both work");
		await Assert.That(said).Contains("%% shows a %.");
		await Assert.That(said).Contains(@"stores").And.Contains(@"\[write\]");
	}

	/// <summary>A list item's wrapped source line reads with one space where the line broke.</summary>
	[Test]
	[Arguments("+help scene")]
	[Arguments("+help scene pose")]
	[Arguments("+help scene privacy")]
	[Arguments("+help scene schedule")]
	public async Task AWrappedSourceLine_LeavesOneSpace(string command)
	{
		await PutLibrarianInMasterRoomAsync();
		var said = Joined(await RunAs(await ReaderAsync(), command));

		await Assert.That(System.Text.RegularExpressions.Regex.IsMatch(said, @"\S  +\S")).IsFalse()
			.Because($"{command} has no double space inside a line");
	}

	/// <summary>The server's help answers for +help, which a player meets in every game's help index.</summary>
	[Test]
	public async Task TheServersHelp_ExplainsPlusHelp()
	{
		var said = Joined(await RunAs(await ReaderAsync(), "help +help"));

		await Assert.That(said).Contains("+help is this game's");
		await Assert.That(said).DoesNotContain("No entry");
	}
}
