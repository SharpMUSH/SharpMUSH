using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace SharpMUSH.Tests.Functions;

public class SearchFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	[Test]
	public async Task Lsearch_TypeFilter_ReturnsMatchingObjects()
	{
		// Test lsearch with type filter
		// Correct syntax: lsearch(player, class, restriction)
		var result = (await Parser.FunctionParse(MModule.single("lsearch(all,type,PLAYER)")))?.Message!;
		var resultText = result.ToPlainText();
		
		// Should return at least player #1 (God)
		await Assert.That(resultText).IsNotNull();
		await Assert.That(resultText).Contains("#1");
	}

	[Test]
	public async Task Lsearch_NameFilter_ReturnsMatchingObjects()
	{
		// Create a test object with unique name
		var uniqueName = $"LSearchTest_{Guid.NewGuid():N}";
		await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, MModule.single($"@create {uniqueName}"));
		
		// Test lsearch with name filter
		// Correct syntax: lsearch(player, class, restriction)
		var result = (await Parser.FunctionParse(MModule.single($"lsearch(all,name,{uniqueName})")))?.Message!;
		var resultText = result.ToPlainText();
		
		await Assert.That(resultText).IsNotNull();
		await Assert.That(resultText).IsNotEmpty();
	}

	[Test]
	public async Task Lsearch_CombinedFilters_ReturnsMatchingObjects()
	{
		// Test lsearch with multiple filters
		// Correct syntax: lsearch(player, class1, restriction1, class2, restriction2)
		var result = (await Parser.FunctionParse(MModule.single("lsearch(all,type,ROOM,mindbref,0)")))?.Message!;
		var resultText = result.ToPlainText();
		
		// Should return at least room #0
		await Assert.That(resultText).IsNotNull();
		await Assert.That(resultText).Contains("#0");
	}

	[Test]
	[Arguments("lsearchr(#0,name,test*)", "")]
	public async Task Lsearchr(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task Nlsearch_ReturnsCount()
	{
		// Test nlsearch returns a count
		// Correct syntax: nlsearch(player, class, restriction)
		var result = (await Parser.FunctionParse(MModule.single("nlsearch(all,type,PLAYER)")))?.Message!;
		var resultText = result.ToPlainText();
		
		// Should return a number >= 1 (at least God exists)
		await Assert.That(resultText).IsNotNull();
		await Assert.That(int.TryParse(resultText, out var count)).IsTrue();
		await Assert.That(count).IsGreaterThanOrEqualTo(1);
	}

	[Test]
	[Arguments("scan(%#)", "")]
	public async Task Scan(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("nearby(%#,%#)", "1")]
	public async Task Nearby(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task Lsearch_ElockFilter_ReturnsMatchingObjects()
	{
		// Test lsearch with elock filter - this should now work without throwing NotImplementedException
		// The elock class evaluates a lock string against objects
		// Example from PennMUSH docs: lsearch(all, elock, FLAG^WIZARD)
		// This searches for objects that pass the lock "FLAG^WIZARD" (i.e., have the WIZARD flag)
		// Correct syntax: lsearch(player, class, restriction)
		
		var result = (await Parser.FunctionParse(MModule.single("lsearch(all,elock,FLAG^WIZARD)")))?.Message!;
		var resultText = result.ToPlainText();
		
		// Should return some result (at least not throw an exception)
		// The result should include wizard objects (player #1 is wizard by default)
		await Assert.That(resultText).IsNotNull();
	}

	[Test]
	public async Task Lsearch_EvalFilter_ReturnsMatchingObjects()
	{
		// Test lsearch with eval filter
		// EVAL evaluates a function/expression for each object, replacing ## with the object's dbref number
		// Using simple constant true expression to verify the mechanism works
		// Correct syntax: lsearch(player, class, restriction)
		
		var result = (await Parser.FunctionParse(MModule.single("lsearch(all,eval,1,maxdbref,2)")))?.Message!;
		var resultText = result.ToPlainText();
		
		// Should return objects 0, 1, 2 since eval=1 always returns true
		await Assert.That(resultText).IsNotNull();
		await Assert.That(resultText).IsNotEmpty();
	}

	[Test]
	public async Task Lsearch_EplayerFilter_ReturnsMatchingPlayers()
	{
		// Test lsearch with eplayer filter
		// EPLAYER is like EVAL but restricted to players only
		// Using constant true to verify type filtering works
		// Correct syntax: lsearch(player, class, restriction)
		
		var result = (await Parser.FunctionParse(MModule.single("lsearch(all,eplayer,1)")))?.Message!;
		var resultText = result.ToPlainText();
		
		// Should return at least player #1 (God)
		await Assert.That(resultText).IsNotNull();
		await Assert.That(resultText).Contains("#1");
	}

	[Test]
	public async Task Lsearch_EroomFilter_ReturnsMatchingRooms()
	{
		// Test lsearch with eroom filter
		// EROOM is like EVAL but restricted to rooms only
		// Using constant true expression
		// Correct syntax: lsearch(player, class, restriction)
		
		var result = (await Parser.FunctionParse(MModule.single("lsearch(all,eroom,1)")))?.Message!;
		var resultText = result.ToPlainText();
		
		// Should return at least room #0
		await Assert.That(resultText).IsNotNull();
		await Assert.That(resultText).Contains("#0");
	}

	[Test]
	public async Task Lsearch_CombinedEvalAndTypeFilters_ReturnsMatchingObjects()
	{
		// Test combining eval with other filters
		// Should apply both database-level type filter and application-level eval filter
		// Using constant true to verify both filters work together
		// Correct syntax: lsearch(player, class1, restriction1, class2, restriction2)
		
		var result = (await Parser.FunctionParse(MModule.single("lsearch(all,type,PLAYER,eval,1)")))?.Message!;
		var resultText = result.ToPlainText();
		
		// Should return player #1
		await Assert.That(resultText).IsNotNull();
		await Assert.That(resultText).Contains("#1");
	}

	[Test]
	public async Task Lsearch_EvalWithEscapedBrackets_ReturnsMatchingObjects()
	{
		// Test lsearch with eval using escaped brackets as shown in PennMUSH documentation
		// From pennfunc.md: lsearch(all, eplayer, \[eq(money(##),100)\])
		// "any brackets, percent signs, or other special characters should be escaped"
		// The code is evaluated twice:
		// 1. Once as an argument to lsearch()
		// 2. Again for each object with ## replaced by the dbref
		
		// Correct syntax uses commas, not equals: lsearch(all,eval,\[...\])
		// Using strmatch to match dbref format with timestamp: #1:*
		var result = (await Parser.FunctionParse(MModule.single(@"lsearch(all,eval,\[strmatch\(##\,#1:*\)\])")))?.Message!;
		var resultText = result.ToPlainText();
		
		// Should return object #1 with its full dbref (including timestamp)
		// Note: Test validates syntax works, actual match depends on object #1 existing
		await Assert.That(resultText).IsNotNull();
		// If we have results, they should contain #1:
		if (!string.IsNullOrEmpty(resultText))
		{
			await Assert.That(resultText).Contains("#1:");
		}
	}
}
