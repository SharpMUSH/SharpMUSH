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
/// PennMUSH ancestor inheritance: after an object's own @parent chain is exhausted, attribute lookup
/// falls through to the type ancestor (ANCESTOR_ROOM/PLAYER/EXIT/THING). $-command and ^-listen
/// matching never does (<c>src/attrib.c:1923</c>).
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
		=> (await Mediator.Send(new GetObjectNodeQuery(dbref))).Expect<AnySharpObject>();

	[Test]
	[NotInParallel]
	public async Task AncestorOnlyAttribute_IsReadableOnPlainThing()
	{
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&ANCESTOR_ONLY_ATTR {AncestorThing}=from ancestor"));

		// A plain, unrelated thing must inherit it through the type ancestor.
		var thingRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AncInheritPlain");
		var thing = await Known(thingRef);

		var attr = (await AttributeService.GetAttributeAsync(thing, thing, "ANCESTOR_ONLY_ATTR",
			IAttributeService.AttributeMode.Read, true)).Expect<SharpAttribute[]>();

		await Assert.That(attr.Last().Value.ToPlainText()).IsEqualTo("from ancestor");
	}

	[Test]
	[NotInParallel]
	public async Task OwnAttribute_ShadowsAncestor()
	{
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&SHADOW_ATTR {AncestorThing}=ancestor value"));

		var thingRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AncShadow");
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&SHADOW_ATTR {thingRef}=own value"));
		var thing = await Known(thingRef);

		var attr = (await AttributeService.GetAttributeAsync(thing, thing, "SHADOW_ATTR",
			IAttributeService.AttributeMode.Read, true)).Expect<SharpAttribute[]>();

		await Assert.That(attr.Last().Value.ToPlainText()).IsEqualTo("own value");
	}

	[Test]
	[NotInParallel]
	public async Task NoInheritAncestorAttribute_IsNotInherited()
	{
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&NO_INHERIT_ATTR {AncestorThing}=secret"));
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@set {AncestorThing}/NO_INHERIT_ATTR=no_inherit"));

		var thingRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AncNoInherit");
		var thing = await Known(thingRef);

		var attr = await AttributeService.GetAttributeAsync(thing, thing, "NO_INHERIT_ATTR",
			IAttributeService.AttributeMode.Read, true);

		await Assert.That(attr.IsNone).IsTrue();
	}

	/// <summary>
	/// Task 7 fixed exactly this shape for <c>@parent</c> chains: a
	/// <c>no_inherit</c> flag on ANY level of the branch blocks the whole path across an
	/// inheritance boundary (Penn's <c>atr_get_with_parent</c>, <c>attrib.c:1232-1252</c>, tests
	/// AF_PRIVATE on every backtick-delimited segment). The type-ancestor fall-through kept the
	/// old leaf-only test, so a <c>no_inherit</c> branch on the ancestor still handed out its
	/// leaves.
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task NoInheritOnAncestorBranch_BlocksTheLeaf()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&ANCNI{uid} {AncestorThing}=branchval"));
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&ANCNI{uid}`LEAF {AncestorThing}=leafval"));
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@set {AncestorThing}/ANCNI{uid}=no_inherit"));

		// Control: an identically-shaped, unflagged branch on the same ancestor.
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&ANCOK{uid} {AncestorThing}=okbranch"));
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&ANCOK{uid}`LEAF {AncestorThing}=okleaf"));

		var thingRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AncNoInheritBranch");
		var thing = await Known(thingRef);

		// Proves the type-ancestor fall-through reaches a nested leaf at all, so the miss below
		// is the no_inherit flag rather than tree attributes never inheriting through ANCESTOR_*.
		var control = (await AttributeService.GetAttributeAsync(thing, thing, $"ANCOK{uid}`LEAF",
			IAttributeService.AttributeMode.Read, true))
			.Expect<SharpAttribute[]>("a nested leaf on the type ancestor is inherited by a plain thing");
		await Assert.That(control.Last().Value.ToPlainText()).IsEqualTo("okleaf");

		var attr = await AttributeService.GetAttributeAsync(thing, thing, $"ANCNI{uid}`LEAF",
			IAttributeService.AttributeMode.Read, true);
		await Assert.That(attr.IsNone).IsTrue()
			.Because("no_inherit on the ancestor's branch blocks the whole subtree, not just the flagged node");
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

	/// <summary>
	/// A $-command on the type ancestor is not a $-command of a plain thing. atr_comm_match walks parents
	/// with a NULL use_ancestor (<c>src/attrib.c:1923</c>), and <c>penntop.hlp:279</c> (like our own
	/// <c>sharptop.md</c> "ANCESTORS") says ancestors are not checked for $-commands. An explicit @parent
	/// with the same attribute still counts, so the miss is the ancestor and not the attribute.
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task AncestorCommand_IsNotMatchedOnPlainThing()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var attribute = $"ANCCMD{uid}";
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&{attribute} {AncestorThing}=$anctest{uid}:@pemit %#=ANCESTOR_CMD_FIRED"));
		try
		{
			var thingRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AncCmdHost");
			var commands = await Mediator.Send(new GetCommandAttributesQuery(await Known(thingRef)));
			await Assert.That(commands.Select(c => c.Attribute.LongName))
				.DoesNotContain(x => x.Equals(attribute, StringComparison.OrdinalIgnoreCase));

			var childRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AncCmdChild");
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@parent {childRef}={AncestorThing}"));
			var inherited = await Mediator.Send(new GetCommandAttributesQuery(await Known(childRef)));
			await Assert.That(inherited.Select(c => c.Attribute.LongName))
				.Contains(x => x.Equals(attribute, StringComparison.OrdinalIgnoreCase));
		}
		finally
		{
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&{attribute} {AncestorThing}"));
		}
	}

	/// <summary>
	/// A ^-pattern on the type ancestor is not heard by a LISTEN_PARENT thing: the same NULL use_ancestor
	/// (<c>src/attrib.c:1923</c>) and <c>penntop.hlp:279</c>.
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task AncestorListen_IsNotMatchedForPlainThing()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var attribute = $"ANCLISTEN{uid}";
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&{attribute} {AncestorThing}=^anc{uid} hears *:@pemit %#=HEARD %0"));
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@set {AncestorThing}/{attribute}=aahear"));
		try
		{
			var thingRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AncListenHost");
			var thing = await Known(thingRef);
			var god = await Known(God);

			var matcher = WebAppFactoryArg.Services.GetRequiredService<IListenPatternMatcher>();
			var matches = await matcher.MatchListenPatternsAsync(thing, $"anc{uid} hears hello", god, checkParents: true);

			await Assert.That(matches).IsEmpty();
		}
		finally
		{
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&{attribute} {AncestorThing}"));
		}
	}

	[Test]
	[NotInParallel]
	public async Task PlainPlayer_InheritsFormatSayDefault()
	{
		// A plain player (no own FORMAT`SAY) inherits the default seeded on the Ancestor Player (#4).
		var playerRef = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "AncFormatPlayer");
		var player = await Known(playerRef);

		var attr = (await AttributeService.GetAttributeAsync(player, player, "FORMAT`SAY",
			IAttributeService.AttributeMode.Read, true)).Expect<SharpAttribute[]>();

		await Assert.That(attr.Last().Value.ToPlainText()).Contains("You say");
	}
}
