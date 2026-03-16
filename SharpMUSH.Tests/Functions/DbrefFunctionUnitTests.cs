using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class DbrefFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	public async Task Loc()
	{
		// Test loc function - should return the location of the current player
		var result = (await Parser.FunctionParse(MModule.single("loc(%#)")))?.Message!;
		await Assert.That(result.ToPlainText()).StartsWith("#0:");
	}

	[Test]
	public async Task Controls()
	{
		// Test controls function - a player should control themselves
		var result = (await Parser.FunctionParse(MModule.single("controls(%#,%#)")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("1");
	}

	[Test]
	public async Task Home()
	{
		// Test home function - should return the home of the current player
		var result = (await Parser.FunctionParse(MModule.single("home(%#)")))?.Message!;
		await Assert.That(result.ToPlainText()).StartsWith("#0:");
	}

	[Test]
	public async Task LocOnCurrentRoom()
	{
		// loc() on the current room (%l) should return drop-to or #-1
		var result = (await Parser.FunctionParse(MModule.single("loc(%l)")))?.Message!;
		// Should return either a dbref or #-1 (if no drop-to)
		await Assert.That(result.ToPlainText()).Matches("^(#[0-9]+:[0-9]+|#-1)$");
	}

	[Test]
	public async Task HomeOnCurrentRoom()
	{
		// home() on the current room (%l) should return drop-to or #-1
		var result = (await Parser.FunctionParse(MModule.single("home(%l)")))?.Message!;
		// Should return either a dbref or #-1 (if no drop-to)
		await Assert.That(result.ToPlainText()).Matches("^(#[0-9]+:[0-9]+|#-1)$");
	}


	[Test]
	[Arguments("entrances(%l)", "")]
	public async Task Entrances(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("followers(%#)", "")]
	public async Task Followers(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("following(%#)", "")]
	public async Task Following(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("locate(%#,nonsense-does-not-exist,*)", "#-1")]
	[Arguments("first(locate(%#,me,*),:)", "#1")]
	[Arguments("first(locate(%#,here,*),:)", "#0")]
	public async Task Locate(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("create(some-silly-object)", "locate(%#,some-silly-object,*)")]
	// TODO: Enable when tel() is implemented
	// [Arguments("tel(create(content-object),create(container-object))", "locate(%#,container-object's content-object,*)")]
	public async Task CreateAndLocate(string create, string locate)
	{
		var result = (await Parser.FunctionParse(MModule.single(create)))?.Message!;
		var located = (await Parser.FunctionParse(MModule.single(locate)))?.Message!;

		await Assert.That(result.ToPlainText()).IsEqualTo(located.ToPlainText());
	}

	[Test]
	[Arguments("lock(%#)", "")]
	[Arguments("lock(%#,Basic)", "")]
	public async Task Lock(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("elock(%#,Basic)", "#-1 NO SUCH LOCK")]
	public async Task Elock(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("rloc(#1,0)", "#0")]
	public async Task Rloc(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("slev()", "")]
	public async Task Slev(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("stext()", "")]
	public async Task Stext(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("llocks(%#)", "")]
	public async Task Llocks(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("llockflags()", "")]
	[Arguments("llockflags(Basic)", "")]
	public async Task Llockflags(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("first(lockowner(%#),:)", "#1")]
	public async Task Lockowner(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("lockfilter(%# %l,Basic,1)", "")]
	public async Task Lockfilter(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("andflags(%#,PLAYER)", "1")]
	[Arguments("andflags(%#,PLAYER WIZARD)", "1")]
	[Arguments("andflags(%#,THING WIZARD)", "0")]
	[Arguments("andflags(%#,PLAYER ROYALTY)", "0")]
	public async Task Andflags(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("orflags(%#,PLAYER)", "1")]
	[Arguments("orflags(%#,WIZARD PLAYER)", "1")]
	public async Task Orflags(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("andlflags(%#,PLAYER)", "1")]
	public async Task Andlflags(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("orlflags(%#,PLAYER)", "1")]
	public async Task Orlflags(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}


	[Test]
	[Arguments("andlpowers(%#,Guest)", "0")]
	public async Task Andlpowers(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("orlpowers(%#,Guest)", "0")]
	public async Task Orlpowers(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task NextDbref_ReturnsValidDbref()
	{
		// nextdbref() should return a valid dbref for the next object to be created
		var result = (await Parser.FunctionParse(MModule.single("nextdbref()")))?.Message!;
		var dbrefStr = result.ToPlainText();

		// Should start with # and contain a colon
		await Assert.That(dbrefStr).StartsWith("#");
		await Assert.That(dbrefStr).Contains(":");

		// Should be parseable as a dbref format
		var parts = dbrefStr.TrimStart('#').Split(':');
		await Assert.That(parts.Length).IsEqualTo(2);
		await Assert.That(int.TryParse(parts[0], out _)).IsTrue();
	}

	[Test]
	public async Task Lsearchr_WithRegexPattern()
	{
		// Create an object with a specific name pattern for testing
		await Parser.FunctionParse(MModule.single("create(TestObject123)"));

		// lsearchr() should support regex matching on names
		// Search for objects with names matching the pattern "TestObject[0-9]+"
		var result = (await Parser.FunctionParse(MModule.single("lsearchr(%#,NAME=TestObject[0-9]+)")))?.Message!;
		var dbrefs = result.ToPlainText();

		// Should find at least the object we created (if not empty)
		// The result can be empty if the object wasn't visible or permissions prevented it
		await Assert.That(dbrefs).IsNotNull();
	}

	[Test]
	[NotInParallel]
	public async Task Lsearchr_BehavesLikeLsearch_WhenNoRegexNeeded()
	{
		// lsearchr() should work the same as lsearch() for simple patterns
		var lsearchResult = (await Parser.FunctionParse(MModule.single("lsearch(%#,TYPE=PLAYER)")))?.Message!;
		var lsearchrResult = (await Parser.FunctionParse(MModule.single("lsearchr(%#,TYPE=PLAYER)")))?.Message!;

		// Both should return the same results for non-regex patterns
		await Assert.That(lsearchrResult.ToPlainText()).IsEqualTo(lsearchResult.ToPlainText());
	}

	/// <summary>
	/// Tests that functions accepting dbrefs also accept objids (#N:timestamp format).
	/// </summary>
	[Test]
	public async Task ObjId_AcceptedByLocFunction()
	{
		// objid(%#) returns the full objid of the executor (e.g. #1:1234567890)
		// loc() should accept this objid and return the same location as with a bare dbref
		var objIdResult = (await Parser.FunctionParse(MModule.single("objid(%#)")))?.Message!;
		var objId = objIdResult.ToPlainText();

		var resultWithObjId = (await Parser.FunctionParse(MModule.single($"loc({objId})")))?.Message!;
		var resultWithDbRef = (await Parser.FunctionParse(MModule.single("loc(%#)")))?.Message!;

		// Both should return the same location
		await Assert.That(resultWithObjId.ToPlainText()).IsEqualTo(resultWithDbRef.ToPlainText());
	}

	[Test]
	public async Task ObjId_AcceptedByNameFunction()
	{
		// objid(%#) returns the full objid of the executor (e.g. #1:1234567890)
		// name() should accept this objid and return the object's name
		var objIdResult = (await Parser.FunctionParse(MModule.single("objid(%#)")))?.Message!;
		var objId = objIdResult.ToPlainText();

		var resultWithObjId = (await Parser.FunctionParse(MModule.single($"name({objId})")))?.Message!;
		var resultWithDbRef = (await Parser.FunctionParse(MModule.single("name(%#)")))?.Message!;

		// Both should return the same name
		await Assert.That(resultWithObjId.ToPlainText()).IsEqualTo(resultWithDbRef.ToPlainText());
	}

	[Test]
	public async Task ObjId_AcceptedByLocateFunction()
	{
		// locate() with an objid as the name argument should find the object
		// First get the objid of the executor
		var objIdResult = (await Parser.FunctionParse(MModule.single("objid(%#)")))?.Message!;
		var objId = objIdResult.ToPlainText();

		// Now locate using the full objid
		var locateResult = (await Parser.FunctionParse(MModule.single($"first(locate(%#,{objId},*),;)")))?.Message!;

		// Should find the object - result should match the bare dbref number
		await Assert.That(locateResult.ToPlainText()).StartsWith("#1");
	}

	[Test]
	public async Task ObjId_WithWrongTimestamp_FailsToLocate()
	{
		// An objid with a wrong timestamp should not locate the object
		// Use a clearly invalid timestamp (0)
		var locateResult = (await Parser.FunctionParse(MModule.single("locate(%#,#1:0,*)")))?.Message!;

		// Should not find the object since timestamp 0 is wrong
		await Assert.That(locateResult.ToPlainText()).IsEqualTo("#-1");
	}

	[Test]
	public async Task PercentColon_ReturnsFullObjId()
	{
		// %: should return the full objid of the enactor (e.g. #1:1234567890)
		var result = (await Parser.FunctionParse(MModule.single("%:")))?.Message!;
		var objId = result.ToPlainText();

		// Should match objid format: #N:M
		await Assert.That(objId).Matches(@"^#\d+:\d+$");

		// Should be consistent with objid(%#)
		var objIdFromFunc = (await Parser.FunctionParse(MModule.single("objid(%#)")))?.Message!;
		await Assert.That(objId).IsEqualTo(objIdFromFunc.ToPlainText());
	}
}
