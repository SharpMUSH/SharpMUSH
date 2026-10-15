using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// RhostMUSH's <c>u2()</c>, <c>u2default()</c>, <c>u2local()</c>, <c>u2ldefault()</c> and
/// <c>privatize()</c>. In RhostMUSH's default build u() runs the attribute as the object holding it
/// and u2() runs it as the caller; the expected results are what an unmodified RhostMUSH server
/// answers, with its object numbers swapped for this world's.
/// </summary>
public class RhostU2FunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private async Task<string> Eval(string code) =>
		(await Parser.FunctionParse(MarkupText.Plain(code)))?.Message?.ToPlainText() ?? string.Empty;

	/// <summary>A thing of its own carrying the attributes the cases call, and the caller's dbref.</summary>
	private async Task<(string Thing, string Caller)> CreateThingAsync()
	{
		var created = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService,
			TestIsolationHelpers.GenerateUniqueName("U2Obj"));
		var thing = $"#{DBRef.Parse(created.Message.ToPlainText()).Number}";
		foreach (var (attribute, value) in new[]
						{
							("X", "[num(me)]|%!|%@|%0"),
							("Q", "%0-%1"),
							("Z", "[setq(0,inner)]in"),
							("V", $"[u2({thing}/X,v)]"),
							("VA", "(before R0: [r(0)])[setq(0,internal)] --My R0:[r(0)]--"),
							("VB", "[setr(0,ONE)]:[r(1)]")
						})
		{
			await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&{attribute} {thing}={value}"));
		}

		return (thing, await Eval("num(me)"));
	}

	private async Task Check(string code, string expected)
	{
		var (thing, caller) = await CreateThingAsync();

		var result = await Eval(code.Replace("#T", thing));

		await Assert.That(result).IsEqualTo(expected.Replace("#T", thing).Replace("#C", caller));
	}

	[Test]
	[Arguments("u(#T/X,a)", "#T|#T|#C|a")]
	[Arguments("u2(#T/X,a)", "#C|#C|#C|a")]
	[Arguments("u2(#T/V)", "#C|#C|#C|v")]
	[Arguments("u(#T/V)", "#T|#T|#T|v")]
	[Arguments("[u2(#T/NOPE)]|", "|")]
	public async Task U2_RunsAsTheCaller(string code, string expected) => await Check(code, expected);

	[Test]
	[Arguments("u2default(#T/X,def,a)", "#C|#C|#C|a")]
	[Arguments("u2default(#T/NOPE,def%0,a)", "def")]
	[Arguments("u2default(#T/Q,def,a,b)", "a-b")]
	[Arguments("u2default(#T/Q,def)", "-")]
	public async Task U2Default(string code, string expected) => await Check(code, expected);

	[Test]
	[Arguments("[setq(0,outer)][u2local(#T/Z)]%q0", "inouter")]
	[Arguments("[setq(0,outer)][u2(#T/Z)]%q0", "ininner")]
	[Arguments("u2local(#T/X,a)", "#C|#C|#C|a")]
	[Arguments("[setq(0,outer)][u2ldefault(#T/Z,dflt)]%q0", "inouter")]
	[Arguments("u2ldefault(#T/X,def,a)", "#C|#C|#C|a")]
	[Arguments("u2ldefault(#T/NOPE,def)", "def")]
	public async Task U2Local(string code, string expected) => await Check(code, expected);

	[Test]
	[Arguments("[setq(0,external)][r(0)][localize(u(#T/VA))][r(0)]", "external(before R0: external) --My R0:internal--external")]
	[Arguments("[privatize(1)][setq(0,external)][r(0)][localize(u(#T/VA))][r(0)]", "external(before R0: ) --My R0:internal--external")]
	[Arguments("[privatize(1)][setq(0,external)][r(0)][ulocal(#T/VA)][r(0)]", "external(before R0: ) --My R0:internal--external")]
	[Arguments("[privatize(1)][privatize(0)][setq(0,external)][r(0)][localize(u(#T/VA))][r(0)]", "external(before R0: external) --My R0:internal--external")]
	public async Task Privatize_Switch(string code, string expected) => await Check(code, expected);

	[Test]
	[Arguments("[setr(0,TWO)]:[setr(1,CHEESE)], [privatize(1,u(#T/VB))], %q0:%q1", "TWO:CHEESE, ONE:, TWO:CHEESE")]
	[Arguments("[setr(0,TWO)]:[setr(1,CHEESE)], [privatize(1,u(#T/VB),0)], %q0:%q1", "TWO:CHEESE, ONE:CHEESE, TWO:CHEESE")]
	[Arguments("[setr(0,TWO)]:[setr(1,CHEESE)], [privatize(1,u(#T/VB),1)], %q0:%q1", "TWO:CHEESE, ONE:, ONE:CHEESE")]
	[Arguments("[setr(0,TWO)]:[setr(1,CHEESE)], [privatize(1,u(#T/VB),0,1)], %q0:%q1", "TWO:CHEESE, ONE:, ONE:CHEESE")]
	[Arguments("[setr(0,TWO)]:[setr(1,CHEESE)], [privatize(0,u(#T/VB))], %q0:%q1", "TWO:CHEESE, ONE:CHEESE, TWO:CHEESE")]
	[Arguments("[setr(0,T)]:[setr(1,C)], [privatize(1,u(#T/VB),0 3 4,,1)], %q0:%q1", "T:C, ONE:C, T:C")]
	[Arguments("[setr(0,T)]:[setr(1,C)], [privatize(1,u(#T/VB),0 1 3 4,,1)], %q0:%q1", "T:C, ONE:, T:C")]
	[Arguments("privatize(1,[setq(0,x)]in)", "in")]
	[Arguments("[privatize(abc,y)]|[privatize(1)]|", "y||")]
	[Arguments("privatize()", "#-1 FUNCTION (PRIVATIZE) EXPECTS 1 ARGUMENT [RECEIVED 0]")]
	[Arguments("privatize(,x)", "#-1 FUNCTION (PRIVATIZE) EXPECTS 1 ARGUMENT [RECEIVED 0]")]
	public async Task Privatize_Code(string code, string expected) => await Check(code, expected);

	/// <summary>The switch lasts only while privatize()'s code runs, then goes back to what it was.</summary>
	[Test]
	public async Task Privatize_CodeRestoresTheSwitch()
	{
		await Check("[privatize(1,x)][setq(0,external)][localize(u(#T/VA))]",
			"x(before R0: external) --My R0:internal--");
	}

	/// <summary>Named registers can be listed by name with the multi argument.</summary>
	[Test]
	public async Task Privatize_NamedRegisters()
	{
		await Check("[setq(pv_keep,a,pv_pass,b)][privatize(1,[setq(pv_keep,x,pv_pass,y)]%q<pv_keep>,pv_keep,,1)]:%q<pv_keep>:%q<pv_pass>",
			"x:a:y");
	}
}
