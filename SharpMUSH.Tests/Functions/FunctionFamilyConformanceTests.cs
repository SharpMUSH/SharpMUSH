using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// The families #962 folded into one core each, checked for the four things a shared core must keep
/// for every member: markup, delimiters, numeric width and permissions. The differential scenario
/// <c>tools/parity/scenarios/35-function-families.scn</c> runs the same calls against PennMUSH; every
/// expected value here is what PennMUSH 80a1d5b answered there. The harness strips ANSI, so the
/// markup cases are pinned here: each expected value is PennMUSH's <c>decompose()</c> of the result,
/// evaluated back into markup.
/// </summary>
public class FunctionFamilyConformanceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private async Task<MString> Parse(string code)
		=> await FunctionParser.EvaluateAsync(MarkupText.Plain(code));

	private async Task<string> Eval(string code)
		=> (await Parse(code)).ToPlainText();

	private async Task<string> Eval(long handle, string expression)
		=> (await CommandParser.CommandParse(handle, ConnectionService, MarkupText.Plain($"think {expression}")))
			?.Message.ToPlainText() ?? string.Empty;

	private static string Uid() => Guid.NewGuid().ToString("N")[..8].ToUpper();

	/// <summary>
	/// Each core hands back the element, value or branch it was given with its markup intact.
	/// <paramref name="pennDecompose"/> is PennMUSH's <c>decompose()</c> of the same call.
	/// </summary>
	[Test]
	[Arguments("grab(ansi(r,alpha) beta,a*)", "[ansi(r,alpha)]")]
	[Arguments("graball(ansi(r,alpha) [ansi(g,apex)] beta,a*)", "[ansi(r,alpha)]%b[ansi(g,apex)]")]
	[Arguments("setunion(ansi(r,b) a,c)", "a%b[ansi(r,b)]%bc")]
	[Arguments("setdiff(ansi(r,b) a,a)", "[ansi(r,b)]")]
	[Arguments("if(1,ansi(r,x))", "[ansi(r,x)]")]
	[Arguments("ifelse(0,a,ansi(g,y))", "[ansi(g,y)]")]
	public async Task SharedCoresKeepMarkup(string call, string pennDecompose)
	{
		var result = await Parse(call);
		var expected = await Parse(pennDecompose);
		await Assert.That(result.Render(MarkupFormat.Ansi)).IsEqualTo(expected.Render(MarkupFormat.Ansi));
		await Assert.That(result.Render(MarkupFormat.Ansi)).Contains("\u001b[");
	}

	/// <summary><c>get()</c> and <c>xget()</c> read one stored value, markup included.</summary>
	[Test]
	public async Task AttributeReadPairKeepsStoredMarkup()
	{
		var attr = $"FFMARK{Uid()}";
		await Parse($"attrib_set(me/{attr},ansi(r,hi))");
		var expected = (await Parse("ansi(r,hi)")).Render(MarkupFormat.Ansi);

		await Assert.That((await Parse($"get(me/{attr})")).Render(MarkupFormat.Ansi)).IsEqualTo(expected);
		await Assert.That((await Parse($"xget(me,{attr})")).Render(MarkupFormat.Ansi)).IsEqualTo(expected);
	}

	/// <summary>A number wrapped in markup is still a number to the aggregates and to lmath.</summary>
	[Test]
	[Arguments("median(ansi(r,3),1,2)", "2")]
	[Arguments("mean(ansi(r,4),2)", "3")]
	[Arguments("lmath(add,ansi(r,1) 2)", "3")]
	[Arguments("matchall(ansi(r,alpha) beta [ansi(h,alpha)],alpha)", "1 3")]
	public async Task MarkupDoesNotHideNumbersOrMatches(string call, string expected)
		=> await Assert.That(await Eval(call)).IsEqualTo(expected);

	/// <summary>
	/// Input and output delimiters through the shared cores. An empty delimiter is a space
	/// (<c>delim_check</c>, <c>src/function.c:253-255</c>); the wildcard scan took it literally and
	/// found nothing. An output separator given explicitly is used as it is, even empty
	/// (<c>fun_matchall</c>, <c>src/funlist.c:402-407</c>). A set function's fourth argument is its
	/// output separator only when it is empty; otherwise it is the sort type.
	/// </summary>
	[Test]
	[Arguments("grab(a b,b,)", "b")]
	[Arguments("match(a b,b,)", "2")]
	[Arguments("graball(a b ab,*b,)", "b ab")]
	[Arguments("matchall(a b a,a,,)", "13")]
	[Arguments("matchall(a b a,a,,|)", "1|3")]
	[Arguments("graball(a|b|ab,*b,|)", "b|ab")]
	[Arguments("setinter(a|b|c,c|b,|,:)", "b|c")]
	[Arguments("setdiff(a b c,b,,,+)", "a+c")]
	[Arguments("setunion(a b,c d,,)", "abcd")]
	[Arguments("lmath(max,1:5:3,:)", "5")]
	public async Task DelimitersThroughTheSharedCores(string call, string expected)
		=> await Assert.That(await Eval(call)).IsEqualTo(expected);

	/// <summary>
	/// lmath() answers at the width of the scalar function it runs, and the aggregates at theirs.
	/// </summary>
	[Test]
	[Arguments("lmath(add,0.1 0.2)", "0.3")]
	[Arguments("lmath(add,9223372036854775807 1)", "9223372036854775808")]
	[Arguments("add(9223372036854775807,1)", "9223372036854775808")]
	[Arguments("lmath(mul,4294967296 4294967296)", "18446744073709551616")]
	[Arguments("mul(4294967296,4294967296)", "18446744073709551616")]
	[Arguments("lmath(median,1 2 3 4)", "2.5")]
	[Arguments("stddev(1,2,3,4)", "1.290994")]
	[Arguments("lmath(stddev,1 2 3 4)", "1.290994")]
	[Arguments("lmath(min,-0.5 3)", "-0.5")]
	public async Task NumericWidthMatchesTheScalarFunctions(string call, string expected)
		=> await Assert.That(await Eval(call)).IsEqualTo(expected);

	/// <summary>
	/// Called directly, <c>median()</c> and <c>mean()</c> get one empty argument, which is not a
	/// number (<c>math_median</c>, <c>math_mean</c>). <c>lmath()</c> of an empty list passes none, and
	/// <c>stddev()</c> answers 0 for fewer than two values before looking at them.
	/// </summary>
	[Test]
	[Arguments("median()", ErrorMessages.Returns.Numbers)]
	[Arguments("mean()", ErrorMessages.Returns.Numbers)]
	[Arguments("[median()] [mean()]", ErrorMessages.Returns.Numbers + " " + ErrorMessages.Returns.Numbers)]
	[Arguments("stddev()", "0")]
	[Arguments("lmath(median,)", "0")]
	[Arguments("lmath(mean,)", "0")]
	public async Task EmptyAggregateCalls(string call, string expected)
		=> await Assert.That(await Eval(call)).IsEqualTo(expected);

	/// <summary>The same calls typed as a command, the way the differential scenario sends them.</summary>
	[Test]
	public async Task EmptyAggregateCallsFromACommand()
		=> await Assert.That(await Eval(1, "[median()] [mean()] [stddev()] [lmath(median,)] [lmath(mean,)]"))
			.IsEqualTo($"{ErrorMessages.Returns.Numbers} {ErrorMessages.Returns.Numbers} 0 0 0");

	/// <summary>
	/// Without a value <c>attrib_set()</c> clears the attribute (<c>src/fundb.c:2295-2296</c>); it
	/// used to store an empty one. An empty value still creates it. Clearing one that is not there is
	/// PennMUSH's "No such attribute to reset." (<c>src/attrib.c:2411-2412</c>), returned here.
	/// </summary>
	[Test]
	public async Task AttribSetWithoutAValueClears()
	{
		var attr = $"FFSET{Uid()}";

		await Assert.That(await Eval($"[attrib_set(me/{attr},x)][attrib_set(me/{attr})][hasattr(me,{attr})]"))
			.IsEqualTo("0");
		await Assert.That(await Eval($"[attrib_set(me/{attr},)][hasattr(me,{attr})][hasattrval(me,{attr})]"))
			.IsEqualTo("10");
		await Assert.That(await Eval($"[attrib_set(me/{attr})][hasattr(me,{attr})]"))
			.IsEqualTo("0");
		await Assert.That(await Eval($"attrib_set(me/NEVER{attr})"))
			.IsEqualTo(ErrorMessages.Returns.NoSuchAttribute);
	}

	/// <summary>
	/// The clear looks the name up exactly, as <c>find_atr_in_list</c> does (<c>src/attrib.c:1095</c>):
	/// an alias names nothing to clear, and the attribute it would resolve to stays. PennMUSH answers
	/// "No such attribute to reset." and keeps <c>DESCRIBE</c>.
	/// </summary>
	[Test]
	public async Task AttribSetClearsByExactNameNotAlias()
	{
		var thing = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "FamAlias");

		await Assert.That(await Eval($"[attrib_set({thing}/DESCRIBE,d)][attrib_set({thing}/DESC)]|[get({thing}/DESCRIBE)]"))
			.IsEqualTo($"{ErrorMessages.Returns.NoSuchAttribute}|d");
	}

	/// <summary>
	/// <c>do_set_atr</c> asks <c>controls()</c> before anything else (<c>src/attrib.c:2265</c>), so a
	/// caller who may not clear an attribute learns nothing about whether it exists: a missing
	/// attribute and a present one are refused alike.
	/// </summary>
	[Test]
	public async Task AttribSetRefusesAClearBeforeLookingForTheAttribute()
	{
		var uid = Uid();
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "FamClrO");
		var viewer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "FamClrV");
		await CommandParser.CommandParse(owner.Handle, ConnectionService, MarkupText.Plain($"&FFHERE{uid} me=x"));

		await Assert.That(await Eval(viewer.Handle, $"[attrib_set({owner.DbRef}/FFHERE{uid})]|[attrib_set({owner.DbRef}/FFGONE{uid})]"))
			.IsEqualTo($"{ErrorMessages.Returns.AttrSetPermissions}|{ErrorMessages.Returns.AttrSetPermissions}");
	}

	/// <summary>
	/// The four <c>hasattr</c> forms refuse an attribute the caller cannot read with <c>e_perm</c>
	/// (<c>src/fundb.c:255-256</c>), not the <c>NO PERMISSION TO GET ATTRIBUTE</c> of <c>get()</c>,
	/// which the <c>get</c>/<c>xget</c> pair keeps.
	/// </summary>
	[Test]
	public async Task AttributePairsRefuseAnUnreadableAttributeAlike()
	{
		var uid = Uid();
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "FamPermO");
		var viewer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "FamPermV");
		await CommandParser.CommandParse(owner.Handle, ConnectionService, MarkupText.Plain($"&FFSECRET{uid} me=hidden"));

		var target = owner.DbRef.ToString();
		await Assert.That(await Eval(viewer.Handle,
				$"[hasattr({target},FFSECRET{uid})]|[hasattrp({target},FFSECRET{uid})]|[hasattrval({target},FFSECRET{uid})]|[hasattrpval({target},FFSECRET{uid})]"))
			.IsEqualTo(string.Join("|", Enumerable.Repeat(ErrorMessages.Returns.PermissionDenied, 4)));
		await Assert.That(await Eval(viewer.Handle, $"[get({target}/FFSECRET{uid})]|[xget({target},FFSECRET{uid})]"))
			.IsEqualTo($"{ErrorMessages.Returns.AttrPermissions}|{ErrorMessages.Returns.AttrPermissions}");
	}
}
