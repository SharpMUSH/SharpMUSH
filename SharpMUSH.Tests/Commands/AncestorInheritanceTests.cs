using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// PennMUSH ancestor inheritance: after an object's own @parent chain is exhausted, attribute /
/// $-command / ^-listen lookup falls through to the type ancestor (ANCESTOR_ROOM/PLAYER/EXIT/THING).
/// The default config points the THING ancestor at #6 (Ancestor Thing) and the PLAYER ancestor at
/// #4 (Ancestor Player). These run against the configured provider via the shared factory.
/// </summary>
public class AncestorInheritanceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();

	private static readonly DBRef AncestorThing = new(6);
	private static readonly DBRef God = new(1);

	private async Task<AnySharpObject> Known(DBRef dbref)
		=> (await Mediator.Send(new GetObjectNodeQuery(dbref))).Known;

	[Test]
	[NotInParallel]
	public async Task AncestorOnlyAttribute_IsReadableOnPlainThing()
	{
		// Define an attribute ONLY on the Ancestor Thing (#6).
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&ANCESTOR_ONLY_ATTR {AncestorThing}=from ancestor"));

		// A plain, unrelated thing must inherit it through the type ancestor.
		var thingRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AncInheritPlain");
		var thing = await Known(thingRef);

		var attr = await AttributeService.GetAttributeAsync(thing, thing, "ANCESTOR_ONLY_ATTR",
			IAttributeService.AttributeMode.Read, true);

		await Assert.That(attr.IsAttribute).IsTrue();
		await Assert.That(attr.AsAttribute.Last().Value.ToPlainText()).IsEqualTo("from ancestor");
	}

	[Test]
	[NotInParallel]
	public async Task OwnAttribute_ShadowsAncestor()
	{
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&SHADOW_ATTR {AncestorThing}=ancestor value"));

		var thingRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AncShadow");
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&SHADOW_ATTR {thingRef}=own value"));
		var thing = await Known(thingRef);

		var attr = await AttributeService.GetAttributeAsync(thing, thing, "SHADOW_ATTR",
			IAttributeService.AttributeMode.Read, true);

		await Assert.That(attr.IsAttribute).IsTrue();
		await Assert.That(attr.AsAttribute.Last().Value.ToPlainText()).IsEqualTo("own value");
	}

	[Test]
	[NotInParallel]
	public async Task NoInheritAncestorAttribute_IsNotInherited()
	{
		// Set an attribute on the ancestor and flag it no_inherit.
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&NO_INHERIT_ATTR {AncestorThing}=secret"));
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@set {AncestorThing}/NO_INHERIT_ATTR=no_inherit"));

		var thingRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AncNoInherit");
		var thing = await Known(thingRef);

		var attr = await AttributeService.GetAttributeAsync(thing, thing, "NO_INHERIT_ATTR",
			IAttributeService.AttributeMode.Read, true);

		await Assert.That(attr.IsNone).IsTrue();
	}

	[Test]
	[NotInParallel]
	public async Task AncestorObject_DoesNotSelfLoop()
	{
		// Looking up a missing attribute on the ancestor object itself must not fall through to
		// itself (no self-loop) — it simply resolves to None.
		var ancestor = await Known(AncestorThing);

		var attr = await AttributeService.GetAttributeAsync(ancestor, ancestor, "DEFINITELY_MISSING_ATTR_XYZ",
			IAttributeService.AttributeMode.Read, true);

		await Assert.That(attr.IsNone).IsTrue();
	}

	[Test]
	[NotInParallel]
	public async Task AncestorCommand_FiresForUnrelatedThing()
	{
		// A $-command defined on the Ancestor Thing must be discoverable on a plain thing of that type.
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&CMD`ANCTEST {AncestorThing}=$anctestcmd:@pemit %#=ANCESTOR_CMD_FIRED"));

		var thingRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AncCmdHost");
		var thing = await Known(thingRef);

		var commands = await Mediator.Send(new GetCommandAttributesQuery(thing));
		var names = commands.Select(c => c.Attribute.LongName ?? string.Empty).ToList();

		await Assert.That(names).Contains(x => x.Equals("CMD`ANCTEST", StringComparison.OrdinalIgnoreCase));
	}

	[Test]
	[NotInParallel]
	public async Task AncestorListen_MatchesForPlainThing()
	{
		// A ^-listen pattern defined on the Ancestor Thing should match for a plain thing of that type
		// when parent/ancestor checking is enabled.
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&LISTEN`ANC {AncestorThing}=^anc hears *:@pemit %#=HEARD %1"));
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@set {AncestorThing}/LISTEN`ANC=aahear"));

		var thingRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AncListenHost");
		var thing = await Known(thingRef);
		var god = await Known(God);

		var matcher = WebAppFactoryArg.Services.GetRequiredService<IListenPatternMatcher>();
		var matches = await matcher.MatchListenPatternsAsync(thing, "anc hears hello", god, checkParents: true);

		await Assert.That(matches.Length).IsGreaterThan(0);
	}

	[Test]
	[NotInParallel]
	public async Task PlainPlayer_InheritsFormatSayDefault()
	{
		// A plain player (no own FORMAT`SAY) inherits the default seeded on the Ancestor Player (#4).
		var playerRef = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "AncFormatPlayer");
		var player = await Known(playerRef);

		var attr = await AttributeService.GetAttributeAsync(player, player, "FORMAT`SAY",
			IAttributeService.AttributeMode.Read, true);

		await Assert.That(attr.IsAttribute).IsTrue();
		await Assert.That(attr.AsAttribute.Last().Value.ToPlainText()).Contains("You say");
	}
}
