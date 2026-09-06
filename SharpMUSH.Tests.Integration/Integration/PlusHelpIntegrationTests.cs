using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.ParserInterfaces;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// What <c>+help</c> answers. The stock helpfiles tell players that local commands live under
/// <c>+help</c>; before the <c>plus-help</c> package there was nothing there, so a game's entire
/// installed surface was undiscoverable from inside it.
///
/// <para>These drive the softcode through the command parser rather than asserting registry rows,
/// because the claims worth defending are about what a reader sees: that a package's topics show up
/// without anyone registering them by hand, that a bare name resolves when it is unique and lists
/// candidates when it is not, that a miss points at the engine's <c>help</c> instead of silently
/// rendering it, and that writing a topic is staff-only and stores what was typed.</para>
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
	private readonly ConcurrentDictionary<long, DBRef> _actors = new();

	private async Task<CallState> God1(string command) =>
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

	/// <summary>
	/// What the reader on <paramref name="handle"/> is SHOWN. A $-command answers with @pemit, so
	/// the CallState from CommandParse is empty and the output has to be read out of the
	/// notification recorder.
	///
	/// <para>Handle 1 is God, bound by the factory, so the tests that assert RENDERING rather than
	/// permission drive it through here instead of creating a player. Every player a suite creates
	/// widens the window ProfileApiTests' whole-database read has to survive.</para>
	/// </summary>
	private async Task<IReadOnlyList<string>> RunAs(long handle, string command)
	{
		var actor = handle == 1 ? WebAppFactoryArg.ExecutorDBRef : _actors[handle];
		var before = Notifications.CountFor(actor);
		await Parser.CommandParse(handle, ConnectionService, MModule.single(command));
		return [.. Notifications.For(actor).Skip(before)];
	}

	private static string Joined(IReadOnlyList<string> lines) => string.Join("\n", lines);

	private async Task<string> CreatePlayerAsync(string name, long handle, bool wizard = false, bool pueblo = false)
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

		await ConnectionService.Register(handle, "localhost", "localhost", "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8,
			pueblo
				? new ConcurrentDictionary<string, string>(new Dictionary<string, string> { ["PUEBLO"] = "1" })
				: null);
		await ConnectionService.Bind(handle, parsed.Value);
		_actors[handle] = parsed.Value;
		return dbref;
	}

	/// <summary>
	/// One of the three objects the package creates: the <c>librarian</c> carries the registry, the
	/// commands and the rendering; <c>plus_help_own</c> carries +help's own topics and <c>game_help</c> the
	/// game's, each a source with a HELP tree like any package's. The librarian holds no content.
	/// </summary>
	private async Task<string> ObjectAsync(string reference)
	{
		var objects = await Registry.GetPackageObjectsAsync("plus-help");
		return PackageInstallService.ParseObjid(objects.Single(o => o.Ref == reference).Objid)!.Value.ToString();
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
		await Assert.That(installed.IsT0).IsTrue().Because("plus-help installs at first boot");

		var librarian = await LibrarianAsync();
		var powers = (await God1($"think [powers({librarian})]")).Message?.ToPlainText() ?? string.Empty;
		await Assert.That(powers).Contains("See_All")
			.Because("the librarian reads HELP trees on other packages' objects, scene's WIZARD one included");
	}

	/// <summary>
	/// The claim the whole registry design rests on: a package registers by attaching one SRC leaf,
	/// so the librarian's registry is maintained by the package manager rather than by hand.
	/// </summary>
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
			WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageSourceService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageManifestService>(),
			installer,
			WebAppFactoryArg.Services.GetRequiredService<IPackageAuthoringService>());

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
	/// Every registered source must resolve to a DIFFERENT object.
	///
	/// <para>An installed <c>{{ref}}</c> is a <c>[v(PM`REFS`&lt;REF&gt;)]</c> recall, and the
	/// <c>PM`REFS</c> tree it reads lives on the object the attribute landed on — this librarian —
	/// shared by every package that registers here. <c>PM`REFS</c> namespaces only the cross-package
	/// <c>{{pkg/ref}}</c> form, so two contributors that both name their object <c>help</c> get one
	/// <c>PM`REFS`HELP</c> between them: the later install silently steals the earlier's topics and
	/// its own object is orphaned. That is what happened when scene and plus-help both used
	/// <c>help</c>, and the symptom was scene's topics vanishing while an unrelated bare name turned
	/// ambiguous — a long way from the cause.</para>
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
		const long handle = 9700;
		await CreatePlayerAsync($"Idx{Tag}", handle);

		var said = Joined(await RunAs(handle, "+help"));

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
		const long handle = 9701;
		await CreatePlayerAsync($"Pkg{Tag}", handle);

		var said = Joined(await RunAs(handle, "+help scene"));

		await Assert.That(said).Contains("+scene/join")
			.Because("the scene topic is what tells a player the verb exists");
		await Assert.That(said).DoesNotContain("Huh?");
		await Assert.That(said).DoesNotContain("#-1");
	}

	[Test]
	public async Task AQualifiedName_ReadsTheSameTopicAsTheBareOne()
	{
		await PutLibrarianInMasterRoomAsync();
		const long handle = 9702;
		await CreatePlayerAsync($"Qual{Tag}", handle);

		var bare = Joined(await RunAs(handle, "+help write"));
		var qualified = Joined(await RunAs(handle, "+help plus-help/write"));

		await Assert.That(bare).Contains("+help/write");
		await Assert.That(qualified).Contains("+help/write");
	}

	/// <summary>
	/// A miss says so and points across. It does not render the engine's entry: falling through
	/// would cost +help the ability to answer "does this GAME document this?".
	/// </summary>
	[Test]
	public async Task AMiss_PointsAtTheEnginesHelpInsteadOfRenderingIt()
	{
		await PutLibrarianInMasterRoomAsync();
		const long handle = 9703;
		await CreatePlayerAsync($"Miss{Tag}", handle);

		var said = Joined(await RunAs(handle, "+help @pemit"));

		await Assert.That(said).Contains("No local topic");
		await Assert.That(said).Contains("help @pemit").Because("the miss has to name the command that would work");
		await Assert.That(said).DoesNotContain("Emits a message")
			.Because("+help must not silently render the engine's own entry");
	}

	[Test]
	public async Task Search_MatchesTopicBodiesAsWellAsNames()
	{
		await PutLibrarianInMasterRoomAsync();
		const long handle = 9704;
		await CreatePlayerAsync($"Srch{Tag}", handle);

		var said = Joined(await RunAs(handle, "+help/search markdown"));

		await Assert.That(said).Contains("plus-help/write")
			.Because("the write topic's body says its text is markdown, and search reads bodies");
	}

	[Test]
	public async Task List_NarrowsToOneSource_AndRefusesOneThatIsNotRegistered()
	{
		await PutLibrarianInMasterRoomAsync();
		const long handle = 9705;
		await CreatePlayerAsync($"Lst{Tag}", handle);

		var listed = Joined(await RunAs(handle, "+help/list plus-help"));
		await Assert.That(listed).Contains("plus-help/sources");
		await Assert.That(listed).DoesNotContain("scene/");

		var refused = Joined(await RunAs(handle, "+help/list nosuchsource"));
		await Assert.That(refused).Contains("No such help source");
	}

	/// <summary>
	/// Every shipped topic must EVALUATE. A body is run through <c>u()</c>, so <c>[</c>, <c>(</c>
	/// and <c>)</c> are softcode syntax in it — including inside a markdown code span, because
	/// backticks are markdown and the parser has never heard of them. An unescaped one ends the
	/// expression and the body fails to parse.
	///
	/// <para>Asserted here rather than left to the reader because <c>FUN`GET`RTEXT</c> falls back to
	/// the stored text when evaluation fails, which is the right behaviour for a reader and a very
	/// good way to not notice: the topic still renders, just without any of its evaluated content.
	/// One shipped topic was broken exactly this way, and the only visible symptom was a
	/// cross-reference elsewhere in it rendering as literal brackets.</para>
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
	/// Subtopics are DERIVED from the attribute tree, never declared: HELP`SCENE`JOIN is already a
	/// child of HELP`SCENE. Nothing in scene's manifest lists them, so they cannot drift when a
	/// topic is added or renamed.
	/// </summary>
	[Test]
	public async Task ATopicWithSubtopics_ListsThemFromTheTree()
	{
		await PutLibrarianInMasterRoomAsync();
		var said = Joined(await RunAs(1, "+help scene"));

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
		var said = Joined(await RunAs(1, "+help scene join"));

		await Assert.That(said).DoesNotContain("Subtopics:");
		await Assert.That(said).Contains("See also:").Because("it declares cross-references instead");
	}

	/// <summary>
	/// A subtopic is shown by its short name but must RUN the full one — the reader clicking "join"
	/// under "scene" wants "+help scene join", not "+help join", which resolves to nothing.
	/// </summary>
	[Test]
	public async Task ASubtopicLink_RunsTheFullTopicName()
	{
		await PutLibrarianInMasterRoomAsync();
		const long handle = 9732;
		await CreatePlayerAsync($"Click{Tag}", handle, pueblo: true);

		// Read the RAW notification: For() has already flattened the MString to text, and the MXP
		// markup a command link is made of only exists in the unflattened form.
		var actor = _actors[handle];
		var before = Notifications.RawCountFor(actor);
		await Parser.CommandParse(handle, ConnectionService, MModule.single("+help scene"));
		var markup = string.Join("\n", Notifications.RawFor(actor).Skip(before)
			.Select(m => m.Match(ms => ms.ToString(), str => str)));

		await Assert.That(markup).Contains(">join<")
			.Because("a subtopic is labelled by its short name");
		await Assert.That(markup).Contains("xch_cmd=\"+help scene/scene join\"")
			.Because("but it runs the QUALIFIED full name, which resolves whatever else is installed");
	}

	/// <summary>
	/// See-also is declared in a parallel SEE tree, because a pointer at another SOURCE cannot be
	/// derived. The names render as written and each is clickable.
	/// </summary>
	[Test]
	public async Task SeeAlso_ComesFromTheDeclaredSeeTree()
	{
		await PutLibrarianInMasterRoomAsync();
		var said = Joined(await RunAs(1, "+help pot"));

		await Assert.That(said).Contains("See also:");
		await Assert.That(said).Contains("scene pose").Because("a multi-word name survives the | split");
	}

	// ── The front page ──────────────────────────────────────────────────────

	/// <summary>
	/// The front page renders the "index" TOPIC above the source list, and lists what is installed
	/// without topic counts — a reader choosing where to look is not helped by knowing one source
	/// has four topics.
	/// </summary>
	[Test]
	public async Task TheIndex_RendersTheIndexTopic_AndCountsNothing()
	{
		await PutLibrarianInMasterRoomAsync();
		var said = Joined(await RunAs(1, "+help"));

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
			await God1("+help/write index=Welcome to the game. Ask staff anything.");
			var said = Joined(await RunAs(1, "+help"));

			await Assert.That(said).Contains("Welcome to the game");
			await Assert.That(said).DoesNotContain("this game's own help")
				.Because("the game's index outranks the package's");
		}
		finally
		{
			await God1("+help/delete index");
		}

		var restored = Joined(await RunAs(1, "+help"));
		await Assert.That(restored).Contains("this game's own help")
			.Because("deleting the override hands the front page back to the package");
	}

	// ── Writing ─────────────────────────────────────────────────────────────

	[Test]
	public async Task Write_IsRefusedForAMortal()
	{
		await PutLibrarianInMasterRoomAsync();
		const long handle = 9706;
		await CreatePlayerAsync($"Mort{Tag}", handle);

		var said = Joined(await RunAs(handle, "+help/write policy=Mortals may not write help."));

		await Assert.That(said).Contains("Staff only");

		var stored = (await God1($"think [get({await ObjectAsync("game_help")}/HELP`POLICY)]")).Message?.ToPlainText()?.Trim() ?? "";
		await Assert.That(stored).IsEmpty().Because("the refusal must not have written anything");
	}

	/// <summary>
	/// A topic body is stored as typed and evaluated when READ — that is the whole point of using
	/// u() rather than get(). The command line a player types is itself evaluated once on the way
	/// in, exactly as it is for <c>&amp;attr obj=...</c>, so staff escape the brackets of anything
	/// meant for the reader; what lands in the attribute is then the unresolved code, and it is the
	/// READER's name that comes back, not the writer's.
	/// </summary>
	[Test]
	public async Task Write_StoresTheBodyVerbatim_AndItEvaluatesForTheReader()
	{
		await PutLibrarianInMasterRoomAsync();
		const long staffHandle = 9707;
		const long readerHandle = 9708;
		var staff = $"Staf{Tag}";
		var reader = $"Read{Tag}";
		await CreatePlayerAsync(staff, staffHandle, wizard: true);
		await CreatePlayerAsync(reader, readerHandle);

		var wrote = Joined(await RunAs(staffHandle, @"+help/write applying=Ask for \[name(%%#)\] at the gate."));
		await Assert.That(wrote).Contains("Wrote game/applying");

		var stored = (await God1($"think [get({await ObjectAsync("game_help")}/HELP`APPLYING)]")).Message?.ToPlainText() ?? string.Empty;
		await Assert.That(stored).Contains("[name(%#)]")
			.Because("the escaped code must reach the attribute unresolved; resolving it at write time would freeze the writer's name into the topic");

		var read = Joined(await RunAs(readerHandle, "+help applying"));
		await Assert.That(read).Contains(reader)
			.Because("a topic is evaluated for the reader, so [name(%#)] is the reader's own name");

		await RunAs(staffHandle, "+help/delete applying");
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
		const long staffHandle = 9709;
		await CreatePlayerAsync($"Coll{Tag}", staffHandle, wizard: true);

		// A second source claiming a name plus-help already uses.
		var rival = (await God1($"@create Rival Help {Tag}")).Message?.ToPlainText()?.Trim();
		await God1($"&HELP {rival}=A rival source.");
		await God1($"&HELP`SOURCES {rival}=The rival's own take on sources.");
		await RunAs(staffHandle, $"+help/source rival={rival}");

		var ambiguous = Joined(await RunAs(staffHandle, "+help sources"));
		await Assert.That(ambiguous).Contains("match")
			.Because("two sources claim 'sources', so neither may be picked silently");
		await Assert.That(ambiguous).Contains("plus-help/sources");
		await Assert.That(ambiguous).Contains("rival/sources");

		// The game's own word wins outright.
		await RunAs(staffHandle, "+help/write sources=The game has the last word.");
		var resolved = Joined(await RunAs(staffHandle, "+help sources"));
		await Assert.That(resolved).Contains("last word")
			.Because("a game-authored topic outranks any number of package ones");

		await RunAs(staffHandle, "+help/delete sources");
		await RunAs(staffHandle, "+help/unsource rival");
		await God1($"@destroy {rival}");
	}
}
