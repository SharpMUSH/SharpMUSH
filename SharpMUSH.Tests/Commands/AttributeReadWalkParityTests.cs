using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// The single-attribute read walk against PennMUSH's <c>atr_get_with_parent</c>
/// (<c>src/attrib.c:1203-1278</c>) and the pattern reads against <c>atr_iter_get_parent</c>
/// (<c>:1501-1650</c>), end to end through softcode. Tests that write to the type ancestor (#6,
/// ANCESTOR_THING) use attribute names of their own and clear them afterwards.
/// </summary>
public class AttributeReadWalkParityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private int MaxParents
		=> (int)WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Limit.MaxParents;

	/// <summary>ANCESTOR_THING in the seeded configuration.</summary>
	private static readonly DBRef AncestorThing = new(6);

	private static string Uid() => Guid.NewGuid().ToString("N")[..8].ToUpper();

	private async Task Cmd(string command)
		=> await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	/// <summary>Evaluated as God.</summary>
	private async Task<string> Eval(string expression)
		=> (await Parser.FunctionParse(MarkupText.Plain(expression)))?.Message?.ToPlainText() ?? string.Empty;

	/// <summary>Evaluated as the player behind <paramref name="handle"/>.</summary>
	private async Task<string> EvalAs(long handle, string expression)
		=> (await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"think {expression}")))?.Message?.ToPlainText()
			?? string.Empty;

	private Task<DBRef> Thing(string prefix) => TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, prefix);

	private async Task<AnySharpObject> Known(DBRef dbref)
		=> (await Mediator.Send(new GetObjectNodeQuery(dbref))).Expect<AnySharpObject>();

	/// <summary>
	/// A child with <paramref name="parents"/> parents above it, the child first. Built through the
	/// database command, so the chain's length is the test's to choose.
	/// </summary>
	private async Task<DBRef[]> Chain(int parents)
	{
		var chain = new List<DBRef> { await Thing("WalkChain") };
		for (var i = 0; i < parents; i++)
		{
			var next = await Thing("WalkChainP");
			await Mediator.Send(new SetObjectParentCommand(await Known(chain[^1]), await Known(next)));
			chain.Add(next);
		}

		return [.. chain];
	}

	/// <summary>
	/// D1: a zone supplies $-commands, never attributes - <c>atr_get_with_parent</c> does not look at
	/// <c>Zone()</c>.
	/// </summary>
	[Test]
	public async Task AZonesAttributeIsNotReadThroughTheObject()
	{
		var uid = Uid();
		var zone = await Thing("WalkZone");
		var obj = await Thing("WalkZoned");
		await Cmd($"&ZA{uid} {zone}=zone{uid}");
		await Cmd($"@chzone {obj}={zone}");

		await Assert.That(await Eval($"get({obj}/ZA{uid})")).IsEqualTo(string.Empty);
		await Assert.That(await Eval($"hasattrp({obj},ZA{uid})")).IsEqualTo("0");
	}

	/// <summary>
	/// D2: a no_inherit hit on a parent is <c>return NULL</c> out of the whole lookup
	/// (<c>src/attrib.c:1250-1251</c>); the type ancestor's copy is not consulted.
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task NoInheritOnAParentHidesTheAncestorsCopy()
	{
		var uid = Uid();
		try
		{
			await Cmd($"&NI{uid} {AncestorThing}=ancestor{uid}");
			var parent = await Thing("WalkNIP");
			var child = await Thing("WalkNIC");
			var control = await Thing("WalkNIK");
			await Cmd($"&NI{uid} {parent}=parent{uid}");
			await Cmd($"@set {parent}/NI{uid}=no_inherit");
			await Cmd($"@parent {child}={parent}");

			await Assert.That(await Eval($"get({control}/NI{uid})")).IsEqualTo($"ancestor{uid}");
			await Assert.That(await Eval($"get({child}/NI{uid})")).IsEqualTo(string.Empty);
		}
		finally
		{
			await Cmd($"&NI{uid} {AncestorThing}=");
		}
	}

	/// <summary>
	/// D9: <c>atr_match</c> (<c>src/atr_tab.c:112-122</c>) - a unique prefix of a <c>prefixmatch</c>
	/// standard attribute reads that attribute, through the parent chain too; an ambiguous prefix
	/// reads nothing.
	/// </summary>
	[Test]
	public async Task AUniquePrefixOfAStandardAttributeReadsIt()
	{
		var uid = Uid();
		var parent = await Thing("WalkPrefixP");
		var child = await Thing("WalkPrefixC");
		await Cmd($"@desc {parent}=described{uid}");
		await Cmd($"@parent {child}={parent}");

		await Assert.That(await Eval($"get({parent}/DESCR)")).IsEqualTo($"described{uid}");
		await Assert.That(await Eval($"get({child}/DESCRI)")).IsEqualTo($"described{uid}");
		await Assert.That(await Eval($"get({parent}/DES)")).IsEqualTo(string.Empty);
	}

	/// <summary>
	/// D11: <c>while (parent_depth &lt; MAX_PARENTS ...)</c> (<c>src/attrib.c:1222</c>) visits the object
	/// and <c>MAX_PARENTS - 1</c> parents, and a chain still going there never reaches the ancestor.
	/// One parent fewer and the chain ends inside the bound, so the ancestor is read.
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task TheDepthBoundStopsTheWalkBeforeTheLastParentAndTheAncestor()
	{
		var uid = Uid();
		try
		{
			await Cmd($"&DA{uid} {AncestorThing}=ancestor{uid}");
			var full = await Chain(MaxParents);
			var shorter = await Chain(MaxParents - 1);
			await Cmd($"&DL{uid} {full[MaxParents - 1]}=last visited{uid}");
			await Cmd($"&DB{uid} {full[MaxParents]}=beyond{uid}");

			await Assert.That(await Eval($"get({full[0]}/DL{uid})")).IsEqualTo($"last visited{uid}");
			await Assert.That(await Eval($"get({full[0]}/DB{uid})")).IsEqualTo(string.Empty);
			await Assert.That(await Eval($"get({full[0]}/DA{uid})")).IsEqualTo(string.Empty);
			await Assert.That(await Eval($"get({shorter[0]}/DA{uid})")).IsEqualTo($"ancestor{uid}");
		}
		finally
		{
			await Cmd($"&DA{uid} {AncestorThing}=");
		}
	}

	/// <summary>
	/// D3: a literal name takes <c>atr_iter_get_parent</c>'s fast path through
	/// <c>atr_get_with_parent</c> (<c>src/attrib.c:1522-1529</c>), so lattrp, nattrp, xattrp and pgrep
	/// find an attribute held only by the type ancestor, and read an alias as its real name.
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task ALiteralParentListingReadsThroughTheAncestorAndAliases()
	{
		var uid = Uid();
		try
		{
			await Cmd($"&LP{uid} {AncestorThing}=needle{uid}");
			var child = await Thing("WalkLiteral");
			await Cmd($"@desc {child}=described{uid}");

			await Assert.That(await Eval($"lattrp({child}/LP{uid})")).IsEqualTo($"LP{uid}");
			await Assert.That(await Eval($"nattrp({child}/LP{uid})")).IsEqualTo("1");
			await Assert.That(await Eval($"xattrp({child}/LP{uid},1,1)")).IsEqualTo($"LP{uid}");
			await Assert.That(await Eval($"pgrep({child},LP{uid},needle)")).IsEqualTo($"LP{uid}");
			await Assert.That(await Eval($"lattrp({child}/DESC)")).IsEqualTo("DESCRIBE");
			await Assert.That(await Eval($"lattr({child}/DESC)")).IsEqualTo("DESCRIBE");
			// A wildcard pattern walks the @parent chain only (src/attrib.c:1574-1576).
			await Assert.That(await Eval($"lattrp({child}/LP{uid}*)")).IsEqualTo(string.Empty);
		}
		finally
		{
			await Cmd($"&LP{uid} {AncestorThing}=");
		}
	}

	/// <summary>
	/// D10: <c>atr_iter_get_parent</c> lists object by object (<c>src/attrib.c:1574-1580</c>), the
	/// child's matches before its parent's.
	/// </summary>
	[Test]
	public async Task AParentListingShowsTheChildsAttributesFirst()
	{
		var uid = Uid();
		var parent = await Thing("WalkOrderP");
		var child = await Thing("WalkOrderC");
		await Cmd($"&OR{uid}A {child}=a");
		await Cmd($"&OR{uid}Z {child}=z");
		await Cmd($"&OR{uid}B {parent}=b");
		await Cmd($"@parent {child}={parent}");

		await Assert.That(await Eval($"lattrp({child}/OR{uid}*)")).IsEqualTo($"OR{uid}A OR{uid}Z OR{uid}B");
	}

	/// <summary>
	/// D10: an inherited match is tested against the object holding it,
	/// <c>Can_Read_Attr(player, parent, ptr)</c> (<c>src/attrib.c:1584</c>), so a mortal does not list
	/// a non-visual attribute on a parent it does not control - though <c>get()</c>, which tests the
	/// child, still reads it.
	/// </summary>
	[Test]
	public async Task AParentListingTestsVisibilityOnTheParent()
	{
		var uid = Uid();
		var parent = await Thing("WalkVisP");
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WalkVisM");
		await Cmd($"&VH{uid} {parent}=hidden{uid}");
		await Cmd($"&VS{uid} {parent}=shown{uid}");
		await Cmd($"@set {parent}/VS{uid}=visual");
		await Cmd($"@parent {mortal.DbRef}={parent}");

		await Assert.That(await EvalAs(mortal.Handle, $"lattrp(me/V?{uid})")).IsEqualTo($"VS{uid}");
		await Assert.That(await EvalAs(mortal.Handle, $"get(me/VH{uid})")).IsEqualTo($"hidden{uid}");
	}
}
