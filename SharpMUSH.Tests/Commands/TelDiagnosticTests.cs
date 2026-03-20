using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.Core;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class TelDiagnosticTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private static string? ExtractMessage(ICall call)
	{
		if (call.GetMethodInfo().Name != nameof(INotifyService.Notify)) return null;
		var args = call.GetArguments();
		if (args.Length < 2) return null;
		if (args[1] is OneOf<MString, string> oneOf)
			return oneOf.Match(mstr => mstr.ToString(), str => str);
		if (args[1] is string str2) return str2;
		if (args[1] is MString mstr2) return mstr2.ToString();
		return null;
	}

	/// <summary>
	/// Helper: evaluate a MUSH expression and return the plain text result.
	/// </summary>
	private async ValueTask<string> Eval(string expression)
	{
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single($"think {expression}"));
		return result.Message?.ToPlainText()?.Trim() ?? "";
	}

	/// <summary>
	/// Helper: execute a command and collect error notifications produced.
	/// </summary>
	private async ValueTask<List<string>> ExecAndCollectErrors(string command)
	{
		var preCount = NotifyService.ReceivedCalls().Count();
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));
		return NotifyService.ReceivedCalls().Skip(preCount)
			.Select(ExtractMessage)
			.Where(m => m != null && (m.Contains("#-1") || m.Contains("can't see") || m.Contains("can't go")))
			.ToList()!;
	}

	/// <summary>
	/// Verify @tel thing-into-thing by name works without errors.
	/// </summary>
	[Test]
	public async ValueTask TelThingIntoThingByName()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create telfixtest_obj"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create telfixtest_dest"));

		var errors = await ExecAndCollectErrors("@tel telfixtest_obj=telfixtest_dest");

		foreach (var e in errors) Console.WriteLine($"ERROR: {e}");
		await Assert.That(errors).IsEmpty()
			.Because("@tel of one thing into another thing by name should work without errors");
	}

	/// <summary>
	/// Verify @tel thing-into-thing by dbref works without errors.
	/// </summary>
	[Test]
	public async ValueTask TelThingIntoThingByDbref()
	{
		var r1 = await Parser.CommandParse(1, ConnectionService, MModule.single("@create telfixtest_obj2"));
		var obj = r1.Message!.ToPlainText()!.Trim();
		var r2 = await Parser.CommandParse(1, ConnectionService, MModule.single("@create telfixtest_dest2"));
		var dest = r2.Message!.ToPlainText()!.Trim();

		var errors = await ExecAndCollectErrors($"@tel {obj}={dest}");

		foreach (var e in errors) Console.WriteLine($"ERROR: {e}");
		await Assert.That(errors).IsEmpty()
			.Because("@tel of one thing into another thing by dbref should work without errors");
	}

	/// <summary>
	/// Verify @tel with single arg (self-teleport) works when destination is a thing.
	/// </summary>
	[Test]
	public async ValueTask TelSelfIntoThing()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create telfixtest_container"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set telfixtest_container=ENTER_OK"));

		var errors = await ExecAndCollectErrors("@tel telfixtest_container");

		foreach (var e in errors) Console.WriteLine($"ERROR: {e}");
		await Assert.That(errors).IsEmpty()
			.Because("@tel self into a container should work without errors");
	}

	/// <summary>
	/// Test name() and get() with various object patterns to verify they work.
	/// </summary>
	[Test]
	public async ValueTask NameAndGetFunctions()
	{
		var r = await Parser.CommandParse(1, ConnectionService, MModule.single("@create ngtest_obj"));
		var dbref = r.Message!.ToPlainText()!.Trim();
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&last_mod {dbref}=2024-01-01"));

		Console.WriteLine($"Created ngtest_obj = {dbref}");

		var nameResult = await Eval($"name({dbref})");
		Console.WriteLine($"name({dbref}) = {nameResult}");

		var getResult = await Eval($"get({dbref}/last_mod)");
		Console.WriteLine($"get({dbref}/last_mod) = {getResult}");

		var nameResult2 = await Eval("name(ngtest_obj)");
		Console.WriteLine($"name(ngtest_obj) = {nameResult2}");

		var getResult2 = await Eval("get(ngtest_obj/last_mod)");
		Console.WriteLine($"get(ngtest_obj/last_mod) = {getResult2}");

		await Assert.That(nameResult).IsEqualTo("ngtest_obj")
			.Because("name() should return the object name");
		await Assert.That(getResult).IsEqualTo("2024-01-01")
			.Because("get() should return the attribute value");
		await Assert.That(nameResult2).IsEqualTo("ngtest_obj")
			.Because("name() by name should return the object name");
		await Assert.That(getResult2).IsEqualTo("2024-01-01")
			.Because("get() by name should return the attribute value");
	}

	// ========================================================================
	// BBS-style diagnostic tests: prove where @tel and get() actually fail
	// ========================================================================

	/// <summary>
	/// Prove: name() and get() work on objects INSIDE containers using dbrefs.
	/// This mimics the BBS flow: after @tel bbpocket=mbboard, the BBS reads
	/// group objects using their dbrefs via name(dbref) and get(dbref/attr).
	/// </summary>
	[Test]
	public async ValueTask NameAndGetOnObjectInsideContainer()
	{
		// Create a container and an inner object
		var r1 = await Parser.CommandParse(1, ConnectionService, MModule.single("@create diag_container"));
		var containerDbref = r1.Message!.ToPlainText()!.Trim();
		var r2 = await Parser.CommandParse(1, ConnectionService, MModule.single("@create diag_inner"));
		var innerDbref = r2.Message!.ToPlainText()!.Trim();

		// Set an attribute on the inner object
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&test_attr {innerDbref}=test_value"));

		// Teleport inner inside container (no @wait - direct)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {innerDbref}={containerDbref}"));

		// Now test name() and get() on the inner object by DBREF
		var nameResult = await Eval($"name({innerDbref})");
		Console.WriteLine($"name({innerDbref}) [inside container] = {nameResult}");

		var getResult = await Eval($"get({innerDbref}/test_attr)");
		Console.WriteLine($"get({innerDbref}/test_attr) [inside container] = {getResult}");

		await Assert.That(nameResult).IsEqualTo("diag_inner")
			.Because("name() by dbref should work even when object is inside a container");
		await Assert.That(getResult).IsEqualTo("test_value")
			.Because("get() by dbref should work even when object is inside a container");
	}

	/// <summary>
	/// Prove: name() and get() fail/succeed on objects INSIDE containers using NAMES.
	/// In PennMUSH, objects inside containers are not directly locatable by name.
	/// </summary>
	[Test]
	public async ValueTask NameAndGetOnObjectInsideContainerByName()
	{
		var r1 = await Parser.CommandParse(1, ConnectionService, MModule.single("@create diag_outerbox"));
		var outerDbref = r1.Message!.ToPlainText()!.Trim();
		var r2 = await Parser.CommandParse(1, ConnectionService, MModule.single("@create diag_inneritem"));
		var innerDbref = r2.Message!.ToPlainText()!.Trim();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&test_attr {innerDbref}=inner_value"));

		// Teleport inner inside outer
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {innerDbref}={outerDbref}"));

		// Try name() and get() by NAME (not dbref) - these may fail because
		// the object is no longer in the player's inventory or location
		var nameResult = await Eval("name(diag_inneritem)");
		Console.WriteLine($"name(diag_inneritem) [inside container, by name] = {nameResult}");

		var getResult = await Eval("get(diag_inneritem/test_attr)");
		Console.WriteLine($"get(diag_inneritem/test_attr) [inside container, by name] = {getResult}");

		// Document what actually happens (this test documents behavior, not assert success)
		Console.WriteLine($"[DIAGNOSTIC] name() by name inside container: '{nameResult}'");
		Console.WriteLine($"[DIAGNOSTIC] get() by name inside container: '{getResult}'");

		// In PennMUSH: objects inside containers can't be found by name
		// The locate service should NOT find the object by name
		// This is expected behavior, but we document it for the BBS analysis
		var nameContainsError = nameResult.Contains("#-1") || nameResult.Contains("can't see");
		var getContainsError = getResult.Contains("#-1") || getResult.Contains("BAD ARGUMENT");
		Console.WriteLine($"[DIAGNOSTIC] name() returned error: {nameContainsError}");
		Console.WriteLine($"[DIAGNOSTIC] get() returned error: {getContainsError}");

		// Prove: the container itself IS still findable by name
		var containerName = await Eval("name(diag_outerbox)");
		Console.WriteLine($"name(diag_outerbox) [container, by name] = {containerName}");
		await Assert.That(containerName).IsEqualTo("diag_outerbox")
			.Because("the container should still be findable by name");
	}

	/// <summary>
	/// Prove: @tel by NAME fails when both objects are in player's inventory.
	/// This is the key test the user reported - @tel fails outside @wait context.
	/// </summary>
	[Test]
	public async ValueTask TelByNameWithObjectsInInventory()
	{
		// Create fresh objects with unique names
		var r1 = await Parser.CommandParse(1, ConnectionService, MModule.single("@create telname_src"));
		var srcDbref = r1.Message!.ToPlainText()!.Trim();
		var r2 = await Parser.CommandParse(1, ConnectionService, MModule.single("@create telname_dst"));
		var dstDbref = r2.Message!.ToPlainText()!.Trim();

		Console.WriteLine($"Created telname_src = {srcDbref}");
		Console.WriteLine($"Created telname_dst = {dstDbref}");

		// Verify both objects are locatable by name BEFORE @tel
		var numSrc = await Eval("num(telname_src)");
		var numDst = await Eval("num(telname_dst)");
		Console.WriteLine($"num(telname_src) = {numSrc}");
		Console.WriteLine($"num(telname_dst) = {numDst}");

		// Try @tel by name (NO @wait)
		var errors = await ExecAndCollectErrors("@tel telname_src=telname_dst");
		foreach (var e in errors) Console.WriteLine($"@tel ERROR: {e}");

		// Verify no errors
		await Assert.That(errors).IsEmpty()
			.Because("@tel by name should succeed when both objects are in player's inventory");

		// Verify the move happened: telname_src should now be inside telname_dst
		var srcLoc = await Eval($"loc({srcDbref})");
		Console.WriteLine($"loc({srcDbref}) after @tel = {srcLoc}");
		await Assert.That(srcLoc).IsEqualTo(dstDbref)
			.Because("source should now be located inside destination after @tel");
	}

	/// <summary>
	/// Prove: the BBS-style @force me=@edit then @tel flow works.
	/// This mimics the exact BBS installation sequence without @wait.
	/// </summary>
	[Test]
	public async ValueTask BBSStyleEditAndTelFlow()
	{
		// Step 1: Create objects like the BBS does
		var r1 = await Parser.CommandParse(1, ConnectionService, MModule.single("@create bbsdiag_pocket"));
		var pocketDbref = r1.Message!.ToPlainText()!.Trim();
		var r2 = await Parser.CommandParse(1, ConnectionService, MModule.single("@create bbsdiag_board"));
		var boardDbref = r2.Message!.ToPlainText()!.Trim();

		Console.WriteLine($"Created bbsdiag_pocket = {pocketDbref}");
		Console.WriteLine($"Created bbsdiag_board = {boardDbref}");

		// Step 2: Set an attribute with a placeholder reference (like BBS uses #222)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&test_ref {pocketDbref}=Object is #222"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&groups {pocketDbref}="));

		// Step 3: Use num() to find objects (like BBS @force me=@edit)
		// num() returns bare #N format (PennMUSH behavior), while @create returns #N:timestamp.
		// Extract just the #N portion from @create output for comparison.
		var pocketBareDbref = pocketDbref.Contains(':') ? pocketDbref[..pocketDbref.IndexOf(':')] : pocketDbref;
		var boardBareDbref = boardDbref.Contains(':') ? boardDbref[..boardDbref.IndexOf(':')] : boardDbref;

		var numPocket = await Eval("num(bbsdiag_pocket)");
		var numBoard = await Eval("num(bbsdiag_board)");
		Console.WriteLine($"num(bbsdiag_pocket) = {numPocket}");
		Console.WriteLine($"num(bbsdiag_board) = {numBoard}");

		await Assert.That(numPocket).IsEqualTo(pocketBareDbref)
			.Because("num() should find the pocket object by name and return bare #N format");
		await Assert.That(numBoard).IsEqualTo(boardBareDbref)
			.Because("num() should find the board object by name and return bare #N format");

		// Step 4: @edit to replace placeholder (like BBS @force me=@edit)
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@edit {pocketDbref}/*=#222,{pocketDbref}"));

		// Verify the @edit worked
		var testRef = await Eval($"get({pocketDbref}/test_ref)");
		Console.WriteLine($"get({pocketDbref}/test_ref) after @edit = {testRef}");
		await Assert.That(testRef).IsEqualTo($"Object is {pocketDbref}")
			.Because("@edit should have replaced #222 with the actual dbref");

		// Step 5: @tel pocket into board (NO @wait - direct, like user reported)
		var errors = await ExecAndCollectErrors($"@tel bbsdiag_pocket=bbsdiag_board");
		foreach (var e in errors) Console.WriteLine($"@tel ERROR: {e}");

		await Assert.That(errors).IsEmpty()
			.Because("@tel by name should work after @edit, even without @wait");

		// Step 6: Verify name() and get() still work on the moved object by DBREF
		var nameAfter = await Eval($"name({pocketDbref})");
		var getAfter = await Eval($"get({pocketDbref}/test_ref)");
		Console.WriteLine($"name({pocketDbref}) after @tel = {nameAfter}");
		Console.WriteLine($"get({pocketDbref}/test_ref) after @tel = {getAfter}");

		await Assert.That(nameAfter).IsEqualTo("bbsdiag_pocket")
			.Because("name() by dbref should work after @tel moved the object into a container");
		await Assert.That(getAfter).IsEqualTo($"Object is {pocketDbref}")
			.Because("get() by dbref should work after @tel moved the object into a container");
	}

	/// <summary>
	/// Prove: get() returns BAD ARGUMENT FORMAT when given invalid patterns.
	/// This tests what happens when get() receives an error string as the object reference.
	/// </summary>
	[Test]
	public async ValueTask GetWithInvalidPatterns()
	{
		// Test get() with empty object part (like what happens when iter() produces empty ##)
		var r1 = await Eval("get(/LAST_MOD)");
		Console.WriteLine($"get(/LAST_MOD) = {r1}");
		await Assert.That(r1).Contains("BAD ARGUMENT FORMAT")
			.Because("get() with empty object should return BAD ARGUMENT FORMAT");

		// Test get() with #-1 dbref
		var r2 = await Eval("get(#-1/LAST_MOD)");
		Console.WriteLine($"get(#-1/LAST_MOD) = {r2}");

		// Test get() with no slash at all
		var r3 = await Eval("get(noobject)");
		Console.WriteLine($"get(noobject) = {r3}");
		await Assert.That(r3).Contains("BAD ARGUMENT FORMAT")
			.Because("get() without slash should return BAD ARGUMENT FORMAT");

		// Document what happens with #-1 (it parses as object=#-1, attr=LAST_MOD)
		Console.WriteLine($"[DIAGNOSTIC] get(#-1/LAST_MOD) returned: '{r2}'");
	}

	/// <summary>
	/// Prove: iter() on empty string behavior.
	/// If iter() on empty string produces a phantom iteration with empty ##,
	/// this explains the BBS +bbread error when no groups exist.
	/// </summary>
	[Test]
	public async ValueTask IterOnEmptyString()
	{
		// Test iter with empty list - should produce NO output
		var r1 = await Eval("iter(,name(##))");
		Console.WriteLine($"iter(,name(##)) = '{r1}'");

		// Test iter with empty attribute - should produce NO output
		// Create a unique object and set an empty attribute on it to avoid mutating shared state
		var emptyAttrObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "IterEmpty");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&empty_attr {emptyAttrObj}="));
		var r2 = await Eval($"iter(get({emptyAttrObj}/empty_attr),name(##))");
		Console.WriteLine($"iter(get({emptyAttrObj}/empty_attr),name(##)) = '{r2}'");

		// Test iter with single space - may produce one phantom iteration
		var r3 = await Eval("iter( ,name(##))");
		Console.WriteLine($"iter( ,name(##)) = '{r3}'");

		// Test iter with valid list
		var r4 = await Eval("iter(#0 #1,name(##))");
		Console.WriteLine($"iter(#0 #1,name(##)) = '{r4}'");

		// Document: if r1 or r2 are non-empty, iter() has phantom iteration bug
		var hasPhantomIter = !string.IsNullOrEmpty(r1) || !string.IsNullOrEmpty(r2);
		Console.WriteLine($"[DIAGNOSTIC] iter() phantom iteration on empty string: {hasPhantomIter}");

		// In PennMUSH, iter(,name(##)) returns empty string
		await Assert.That(r1).IsEqualTo("")
			.Because("iter() on empty list should produce empty output (PennMUSH behavior)");
	}

	/// <summary>
	/// Prove: the exact BBS +bbread failure chain.
	/// Creates the BBS structure, then simulates the +bbread command flow.
	/// </summary>
	[Test]
	public async ValueTask BBSReadFailureChain()
	{
		// Create the BBS objects
		var r1 = await Parser.CommandParse(1, ConnectionService, MModule.single("@create bbsread_pocket"));
		var pocketDbref = r1.Message!.ToPlainText()!.Trim();
		var r2 = await Parser.CommandParse(1, ConnectionService, MModule.single("@create bbsread_board"));
		var boardDbref = r2.Message!.ToPlainText()!.Trim();

		Console.WriteLine($"pocket = {pocketDbref}");
		Console.WriteLine($"board = {boardDbref}");

		// Set attributes like BBS does (GROUPS is empty - no groups created yet)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&groups {pocketDbref}="));
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&valid_groups {pocketDbref}=iter(v(groups),switch(1,1,##))"));

		// Move pocket into board (like @tel bbpocket=mbboard)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {pocketDbref}={boardDbref}"));

		// Now simulate what +bbread does: u(pocket/valid_groups)
		var validGroups = await Eval($"u({pocketDbref}/valid_groups)");
		Console.WriteLine($"u({pocketDbref}/valid_groups) = '{validGroups}'");
		Console.WriteLine($"words of valid_groups = {validGroups.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length}");

		// Test: what does v(groups) return on the pocket?
		var groups = await Eval($"get({pocketDbref}/groups)");
		Console.WriteLine($"get({pocketDbref}/groups) = '{groups}'");

		// Test: what does iter produce with empty groups?
		var iterResult = await Eval($"iter(get({pocketDbref}/groups),name(##))");
		Console.WriteLine($"iter(get({pocketDbref}/groups),name(##)) = '{iterResult}'");

		// If iter produces phantom output, the name() call on empty ## explains the error
		if (!string.IsNullOrEmpty(iterResult))
		{
			Console.WriteLine($"[DIAGNOSTIC] PHANTOM ITERATION DETECTED: iter on empty groups produced '{iterResult}'");
			Console.WriteLine("[DIAGNOSTIC] This explains the BBS +bbread error:");
			Console.WriteLine("[DIAGNOSTIC]   name(empty_string) -> #-1 CAN'T SEE THAT HERE");
			Console.WriteLine("[DIAGNOSTIC]   get(empty_string/LAST_MOD) -> #-1 BAD ARGUMENT FORMAT TO GET");
		}

		// The key diagnostic: does iter() on empty groups produce output?
		await Assert.That(iterResult).IsEqualTo("")
			.Because("iter() on empty groups list should produce no output");
	}

	/// <summary>
	/// Prove: words() on error strings produces misleading counts.
	/// The "6 U" in +bbread output comes from words() counting an error string.
	/// </summary>
	[Test]
	public async ValueTask WordsOnErrorStrings()
	{
		// Test: words() on a typical error string
		var r1 = await Eval("words(#-1 NO SUCH OBJECT VISIBLE)");
		Console.WriteLine($"words(#-1 NO SUCH OBJECT VISIBLE) = {r1}");

		var r2 = await Eval("words(#-1 BAD ARGUMENT FORMAT TO GET)");
		Console.WriteLine($"words(#-1 BAD ARGUMENT FORMAT TO GET) = {r2}");

		var r3 = await Eval("words(#-1 CAN'T SEE THAT HERE)");
		Console.WriteLine($"words(#-1 CAN'T SEE THAT HERE) = {r3}");

		// These confirm the user's insight: words() on error = misleading count
		await Assert.That(r1).IsEqualTo("5")
			.Because("words() on '#-1 NO SUCH OBJECT VISIBLE' should count 5 words");
		await Assert.That(r2).IsEqualTo("6")
			.Because("words() on '#-1 BAD ARGUMENT FORMAT TO GET' should count 6 words");

		// Confirm: words("") should be 0 (not 1)
		// Create a unique object and set empty attribute to avoid mutating shared state
		var emptyObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WordsEmpty");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&wordstest_empty {emptyObj}="));
		var r4 = await Eval($"words(get({emptyObj}/wordstest_empty))");
		Console.WriteLine($"words(get({emptyObj}/wordstest_empty)) = {r4}");
		await Assert.That(r4).IsEqualTo("0")
			.Because("words() on empty string should return 0 (PennMUSH behavior)");
	}

	/// <summary>
	/// Prove: @tel with @force (BBS uses @force me=@edit then @tel).
	/// Tests the exact command sequence without @wait.
	/// </summary>
	[Test]
	public async ValueTask TelAfterForceEdit()
	{
		var r1 = await Parser.CommandParse(1, ConnectionService, MModule.single("@create forcediag_a"));
		var aDbref = r1.Message!.ToPlainText()!.Trim();
		var r2 = await Parser.CommandParse(1, ConnectionService, MModule.single("@create forcediag_b"));
		var bDbref = r2.Message!.ToPlainText()!.Trim();

		// Set attribute with placeholder
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&ref {aDbref}=#222"));

		// @force me=@edit (like BBS)
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@force me=@edit {aDbref}/*=#222,{aDbref}"));

		// Verify edit worked
		var refVal = await Eval($"get({aDbref}/ref)");
		Console.WriteLine($"After @force @edit: get({aDbref}/ref) = {refVal}");
		await Assert.That(refVal).IsEqualTo(aDbref)
			.Because("@force @edit should have replaced #222 with actual dbref");

		// Now @tel by name (NO @wait)
		var errors = await ExecAndCollectErrors("@tel forcediag_a=forcediag_b");
		foreach (var e in errors) Console.WriteLine($"@tel ERROR: {e}");

		await Assert.That(errors).IsEmpty()
			.Because("@tel by name should work after @force @edit");
	}
}
