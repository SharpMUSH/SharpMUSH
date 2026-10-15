using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// RhostMUSH's q-register labels: <c>setq(&lt;register&gt;, &lt;value&gt;, &lt;label&gt;)</c>, setqm()'s
/// <c>&lt;register&gt;&gt;&lt;label&gt;</c> names and <c>nameq()</c>. A label is another name for its
/// register: both read and write the same value. Each evaluation gets a fresh register frame.
/// </summary>
public class RegisterLabelTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	private async Task<string> Eval(string code)
		=> (await Parser.EvaluateAsync(MarkupText.Plain(code))).ToPlainText();

	[Test]
	[Arguments("[setq(0,val,lbl)]%q<lbl>/%q0", "val/val")]
	[Arguments("setr(0,val,lbl)/%q<lbl>", "val/val")]
	[Arguments("[setq(0,v,lbl)][setq(lbl,new)]%q0", "new")]
	[Arguments("[setq(0,v,lbl)][setq(0,x)]%q<lbl>/[nameq(0)]", "x/lbl")]
	[Arguments("[setq(0,v,lbl)][r(lbl)]", "v")]
	[Arguments("[setq(0,v,lbl)][setq(1,w,two)][listq()]", "0 1")]
	[Arguments("[setq(0,v,LbL)][nameq(0)]%q<lbl>", "LbLv")]
	[Arguments("[setq(0,,lbl)]x%q<lbl>x[nameq(0)]", "xxlbl")]
	[Arguments("[setq(0,v,lbl)]%q<!!$lbl>", "1")]
	public async Task LabelIsAnotherNameForTheRegister(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);

	[Test]
	[Arguments("setq(0,v,l)")]
	[Arguments("setq(0,v,12)")]
	[Arguments("setq(0,v,a>b)")]
	[Arguments("setr(0,v,)")]
	public async Task LabelMustBeTwoCharactersAndNotANumber(string code)
		=> await Assert.That(await Eval(code)).IsEqualTo("#-1 INVALID LABEL");

	[Test]
	[Arguments("setq(a,1,b,2,c)", "SETQ")]
	[Arguments("setr(a,1,b,2,c)", "SETR")]
	public async Task OddCountsOtherThanThreeStayErrors(string code, string name)
		=> await Assert.That(await Eval(code)).IsEqualTo($"#-1 FUNCTION ({name}) EXPECTS AN EVEN NUMBER OF ARGUMENTS");

	/// <summary>A register has one label: a new one replaces it, and the old name reads its own register.</summary>
	[Test]
	public async Task RelabellingDropsTheOldLabel()
		=> await Assert.That(await Eval("[setq(0,a,ab1)][setq(0,b,cd2)]%q<ab1>/%q<cd2>/[nameq(0)]")).IsEqualTo("/b/cd2");

	/// <summary>A label names one register: given to another, it moves there.</summary>
	[Test]
	public async Task LabelMovesToTheRegisterLastGivenIt()
		=> await Assert.That(await Eval("[setq(0,v,lbl)][setq(1,w,lbl)]%q<lbl>/[nameq(0)]/[nameq(1)]")).IsEqualTo("w//lbl");

	/// <summary>localize() saves and restores labels with the values, as RhostMUSH does.</summary>
	[Test]
	public async Task LocalizeRestoresLabels()
		=> await Assert.That(await Eval("[setq(0,v,lb3)][localize([setq(1,x,lb3)][setq(0,zz,lb4)])]%q<lb3>/%q<lb4>/[nameq(0)]"))
			.IsEqualTo("v//lb3");

	[Test]
	[Arguments("[setq(+,v,nl)][nameq(nl,,1)]/%q<nl>", "0/v")]
	[Arguments("[setq(0,x)][setq(+,v,nl)][nameq(nl,,1)]", "1")]
	[Arguments("[setq(0,x)][setq(!,v,nl)][nameq(nl,,1)]", "0")]
	[Arguments("[setq(5,x,nl)][setq(+,v,nl)]%q5", "v")]
	public async Task PlusAndBangPickARegisterForTheLabel(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);

	[Test]
	[Arguments("[setq(0,v,lbl)][setqm(1>lb9,w)]%q<lb9>/%q1/[nameq(1)]", "w/w/lb9")]
	[Arguments("setrm(2>lb8,u,3,%q<lb8>)", "u u")]
	[Arguments("setqm(1>l,w)", "#-1 INVALID LABEL")]
	public async Task SetQMTakesRegisterAndLabelTogether(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);

	[Test]
	[Arguments("[nameq(0,foo)][nameq(0)]/%q<foo>[setq(foo,bar)]%q0", "foo/bar")]
	[Arguments("[setq(0,v,lbl)][nameq(lbl)]/[nameq(lbl,,1)]/[nameq(0,,1)]", "lbl/0/lbl")]
	[Arguments("[setq(0,v,lbl)][nameq(1,lbl)]", "#-1 LABEL [lbl] ALREADY IN USE")]
	[Arguments("[setq(0,v,lbl)][nameq(1,x)][nameq(1)]", "")]
	[Arguments("[setq(0,v,lbl)][nameq(0,)]x%q<lbl>x[nameq(0)]", "xx")]
	[Arguments("[setq(0,v,lbl)][setq(1,w,two)][setq(foo,bar)][nameq(*,,l)]|[nameq(*,,v)]|[nameq(l*,,u)]|[nameq(b*,,v)]|[nameq(1,,n)]",
		"0 1|0 1 FOO|0|FOO|1")]
	[Arguments("[setq(0,v,lbl)][setq(foo,bar)][nameq(*,,b)]|[nameq(0,,f)]|[nameq(v,,a)]", "0|0|0")]
	public async Task NameQReadsSetsAndListsLabels(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);

	/// <summary>The examples in <c>help setq register labels</c>, <c>help setqm()</c> and <c>help nameq()</c>.</summary>
	[Test]
	[Arguments("[setq(0, 15, damage)]%q<damage> %q0", "15 15")]
	[Arguments("[setq(0, 15, damage)][setq(damage, 20)]%q0", "20")]
	[Arguments("[setq(+, north, exit)][nameq(exit, , 1)]: %q<exit>", "0: north")]
	[Arguments("[setqm(0>total, 5, total, add(%q<total>, 1))]%q0", "6")]
	[Arguments("[setq(0, 42, total)][nameq(0)] [nameq(total)] [nameq(total, , 1)]", "total total 0")]
	[Arguments("[nameq(1, count)][setq(count, 7)]%q1", "7")]
	[Arguments("[setq(0, a, total)][nameq(1, total)]", "#-1 LABEL [total] ALREADY IN USE")]
	[Arguments("[setq(0, 42, total)][setq(1, 3, count)][setq(tax, 7)][nameq(*, , l)]", "0 1")]
	[Arguments("[setq(0, 42, total)][setq(tax, 7)][nameq(t*, , u)] / [nameq(4*, , v)]", "0 / 0")]
	public async Task HelpExamples(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);
}
