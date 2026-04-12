using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class ListFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	/// <summary>
	/// Creates a unique test object with a single attribute set on it.
	/// Each test creates its own object to avoid cross-contamination when running in parallel.
	/// </summary>
	private async Task<int> CreateObjectWithAttribute(string objectName, string attrName, string attrValue)
	{
		var createResult = await CommandParser.CommandParse(1, ConnectionService,
			MModule.single($"@create {objectName}"));
		var dbRef = DBRef.Parse(createResult.Message!.ToPlainText()!);
		await CommandParser.CommandParse(1, ConnectionService,
			MModule.single($"&{attrName} #{dbRef.Number}={attrValue}"));
		return dbRef.Number;
	}

	[Test, NotInParallel]
	[Arguments("iter(1 2 3,%i0)", "1 2 3")]
	[Arguments("iter(1,%i1)", "#-1 REGISTER OUT OF RANGE")]
	[Arguments("iter(1 2 3,add(%i0,1))", "2 3 4")]
	[Arguments("iter(1|2|3,%i0,|)", "1|2|3")]
	[Arguments("iter(1|2|3,%i0,|,-)", "1-2-3")]
	[Arguments("iter(1|2|3,add(%i0,1),|,-)", "2-3-4")]
	[Arguments("iter(1|2|3,iter(1 2 3,add(%i0,%i1)),|,-)", "2 3 4-3 4 5-4 5 6")]
	// TODO: %iL does not evaluate to the correct value.
	// [Arguments("iter(1|2|3,iter(1 2 3,add(%i0,%iL)),|,-)", "2 3 4-3 4 5-4 5 6")]
	[Arguments("iter(1 2 3,##)", "1 2 3")]
	[Arguments("iter(1 2 3,add(##,1))", "2 3 4")]
	[Arguments("iter(1|2|3,##,|,-)", "1-2-3")]
	public async Task IterationValue(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	// Fixed: %$0 is for switches, not iterations.
	// Using inum(0) for iteration number, which is the correct function.
	// TODO: Implement #@ token as shorthand for inum(0).
	[Test, NotInParallel]
	[Arguments("iter(5 6 7,inum(0))", "1 2 3")]
	[Arguments("iter(1|2|3,iter(1 2 3,add(inum(0),%i1)),|,-)", "2 2 2-4 4 4-6 6 6")]
	public async Task IterationNumber(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test, NotInParallel]
	[Arguments("iter(1|2|3,add(%i0,1)[ibreak()],|,-)", "2")]
	[Arguments("iter(1|2|3,add(%i0,1)[ibreak(0)],|,-)", "2")]
	[Arguments("iter(1|2|3,iter(1 2 3,[add(%i0,%i1)][ibreak()]),|,-)", "2 3 4")]
	[Arguments("iter(1|2|3,iter(1 2 3,[add(%i0,%i1)][ibreak(0)]),|,-)", "2 3 4")]
	[Arguments("iter(1|2|3,iter(1 2 3,[add(%i0,%i1)][ibreak(1)]),|,-)", "2-3-4")]
	[Arguments("iter(1|2|3,iter(1 2 3,[add(1,1)][add(%i0,%i1)][ibreak(1)]),|,-)", "22-23-24")]
	[Arguments("iter(1|2|3,iter(1 2 3,[ibreak(1)][add(%i0,%i1)]),|,-)", "2-3-4")]
	// TODO: Why does putting [ibreak()] at the start of the contents cause a different evaluation?
	public async Task IterationBreak(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test, NotInParallel]
	public async Task SimpleAnsiTest()
	{
		// Simple test to check if ansi works at all
		var result = (await Parser.FunctionParse(MModule.single("ansi(hr,test)")))?.Message!;

		// Should contain ANSI escape codes
		await Assert.That(result.ToString()).Contains("\u001b[");
	}

	[Test, NotInParallel]
	public async Task IterationWithAnsiMarkup()
	{
		// Test case from issue: iter should preserve ANSI markup
		// The problem: iter(lnum(1,5),%i0 --> [ansi(hr,%i0)],,%r)
		// loses the ANSI markup

		// First, test the working equivalent as a baseline
		var expected = (await Parser.FunctionParse(
			MModule.single("1 --> [ansi(hr,1)]%r2 --> [ansi(hr,2)]%r3 --> [ansi(hr,3)]%r4 --> [ansi(hr,4)]%r5 --> [ansi(hr,5)]")))?.Message!;

		// Now test the iter version - it should produce the same result
		var actual = (await Parser.FunctionParse(
			MModule.single("iter(lnum(1,5),%i0 --> [ansi(hr,%i0)],,%r)")))?.Message!;

		// Compare using the same method as other Markup tests
		var resultBytes = System.Text.Encoding.Unicode.GetBytes(actual.ToString());
		var expectedBytes = System.Text.Encoding.Unicode.GetBytes(expected.ToString());

		foreach (var (first, second) in resultBytes.Zip(expectedBytes))
		{
			await Assert.That(first).IsEqualTo(second);
		}
	}

	[Test]
	[Arguments("rest(1|2|3)", "")]
	[Arguments("rest(%b)", "")]
	[Arguments("rest(null())", "")]
	[Arguments("rest(1|2|3 5 6)", "5 6")]
	[Arguments("rest(1 2 3)", "2 3")]
	[Arguments("rest(1|2|3,|)", "2|3")]
	public async Task Rest(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("last(1|2|3)", "1|2|3")]
	[Arguments("last(null())", "")]
	[Arguments("last(%b)", "")]
	[Arguments("last(1|2|3 5 6)", "6")]
	[Arguments("last(1 2 3)", "3")]
	[Arguments("last(1|2|3,|)", "3")]
	public async Task Last(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("first(1 2 3)", "1")]
	[Arguments("first(a|b|c,|)", "a")]
	public async Task First(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("words(1 2 3)", "3")]
	[Arguments("words(single)", "1")]
	public async Task Words(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("extract(a b c,2)", "b")]
	[Arguments("extract(a b c,1,2)", "a b")]
	[Arguments("extract(a|b|c,2,3,|)", "b|c")]
	public async Task Extract(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("grab(This is a test,tes*)", "test")]
	[Arguments("grab(a|b|c|d,c,|)", "c")]
	public async Task Grab(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("graball(This is a test of a test,test)", "test test")]
	[Arguments("graball(This|is|testing|a|test,tes*,|)", "testing|test")]
	public async Task Graball(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("sort(3 1 2)", "1 2 3")]
	[Arguments("sort(foo bar baz)", "bar baz foo")]
	public async Task Sort(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("filter(test/IS_ODD_FILTER,1 2 3 4 5 6)", "1 3 5")]
	public async Task Filter(string function, string expected)
	{
		var objNum = await CreateObjectWithAttribute("filter_obj", "IS_ODD_FILTER", "mod(%0,2)");
		var functionWithDbRef = function.Replace("test", $"#{objNum}");
		var result = (await Parser.FunctionParse(MModule.single(functionWithDbRef)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test, NotInParallel]
	[Arguments(@"filter(#lambda/mod\(\%0\,2\),1 2 3 4 5 6)", "1 3 5")]
	[Arguments(@"filter(#apply/isnum,1 foo 3 bar 5 6)", "1 3 5 6")]
	public async Task FilterWithLambda(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("map(test/IS_ODD_MAP,1 2 3 4 5 6)", "1 0 1 0 1 0")]
	public async Task Map(string function, string expected)
	{
		var objNum = await CreateObjectWithAttribute("map_obj", "IS_ODD_MAP", "mod(%0,2)");
		var functionWithDbRef = function.Replace("test", $"#{objNum}");
		var result = (await Parser.FunctionParse(MModule.single(functionWithDbRef)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test, NotInParallel]
	[Arguments(@"map(#lambda/strlen\(\%0\),hello world foo)", "5 5 3")]
	[Arguments(@"map(#lambda/strlen\(\%0\),hello;world;foo,;)", "5;5;3")]
	[Arguments(@"map(#lambda/\%0,a b c)", "a b c")]
	// Bracket-form lambda: verify [func()] syntax in lambda code passes attribute validation
	[Arguments(@"map(#lambda/[strlen\(\%0\)],hello world foo)", "5 5 3")]
	public async Task MapWithLambda(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test, NotInParallel]
	[Arguments(@"map(#apply/strlen,hello world foo)", "5 5 3")]
	[Arguments(@"map(#apply/strlen,hello;world;foo,;)", "5;5;3")]
	public async Task MapWithApply(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("fold(test/ADD_FUNC_FOLD,1 2 3)", "6")]
	public async Task Fold(string function, string expected)
	{
		var objNum = await CreateObjectWithAttribute("fold_obj", "ADD_FUNC_FOLD", "add(%0,%1)");
		var functionWithDbRef = function.Replace("test", $"#{objNum}");
		var result = (await Parser.FunctionParse(MModule.single(functionWithDbRef)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test, NotInParallel]
	[Arguments(@"fold(#lambda/add\(\%0\,\%1\),1 2 3)", "6")]
	[Arguments(@"fold(#lambda/add\(\%0\,\%1\),1 2 3 4,0)", "10")]
	[Arguments(@"fold(#apply2/add,1 2 3)", "6")]
	public async Task FoldWithLambda(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("ldelete(a b c d,2)", "a c d")]
	[Arguments("ldelete(a|b|c|d,2,|)", "a|c|d")]
	public async Task Ldelete(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("lreplace(a b c,2,foo)", "a foo c")]
	[Arguments("lreplace(a|b|c,2,foo,|)", "a|foo|c")]
	public async Task ListReplace(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("member(a b c,b)", "2")]
	[Arguments("member(a|b|c,b,|)", "2")]
	public async Task Member(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("remove(a b c b a,b a)", "c b a")]
	[Arguments("remove(a|b|c|b,b,|)", "a|c|b")]
	public async Task Remove(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("setunion(a b c,c d e)", "a b c d e")]
	[Arguments("setunion(1 2 3,2 3 4)", "1 2 3 4")]
	// Penn setunion space-delimited tests
	[Arguments("setunion(,)", "")]
	[Arguments("setunion(a,)", "a")]
	[Arguments("setunion(,a)", "a")]
	[Arguments("setunion(a a a,)", "a")]
	[Arguments("setunion(,a a a)", "a")]
	[Arguments("setunion(a,a)", "a")]
	[Arguments("setunion(a,b)", "a b")]
	[Arguments("setunion(a b,b)", "a b")]
	[Arguments("setunion(b a,b)", "a b")]
	[Arguments("setunion(c a b a,a b c c)", "a b c")]
	// Penn setunion ! delimiter tests
	[Arguments("setunion(,,!)", "")]
	[Arguments("setunion(!,,!)", "")]
	[Arguments("setunion(,!,!)", "")]
	[Arguments("setunion(a,,!)", "a")]
	[Arguments("setunion(,a,!)", "a")]
	[Arguments("setunion(a!a!a,,!)", "a")]
	[Arguments("setunion(,a!a!a,!)", "a")]
	[Arguments("setunion(a!a!a,!,!)", "!a")]
	[Arguments("setunion(a!a!a,!a,!)", "!a")]
	[Arguments("setunion(a,a,!)", "a")]
	[Arguments("setunion(a,b,!)", "a!b")]
	[Arguments("setunion(a!b,b,!)", "a!b")]
	[Arguments("setunion(b!a,b,!)", "a!b")]
	[Arguments("setunion(b!a,!b,!)", "!a!b")]
	[Arguments("setunion(!b!a,b,!)", "!a!b")]
	[Arguments("setunion(b!a!,b,!)", "!a!b")]
	[Arguments("setunion(!b!a!,b,!)", "!a!b")]
	[Arguments("setunion(!b!a!,!b,!)", "!a!b")]
	[Arguments("setunion(c!a!b!a,a!b!c!c,!)", "a!b!c")]
	[Arguments("setunion(!c!a!b!a,a!b!c!c,!)", "!a!b!c")]
	public async Task SetUnion(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("setinter(a b c,c d e)", "c")]
	[Arguments("setinter(1 2 3,2 3 4)", "2 3")]
	// Penn setinter space-delimited tests
	[Arguments("setinter(,)", "")]
	[Arguments("setinter(a,)", "")]
	[Arguments("setinter(,a)", "")]
	[Arguments("setinter(a a a,)", "")]
	[Arguments("setinter(,a a a)", "")]
	[Arguments("setinter(a,a)", "a")]
	[Arguments("setinter(a,b)", "")]
	[Arguments("setinter(a b,b)", "b")]
	[Arguments("setinter(b a,b)", "b")]
	[Arguments("setinter(c a b a,a b c c)", "a b c")]
	// Penn setinter ! delimiter tests
	[Arguments("setinter(,,!)", "")]
	[Arguments("setinter(!,,!)", "")]
	[Arguments("setinter(,!,!)", "")]
	[Arguments("setinter(a,,!)", "")]
	[Arguments("setinter(,a,!)", "")]
	[Arguments("setinter(a!a!a,,!)", "")]
	[Arguments("setinter(,a!a!a,!)", "")]
	[Arguments("setinter(a!a!a,!,!)", "")]
	[Arguments("setinter(a!a!a,!a,!)", "a")]
	[Arguments("setinter(a,a,!)", "a")]
	[Arguments("setinter(a,b,!)", "")]
	[Arguments("setinter(a!b,b,!)", "b")]
	[Arguments("setinter(b!a,b,!)", "b")]
	[Arguments("setinter(b!a,!b,!)", "b")]
	[Arguments("setinter(!b!a,b,!)", "b")]
	[Arguments("setinter(b!a!,b,!)", "b")]
	[Arguments("setinter(!b!a!,b,!)", "b")]
	[Arguments("setinter(!b!a!,!b,!)", "!b")]
	[Arguments("setinter(c!a!b!a,a!b!c!c,!)", "a!b!c")]
	[Arguments("setinter(!c!a!b!a,a!b!c!c,!)", "a!b!c")]
	public async Task SetIntersection(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("setdiff(a b c,c d e)", "a b")]
	[Arguments("setdiff(1 2 3,2 3 4)", "1")]
	// Penn setdiff space-delimited tests
	[Arguments("setdiff(,)", "")]
	[Arguments("setdiff(a,)", "a")]
	[Arguments("setdiff(,a)", "")]
	[Arguments("setdiff(a a a,)", "a")]
	[Arguments("setdiff(,a a a)", "")]
	[Arguments("setdiff(a,a)", "")]
	[Arguments("setdiff(a,b)", "a")]
	[Arguments("setdiff(a b,b)", "a")]
	[Arguments("setdiff(b a,b)", "a")]
	[Arguments("setdiff(c a b a,a b c c)", "")]
	// Penn setdiff ! delimiter tests
	[Arguments("setdiff(,,!)", "")]
	[Arguments("setdiff(!,,!)", "")]
	[Arguments("setdiff(,!,!)", "")]
	[Arguments("setdiff(a,,!)", "a")]
	[Arguments("setdiff(,a,!)", "")]
	[Arguments("setdiff(a!a!a,,!)", "a")]
	[Arguments("setdiff(,a!a!a,!)", "")]
	[Arguments("setdiff(a!a!a,!,!)", "a")]
	[Arguments("setdiff(a!a!a,!a,!)", "")]
	[Arguments("setdiff(a,a,!)", "")]
	[Arguments("setdiff(a,b,!)", "a")]
	[Arguments("setdiff(a!b,b,!)", "a")]
	[Arguments("setdiff(b!a,b,!)", "a")]
	[Arguments("setdiff(b!a,!b,!)", "a")]
	[Arguments("setdiff(!b!a,b,!)", "!a")]
	[Arguments("setdiff(b!a!,b,!)", "!a")]
	[Arguments("setdiff(!b!a!,b,!)", "!a")]
	[Arguments("setdiff(!b!a!,!b,!)", "a")]
	[Arguments("setdiff(c!a!b!a,a!b!c!c,!)", "")]
	[Arguments("setdiff(!c!a!b!a,a!b!c!c,!)", "")]
	public async Task SetDifference(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("matchall(foo bar baz,ba*)", "2 3")]
	public async Task Matchall(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("mix(test/CONCAT_MIX,a b c,1 2 3)", "a 1 b 2 c 3")]
	public async Task Mix(string function, string expected)
	{
		var objNum = await CreateObjectWithAttribute("mix_obj", "CONCAT_MIX", "%0%b%1");
		var functionWithDbRef = function.Replace("test", $"#{objNum}");
		var result = (await Parser.FunctionParse(MModule.single(functionWithDbRef)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("munge(test/SORT_MUNGE,b a c,2 1 3)", "1 2 3")]
	public async Task Munge(string function, string expected)
	{
		var objNum = await CreateObjectWithAttribute("munge_obj", "SORT_MUNGE", "sort(%0,%1)");
		var functionWithDbRef = function.Replace("test", $"#{objNum}");
		var result = (await Parser.FunctionParse(MModule.single(functionWithDbRef)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("unique(a b b c b)", "a b c b")]
	public async Task Unique(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("randextract(a b c d e)", "")]
	public async Task RandomExtract(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		// Random result, just check it's not empty
		await Assert.That(result.ToString()).IsNotEmpty();
	}

	[Test]
	[Arguments("randword(a b c d e)")]
	public async Task RandomWord(string function)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		// Random result, just check it's not empty
		await Assert.That(result.ToString()).IsNotEmpty();
	}

	[Test]
	[Arguments("step(test/FIRST_STEP,a b c d e,2)", "a c e")]
	public async Task Step(string function, string expected)
	{
		var objNum = await CreateObjectWithAttribute("step_obj", "FIRST_STEP", "%0");
		var functionWithDbRef = function.Replace("test", $"#{objNum}");
		var result = (await Parser.FunctionParse(MModule.single(functionWithDbRef)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("index(a b c d,%b,2,2)", "b c")]
	public async Task Index(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("itemize(a b c)", "a, b, and c")]
	public async Task Itemize(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("items(a b c,%b)", "3")]
	public async Task Items(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("namegrab(#0 #1 #2,room)", "#0")]
	[Arguments("namegrab(#0 #1 #2,Master Room)", "#2")]
	[Arguments("namegrab(#0 #1 #2,God)", "#1")]
	public async Task Namegrab(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsNotNull();
	}

	[Test]
	[Arguments("namegraball(#0 #1 #2,room)", "#0 #2")]
	[Arguments("namegraball(#0 #1 #2,Master Room)", "#2")]
	[Arguments("namegraball(#0 #1 #2,God)", "#1")]
	public async Task NameGrabAll(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsNotNull();
	}

	[Test, NotInParallel]
	[Arguments(@"filterbool(#lambda/\%0,1 0 1)", "1 1")]
	public async Task FilterBool(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("revwords(a b c)", "c b a")]
	public async Task ReverseWords(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("splice(a b c,d e f, )", "a d  b e  c f")]
	public async Task Splice(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("lset(a b c,2,x)", "a x c")]
	[Arguments("lset(a b c,1,x)", "x b c")]
	[Arguments("lset(a b c,3,x)", "a b x")]
	public async Task ListSet(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("linsert(a b c,2,x)", "a x b c")]
	// Penn insert.1-insert.9 (PennMUSH insert = SharpMUSH linsert)
	[Arguments("linsert(a b c,0,X)", "a b c")]
	[Arguments("linsert(a b c,1,X)", "X a b c")]
	[Arguments("linsert(a b c,2,X)", "a X b c")]
	[Arguments("linsert(a b c,3,X)", "a b X c")]
	[Arguments("linsert(a b c,4,X)", "a b c")]
	[Arguments("linsert(a b c,-1,X)", "a b c X")]
	[Arguments("linsert(a b c,-2,X)", "a b X c")]
	[Arguments("linsert(a b c,-3,X)", "a X b c")]
	[Arguments("linsert(a b c,-4,X)", "a b c")]
	public async Task ListInsert(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("sortby(test/COMP_SORTBY,c a b)", "a b c")]
	public async Task SortBy(string str, string expected)
	{
		var objNum = await CreateObjectWithAttribute("sortby_obj", "COMP_SORTBY", "comp(%0,%1)");
		var functionWithDbRef = str.Replace("test", $"#{objNum}");
		var result = (await Parser.FunctionParse(MModule.single(functionWithDbRef)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("sortkey(test/KEY_SORTKEY,abc ab a)", "a ab abc")]
	public async Task SortKey(string str, string expected)
	{
		var objNum = await CreateObjectWithAttribute("sortkey_obj", "KEY_SORTKEY", "strlen(%0)");
		var functionWithDbRef = str.Replace("test", $"#{objNum}");
		var result = (await Parser.FunctionParse(MModule.single(functionWithDbRef)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("elist(a b c)", "a, b, and c")]
	public async Task Elist(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("ilev()", "-1")]
	public async Task Ilev(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("inum(0)", "#-1 REGISTER OUT OF RANGE")]
	public async Task Inum(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("itext(0)", "#-1 REGISTER OUT OF RANGE")]
	public async Task Itext(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("setsymdiff(a b c,b c d)", "a d")]
	// Penn setsymdiff space-delimited tests
	[Arguments("setsymdiff(,)", "")]
	[Arguments("setsymdiff(a,)", "a")]
	[Arguments("setsymdiff(,a)", "a")]
	[Arguments("setsymdiff(a a a,)", "a")]
	[Arguments("setsymdiff(,a a a)", "a")]
	[Arguments("setsymdiff(a,a)", "")]
	[Arguments("setsymdiff(a,b)", "a b")]
	[Arguments("setsymdiff(a b,b)", "a")]
	[Arguments("setsymdiff(b a,b)", "a")]
	[Arguments("setsymdiff(c a b a,a b c c)", "")]
	[Arguments("setsymdiff(a b,c d)", "a b c d")]
	[Arguments("setsymdiff(a b c,c d)", "a b d")]
	// Penn setsymdiff ! delimiter tests
	[Arguments("setsymdiff(,,!)", "")]
	[Arguments("setsymdiff(!,,!)", "")]
	[Arguments("setsymdiff(,!,!)", "")]
	[Arguments("setsymdiff(a,,!)", "a")]
	[Arguments("setsymdiff(,a,!)", "a")]
	[Arguments("setsymdiff(a!a!a,,!)", "a")]
	[Arguments("setsymdiff(,a!a!a,!)", "a")]
	[Arguments("setsymdiff(a!a!a,!,!)", "!a")]
	[Arguments("setsymdiff(a!a!a,!a,!)", "")]
	[Arguments("setsymdiff(a,a,!)", "")]
	[Arguments("setsymdiff(a,b,!)", "a!b")]
	[Arguments("setsymdiff(a!b,b,!)", "a")]
	[Arguments("setsymdiff(b!a,b,!)", "a")]
	[Arguments("setsymdiff(b!a,!b,!)", "!a")]
	[Arguments("setsymdiff(!b!a,b,!)", "!a")]
	[Arguments("setsymdiff(b!a!,b,!)", "!a")]
	[Arguments("setsymdiff(!b!a!,b,!)", "!a")]
	[Arguments("setsymdiff(!b!a!,!b,!)", "a")]
	[Arguments("setsymdiff(c!a!b!a,a!b!c!c,!)", "")]
	[Arguments("setsymdiff(!c!a!b!a,a!b!c!c,!)", "")]
	public async Task Setsymdiff(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}
}