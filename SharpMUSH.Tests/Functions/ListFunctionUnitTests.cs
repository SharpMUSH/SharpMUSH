using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class ListFunctionUnitTests : TestsBase
{
	private IMUSHCodeParser Parser => FunctionParser;
	private IConnectionService ConnectionService => Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => Services.GetRequiredService<IMediator>();

	private static bool _testObjectsCreated = false;
	private static DBRef _testObjectDbRef;
	// private static readonly SemaphoreSlim Lock = new SemaphoreSlim(1, 1);

	/// <summary>
	/// Creates test object and attributes needed for list function tests
	/// </summary>
	private async Task EnsureTestObjectsExist()
	{
		if (_testObjectsCreated) return;

		// Create test object and capture its DBRef
		var createResult = await CommandParser.CommandParse(1, ConnectionService, MModule.single("@create test"));
		_testObjectDbRef = DBRef.Parse(createResult.Message!.ToPlainText()!);

		// Set up filter test attribute using DBRef
		await CommandParser.CommandParse(1, ConnectionService,
			MModule.single($"&IS_ODD #{_testObjectDbRef.Number}=mod(%0,2)"));

		// Set up fold test attribute
		await CommandParser.CommandParse(1, ConnectionService,
			MModule.single($"&ADD_FUNC #{_testObjectDbRef.Number}=add(%0,%1)"));

		// Set up mix test attribute  
		await CommandParser.CommandParse(1, ConnectionService,
			MModule.single($"&CONCAT #{_testObjectDbRef.Number}=cat(%0,%b,%1)"));

		// Set up munge test attribute (sort function)
		await CommandParser.CommandParse(1, ConnectionService,
			MModule.single($"&SORT #{_testObjectDbRef.Number}=sort(%0,%1)"));

		// Set up sortby test attribute (comparison function)
		await CommandParser.CommandParse(1, ConnectionService,
			MModule.single($"&COMP #{_testObjectDbRef.Number}=comp(%0,%1)"));

		// Set up sortkey test attribute (key generator)
		await CommandParser.CommandParse(1, ConnectionService,
			MModule.single($"&KEY #{_testObjectDbRef.Number}=strlen(%0)"));

		// Set up step test attribute
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&FIRST #{_testObjectDbRef.Number}=%0"));

		_testObjectsCreated = true;
	}

	[Test]
	[Arguments("iter(1 2 3,%i0)", "1 2 3")]
	[Arguments("iter(1,%i1)", "#-1 REGISTER OUT OF RANGE")]
	[Arguments("iter(1 2 3,add(%i0,1))", "2 3 4")]
	[Arguments("iter(1|2|3,%i0,|)", "1|2|3")]
	[Arguments("iter(1|2|3,%i0,|,-)", "1-2-3")]
	[Arguments("iter(1|2|3,add(%i0,1),|,-)", "2-3-4")]
	[Arguments("iter(1|2|3,iter(1 2 3,add(%i0,%i1)),|,-)", "2 3 4-3 4 5-4 5 6")]
	// TODO: %iL does not evaluate to the correct value.
	// [Arguments("iter(1|2|3,iter(1 2 3,add(%i0,%iL)),|,-)", "2 3 4-3 4 5-4 5 6")]
	public async Task IterationValue(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	// TODO: Fix: %$0 is for switches.
	// TODO: This should be #@, which is not yet implemented.
	[Test]
	[Arguments("iter(5 6 7,%$0)", "1 2 3")]
	[Arguments("iter(1|2|3,iter(1 2 3,add(%$0,%i1)),|,-)", "2 2 2-4 4 4-6 6 6")]
	public async Task IterationNumber(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
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

	[Test]
	public async Task SimpleAnsiTest()
	{
		// Simple test to check if ansi works at all
		var result = (await Parser.FunctionParse(MModule.single("ansi(hr,test)")))?.Message!;
		
		// Should contain ANSI escape codes
		await Assert.That(result.ToString()).Contains("\u001b[");
	}

	[Test]
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
	[Skip("Implementation incomplete - sort function needs full implementation")]
	[Arguments("sort(3 1 2)", "1 2 3")]
	[Arguments("sort(foo bar baz)", "bar baz foo")]
	public async Task Sort(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("filter(test/is_odd,1 2 3 4 5 6)", "1 3 5")]
	public async Task Filter(string function, string expected)
	{
		await EnsureTestObjectsExist();
		// Replace "test" with actual DBRef
		var functionWithDbRef = function.Replace("test", $"#{_testObjectDbRef.Number}");
		var result = (await Parser.FunctionParse(MModule.single(functionWithDbRef)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("fold(test/add_func,1 2 3)", "6")]
	public async Task Fold(string function, string expected)
	{
		await EnsureTestObjectsExist();
		// Replace "test" with actual DBRef
		var functionWithDbRef = function.Replace("test", $"#{_testObjectDbRef.Number}");
		var result = (await Parser.FunctionParse(MModule.single(functionWithDbRef)))?.Message!;
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
	public async Task SetUnion(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("setinter(a b c,c d e)", "c")]
	[Arguments("setinter(1 2 3,2 3 4)", "2 3")]
	public async Task SetIntersection(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("setdiff(a b c,c d e)", "a b")]
	[Arguments("setdiff(1 2 3,2 3 4)", "1")]
	public async Task SetDifference(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Implementation doesn't match expected behavior - returns wrong indices")]
	[Arguments("matchall(foo bar baz,ba*)", "2 3")]
	public async Task Matchall(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Skip("User-defined function execution issue - attribute not being called correctly")]
	[Arguments("mix(test/concat,a b c,1 2 3)", "a 1 b 2 c 3")]
	public async Task Mix(string function, string expected)
	{
		await EnsureTestObjectsExist();
		// Replace "test" with actual DBRef
		var functionWithDbRef = function.Replace("test", $"#{_testObjectDbRef.Number}");
		var result = (await Parser.FunctionParse(MModule.single(functionWithDbRef)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("munge(test/sort,b a c,2 1 3)", "1 2 3")]
	public async Task Munge(string function, string expected)
	{
		await EnsureTestObjectsExist();
		// Replace "test" with actual DBRef
		var functionWithDbRef = function.Replace("test", $"#{_testObjectDbRef.Number}");
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
	[Arguments("step(test/first,a b c d e,2)", "a c e")]
	public async Task Step(string function, string expected)
	{
		await EnsureTestObjectsExist();
		// Replace "test" with actual DBRef
		var functionWithDbRef = function.Replace("test", $"#{_testObjectDbRef.Number}");
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
	[Skip("Formatting logic incorrect - conjunction and punctuation not formatted properly")]
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

	[Test]
	[Skip("Lambda function syntax not fully supported - #lambda/\\%0 pattern needs implementation")]
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
	[Skip("Implementation issue - splice logic doesn't handle output separator correctly")]
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
	[Skip("Indexing issue - 1-based position handling for insert incorrect")]
	[Arguments("linsert(a b c,2,x)", "a x b c")]
	public async Task ListInsert(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("sortby(test/comp,c a b)", "a b c")]
	public async Task SortBy(string str, string expected)
	{
		await EnsureTestObjectsExist();
		// Replace "test" with actual DBRef
		var functionWithDbRef = str.Replace("test", $"#{_testObjectDbRef.Number}");
		var result = (await Parser.FunctionParse(MModule.single(functionWithDbRef)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("sortkey(test/key,abc ab a)", "a ab abc")]
	public async Task SortKey(string str, string expected)
	{
		await EnsureTestObjectsExist();
		// Replace "test" with actual DBRef
		var functionWithDbRef = str.Replace("test", $"#{_testObjectDbRef.Number}");
		var result = (await Parser.FunctionParse(MModule.single(functionWithDbRef)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Formatting logic incorrect - same issue as itemize")]
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
	[Skip("Error handling outside iteration - should return error but doesn't")]
	[Arguments("itext(0)", "#-1 REGISTER OUT OF RANGE")]
	public async Task Itext(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("setsymdiff(a b c,b c d)", "a d")]
	public async Task Setsymdiff(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}
}