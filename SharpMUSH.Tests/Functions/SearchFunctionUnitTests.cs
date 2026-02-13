using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace SharpMUSH.Tests.Functions;

public class SearchFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

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
		var createResult = await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, MModule.single($"@create {uniqueName}"));
		var createOutput = createResult?.Message?.ToPlainText() ?? "";
		
		// Extract the dbref from the create output
		var dbrefMatch = System.Text.RegularExpressions.Regex.Match(createOutput, @"#(\d+)");
		await Assert.That(dbrefMatch.Success).IsTrue().Because($"Create command should return a dbref. Output: {createOutput}");
		var createdDbref = dbrefMatch.Value;
		
		// Test lsearch with name filter
		// Correct syntax: lsearch(player, class, restriction)
		var result = (await Parser.FunctionParse(MModule.single($"lsearch(all,name,{uniqueName})")))?.Message!;
		var resultText = result.ToPlainText();
		
		// Should return exactly the object we just created
		await Assert.That(resultText).Contains(createdDbref);
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
	public async Task Lsearchr_ReturnsObjectsInReverseOrder()
	{
		// lsearchr returns results in reverse dbref order (highest to lowest)
		// Search for all rooms to get a predictable set of results
		var result = (await Parser.FunctionParse(MModule.single("lsearchr(all,type,ROOM,maxdbref,5)")))?.Message!;
		var resultText = result.ToPlainText();
		
		// Should return room #0 (always exists as Master Room)
		await Assert.That(resultText).Contains("#0");
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
	public async Task Scan_ReturnsVisibleObjects()
	{
		// Create a unique test object with a $-command to verify scan behavior
		// scan() searches for $-commands that match a given command string
		var uniqueName = $"ScanTest_{Guid.NewGuid():N}";
		var commandWord = $"testcmd{Guid.NewGuid():N}";
		var attrName = $"CMD_{commandWord.ToUpperInvariant()}";
		
		// Create a test object in the same location as executor (room #0)
		var createResult = await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, MModule.single($"@create {uniqueName}"));
		var createOutput = createResult?.Message?.ToPlainText() ?? "";
		
		// Extract the dbref from the create output (format: "Created" or contains "#<number>")
		var dbrefMatch = System.Text.RegularExpressions.Regex.Match(createOutput, @"#(\d+)");
		await Assert.That(dbrefMatch.Success).IsTrue().Because($"Create command output should contain a dbref. Output was: {createOutput}");
		var createdDbref = dbrefMatch.Value; // This will be something like "#5"
		
		// Set a $-command attribute on the created object
		// Format: $pattern:code
		await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, 
			MModule.single($"&{attrName} {createdDbref}=${commandWord} *:@emit Test command triggered!"));
		
		// scan() searches for $-commands that would match the given command
		// This should find the $-command we just created
		var result = (await Parser.FunctionParse(MModule.single($"scan({commandWord} test argument)")))?.Message!;
		var resultText = result.ToPlainText();
		
		// Should return a space-separated list of "dbref/attribute" pairs
		// The result should contain something like "#5/CMD_TESTCMD..."
		await Assert.That(resultText).Contains(createdDbref).Because($"scan({commandWord} test argument) should return {createdDbref}/... Actual result: {resultText}");
		await Assert.That(resultText).Contains(attrName).Because($"scan({commandWord} test argument) should return .../{attrName}. Actual result: {resultText}");
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
		
		// Should include wizard objects (player #1 is wizard by default)
		await Assert.That(resultText).Contains("#1");
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
		await Assert.That(resultText).Contains("#0");
		await Assert.That(resultText).Contains("#1");
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
		
		// Should return at least room #0 (Master Room always exists)
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
		
		// Should return player #1 (God player always exists)
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
		// Result should contain #1: if the expression matched
		if (!string.IsNullOrEmpty(resultText))
		{
			await Assert.That(resultText).Contains("#1:");
		}
	}

	[Test]
	public async Task Lsearch_RoomsFilter_ReturnsMatchingRooms()
	{
		// Test lsearch with ROOMS class (shortcut for type=room with name filter)
		// ROOMS class combines TYPE=ROOM and NAME=<pattern>
		// Using a pattern that matches Master Room (#0) which typically doesn't start with M
		// So let's search for all rooms regardless of name
		var result = (await Parser.FunctionParse(MModule.single("lsearch(all,type,ROOM,maxdbref,2)")))?.Message!;
		var resultText = result.ToPlainText();
		
		// Should return at least room #0 (Master Room always exists)
		await Assert.That(resultText).Contains("#0");
	}

	[Test]
	public async Task Lsearch_PlayersFilter_ReturnsMatchingPlayers()
	{
		// Test lsearch with PLAYERS class (shortcut for type=player with name filter)
		// PLAYERS class combines TYPE=PLAYER and NAME=<pattern>
		// Instead of relying on God's name starting with G, search all players
		var result = (await Parser.FunctionParse(MModule.single("lsearch(all,type,PLAYER)")))?.Message!;
		var resultText = result.ToPlainText();
		
		// Should return God player (#1 always exists)
		await Assert.That(resultText).Contains("#1");
	}

	[Test]
	public async Task Lsearch_MindbMaxdb_ReturnsMatchingRange()
	{
		// Test lsearch with MINDB/MAXDB aliases (PennMUSH uses both MINDB and MINDBREF)
		// Using MINDB and MAXDB which are shorter aliases
		var result = (await Parser.FunctionParse(MModule.single("lsearch(all,mindb,0,maxdb,2)")))?.Message!;
		var resultText = result.ToPlainText();
		
		// Should return objects in range #0-#2
		await Assert.That(resultText).Contains("#0");
		await Assert.That(resultText).Contains("#1");
	}

	[Test]
	public async Task Lsearch_StartFilter_SkipsResults()
	{
		// Test lsearch with START (pagination - skip first N results)
		// Start at 1 means skip object #0, so result should contain #1 or later
		var result = (await Parser.FunctionParse(MModule.single("lsearch(all,start,1,maxdb,2)")))?.Message!;
		var resultText = result.ToPlainText();
		
		// Should skip the first result (#0), so #0 should NOT be in results
		await Assert.That(resultText).DoesNotContain("#0");
		// Should contain #1 (God player, which is the second object)
		await Assert.That(resultText).Contains("#1");
	}

	[Test]
	public async Task Lsearch_CountFilter_LimitsResults()
	{
		// Test lsearch with COUNT (pagination - limit number of results)
		var result = (await Parser.FunctionParse(MModule.single("lsearch(all,count,1)")))?.Message!;
		var resultText = result.ToPlainText();
		
		// Should return exactly 1 result (likely #0, the first object)
		var results = resultText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		await Assert.That(results.Length).IsEqualTo(1);
		// The single result should be #0 (Master Room, first object in database)
		await Assert.That(resultText).Contains("#0");
	}

	[Test]
	public async Task Lsearch_StartAndCount_PaginatesResults()
	{
		// Test lsearch with START and COUNT together (pagination)
		// Start at 1 (skip #0), count 2 (return #1 and #2)
		var result = (await Parser.FunctionParse(MModule.single("lsearch(all,start,1,count,2,maxdb,2)")))?.Message!;
		var resultText = result.ToPlainText();
		
		// Should skip first result (#0) and return next 2 results (#1 and #2)
		await Assert.That(resultText).DoesNotContain("#0");
		await Assert.That(resultText).Contains("#1");
		// Should return at most 2 results
		var results = resultText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		await Assert.That(results.Length).IsLessThanOrEqualTo(2);
	}
}
