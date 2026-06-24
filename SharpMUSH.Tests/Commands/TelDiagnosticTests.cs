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
			.Where(m => m != null && (m.Contains("#-1") || m.Contains("can't see") || m.Contains("don't see") || m.Contains("can't go")))
			.ToList()!;
	}

	private async ValueTask WaitUntilAsync(Func<ValueTask<bool>> condition, string failureMessage, int attempts = 20, int delayMs = 100)
	{
		for (var attempt = 0; attempt < attempts; attempt++)
		{
			if (await condition())
			{
				return;
			}

			await Task.Delay(delayMs);
		}

		throw new TimeoutException(failureMessage);
	}

	/// <summary>
	/// Verify @tel thing-into-thing by name works without errors.
	/// </summary>
	[Test]
	public async ValueTask TelThingIntoThingByName()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@tel me=#0"));

		var objName = TestIsolationHelpers.GenerateUniqueName("telobj");
		var destName = TestIsolationHelpers.GenerateUniqueName("teldst");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {objName}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {destName}"));

		var numObj = await Eval($"num({objName})");
		var numDest = await Eval($"num({destName})");
		await Assert.That(numObj).IsNotEqualTo("#-1")
			.Because("source object should be locatable by name before @tel");
		await Assert.That(numDest).IsNotEqualTo("#-1")
			.Because("destination object should be locatable by name before @tel");

		var errors = await ExecAndCollectErrors($"@tel {objName}={destName}");

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
		var objName = TestIsolationHelpers.GenerateUniqueName("telobj2");
		var destName = TestIsolationHelpers.GenerateUniqueName("teldst2");
		var r1 = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {objName}"));
		var obj = r1.Message!.ToPlainText()!.Trim();
		var r2 = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {destName}"));
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
		await Parser.CommandParse(1, ConnectionService, MModule.single("@tel me=#0"));

		var containerName = TestIsolationHelpers.GenerateUniqueName("telcont");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {containerName}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {containerName}=ENTER_OK"));

		var errors = await ExecAndCollectErrors($"@tel {containerName}");

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
		await Parser.CommandParse(1, ConnectionService, MModule.single("@tel me=#0"));

		var objName = TestIsolationHelpers.GenerateUniqueName("ngobj");
		var r = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {objName}"));
		var dbref = r.Message!.ToPlainText()!.Trim();
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&last_mod {dbref}=2024-01-01"));

		Console.WriteLine($"Created {objName} = {dbref}");

		var nameResult = await Eval($"name({dbref})");
		Console.WriteLine($"name({dbref}) = {nameResult}");

		var getResult = await Eval($"get({dbref}/last_mod)");
		Console.WriteLine($"get({dbref}/last_mod) = {getResult}");

		await WaitUntilAsync(async () =>
			(await Eval($"name({objName})")) == objName &&
			(await Eval($"get({objName}/last_mod)")) == "2024-01-01",
			"name() and get() by object name never became visible after object creation");

		var nameResult2 = await Eval($"name({objName})");
		Console.WriteLine($"name({objName}) = {nameResult2}");

		var getResult2 = await Eval($"get({objName}/last_mod)");
		Console.WriteLine($"get({objName}/last_mod) = {getResult2}");

		await Assert.That(nameResult).IsEqualTo(objName)
			.Because("name() should return the object name");
		await Assert.That(getResult).IsEqualTo("2024-01-01")
			.Because("get() should return the attribute value");
		await Assert.That(nameResult2).IsEqualTo(objName)
			.Because("name() by name should return the object name");
		await Assert.That(getResult2).IsEqualTo("2024-01-01")
			.Because("get() by name should return the attribute value");
	}

	/// <summary>
	/// Prove: name() and get() work on objects INSIDE containers using dbrefs.
	/// This mimics the BBS flow: after @tel bbpocket=mbboard, the BBS reads
	/// group objects using their dbrefs via name(dbref) and get(dbref/attr).
	/// </summary>
	[Test]
	public async ValueTask NameAndGetOnObjectInsideContainer()
	{
		var containerName = TestIsolationHelpers.GenerateUniqueName("diagcont");
		var innerName = TestIsolationHelpers.GenerateUniqueName("diaginn");
		var r1 = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {containerName}"));
		var containerDbref = r1.Message!.ToPlainText()!.Trim();
		var r2 = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {innerName}"));
		var innerDbref = r2.Message!.ToPlainText()!.Trim();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&test_attr {innerDbref}=test_value"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {innerDbref}={containerDbref}"));

		var nameResult = await Eval($"name({innerDbref})");
		Console.WriteLine($"name({innerDbref}) [inside container] = {nameResult}");

		var getResult = await Eval($"get({innerDbref}/test_attr)");
		Console.WriteLine($"get({innerDbref}/test_attr) [inside container] = {getResult}");

		await Assert.That(nameResult).IsEqualTo(innerName)
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
		await Parser.CommandParse(1, ConnectionService, MModule.single("@tel me=#0"));

		var outerName = TestIsolationHelpers.GenerateUniqueName("diagout");
		var innerName = TestIsolationHelpers.GenerateUniqueName("diagitm");
		var r1 = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {outerName}"));
		var outerDbref = r1.Message!.ToPlainText()!.Trim();
		var r2 = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {innerName}"));
		var innerDbref = r2.Message!.ToPlainText()!.Trim();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&test_attr {innerDbref}=inner_value"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {innerDbref}={outerDbref}"));

		var nameResult = await Eval($"name({innerName})");
		Console.WriteLine($"name({innerName}) [inside container, by name] = {nameResult}");

		var getResult = await Eval($"get({innerName}/test_attr)");
		Console.WriteLine($"get({innerName}/test_attr) [inside container, by name] = {getResult}");

		Console.WriteLine($"[DIAGNOSTIC] name() by name inside container: '{nameResult}'");
		Console.WriteLine($"[DIAGNOSTIC] get() by name inside container: '{getResult}'");

		// In PennMUSH: objects inside containers can't be found by name
		var nameContainsError = nameResult.Contains("#-1") || nameResult.Contains("can't see");
		var getContainsError = getResult.Contains("#-1") || getResult.Contains("BAD ARGUMENT");
		Console.WriteLine($"[DIAGNOSTIC] name() returned error: {nameContainsError}");
		Console.WriteLine($"[DIAGNOSTIC] get() returned error: {getContainsError}");

		var containerNameResult = await Eval($"name({outerName})");
		Console.WriteLine($"name({outerName}) [container, by name] = {containerNameResult}");
		await Assert.That(containerNameResult).IsEqualTo(outerName)
			.Because("the container should still be findable by name");
	}

	/// <summary>
	/// Prove: @tel by NAME works when both objects are in player's inventory.
	/// </summary>
	[Test]
	public async ValueTask TelByNameWithObjectsInInventory()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@tel me=#0"));

		var srcName = TestIsolationHelpers.GenerateUniqueName("telsrc");
		var dstName = TestIsolationHelpers.GenerateUniqueName("teldst");
		var r1 = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {srcName}"));
		var srcDbref = r1.Message!.ToPlainText()!.Trim();
		var r2 = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {dstName}"));
		var dstDbref = r2.Message!.ToPlainText()!.Trim();

		Console.WriteLine($"Created {srcName} = {srcDbref}");
		Console.WriteLine($"Created {dstName} = {dstDbref}");

		var numSrc = await Eval($"num({srcName})");
		var numDst = await Eval($"num({dstName})");
		Console.WriteLine($"num({srcName}) = {numSrc}");
		Console.WriteLine($"num({dstName}) = {numDst}");

		var errors = await ExecAndCollectErrors($"@tel {srcName}={dstName}");
		foreach (var e in errors) Console.WriteLine($"@tel ERROR: {e}");

		await Assert.That(errors).IsEmpty()
			.Because("@tel by name should succeed when both objects are in player's inventory");

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
		await Parser.CommandParse(1, ConnectionService, MModule.single("@tel me=#0"));

		var pocketName = TestIsolationHelpers.GenerateUniqueName("bbspkt");
		var boardName = TestIsolationHelpers.GenerateUniqueName("bbsbrd");
		var r1 = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {pocketName}"));
		var pocketDbref = r1.Message!.ToPlainText()!.Trim();
		var r2 = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {boardName}"));
		var boardDbref = r2.Message!.ToPlainText()!.Trim();

		Console.WriteLine($"Created {pocketName} = {pocketDbref}");
		Console.WriteLine($"Created {boardName} = {boardDbref}");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&test_ref {pocketDbref}=Object is #222"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&groups {pocketDbref}="));

		// num() returns bare #N format (PennMUSH behavior), while @create returns #N:timestamp.
		var pocketBareDbref = pocketDbref.Contains(':') ? pocketDbref[..pocketDbref.IndexOf(':')] : pocketDbref;
		var boardBareDbref = boardDbref.Contains(':') ? boardDbref[..boardDbref.IndexOf(':')] : boardDbref;

		var numPocket = await Eval($"num({pocketName})");
		var numBoard = await Eval($"num({boardName})");
		Console.WriteLine($"num({pocketName}) = {numPocket}");
		Console.WriteLine($"num({boardName}) = {numBoard}");

		await Assert.That(numPocket).IsEqualTo(pocketBareDbref)
			.Because("num() should find the pocket object by name and return bare #N format");
		await Assert.That(numBoard).IsEqualTo(boardBareDbref)
			.Because("num() should find the board object by name and return bare #N format");

		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@edit {pocketDbref}/*=#222,{pocketDbref}"));

		var testRef = await Eval($"get({pocketDbref}/test_ref)");
		Console.WriteLine($"get({pocketDbref}/test_ref) after @edit = {testRef}");
		await Assert.That(testRef).IsEqualTo($"Object is {pocketDbref}")
			.Because("@edit should have replaced #222 with the actual dbref");

		var errors = await ExecAndCollectErrors($"@tel {pocketName}={boardName}");
		foreach (var e in errors) Console.WriteLine($"@tel ERROR: {e}");

		await Assert.That(errors).IsEmpty()
			.Because("@tel by name should work after @edit, even without @wait");

		var nameAfter = await Eval($"name({pocketDbref})");
		var getAfter = await Eval($"get({pocketDbref}/test_ref)");
		Console.WriteLine($"name({pocketDbref}) after @tel = {nameAfter}");
		Console.WriteLine($"get({pocketDbref}/test_ref) after @tel = {getAfter}");

		await Assert.That(nameAfter).IsEqualTo(pocketName)
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
		var r1 = await Eval("get(/LAST_MOD)");
		Console.WriteLine($"get(/LAST_MOD) = {r1}");
		await Assert.That(r1).Contains("BAD ARGUMENT FORMAT")
			.Because("get() with empty object should return BAD ARGUMENT FORMAT");

		var r2 = await Eval("get(#-1/LAST_MOD)");
		Console.WriteLine($"get(#-1/LAST_MOD) = {r2}");

		var r3 = await Eval("get(noobject)");
		Console.WriteLine($"get(noobject) = {r3}");
		await Assert.That(r3).Contains("BAD ARGUMENT FORMAT")
			.Because("get() without slash should return BAD ARGUMENT FORMAT");

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
		var r1 = await Eval("iter(,name(##))");
		Console.WriteLine($"iter(,name(##)) = '{r1}'");

		// Create a unique object and set an empty attribute on it to avoid mutating shared state
		var emptyAttrObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "IterEmpty");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&empty_attr {emptyAttrObj}="));
		var r2 = await Eval($"iter(get({emptyAttrObj}/empty_attr),name(##))");
		Console.WriteLine($"iter(get({emptyAttrObj}/empty_attr),name(##)) = '{r2}'");

		var r3 = await Eval("iter( ,name(##))");
		Console.WriteLine($"iter( ,name(##)) = '{r3}'");

		var r4 = await Eval("iter(#0 #1,name(##))");
		Console.WriteLine($"iter(#0 #1,name(##)) = '{r4}'");

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
		var pocketName = TestIsolationHelpers.GenerateUniqueName("bbsrpkt");
		var boardName = TestIsolationHelpers.GenerateUniqueName("bbsrbrd");
		var r1 = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {pocketName}"));
		var pocketDbref = r1.Message!.ToPlainText()!.Trim();
		var r2 = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {boardName}"));
		var boardDbref = r2.Message!.ToPlainText()!.Trim();

		Console.WriteLine($"pocket = {pocketDbref}");
		Console.WriteLine($"board = {boardDbref}");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&groups {pocketDbref}="));
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&valid_groups {pocketDbref}=iter(v(groups),switch(1,1,##))"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {pocketDbref}={boardDbref}"));

		var validGroups = await Eval($"u({pocketDbref}/valid_groups)");
		Console.WriteLine($"u({pocketDbref}/valid_groups) = '{validGroups}'");
		Console.WriteLine($"words of valid_groups = {validGroups.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length}");

		var groups = await Eval($"get({pocketDbref}/groups)");
		Console.WriteLine($"get({pocketDbref}/groups) = '{groups}'");

		var iterResult = await Eval($"iter(get({pocketDbref}/groups),name(##))");
		Console.WriteLine($"iter(get({pocketDbref}/groups),name(##)) = '{iterResult}'");

		if (!string.IsNullOrEmpty(iterResult))
		{
			Console.WriteLine($"[DIAGNOSTIC] PHANTOM ITERATION DETECTED: iter on empty groups produced '{iterResult}'");
			Console.WriteLine("[DIAGNOSTIC] This explains the BBS +bbread error:");
			Console.WriteLine("[DIAGNOSTIC]   name(empty_string) -> #-1 CAN'T SEE THAT HERE");
			Console.WriteLine("[DIAGNOSTIC]   get(empty_string/LAST_MOD) -> #-1 BAD ARGUMENT FORMAT TO GET");
		}

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
		var r1 = await Eval("words(#-1 NO SUCH OBJECT VISIBLE)");
		Console.WriteLine($"words(#-1 NO SUCH OBJECT VISIBLE) = {r1}");

		var r2 = await Eval("words(#-1 BAD ARGUMENT FORMAT TO GET)");
		Console.WriteLine($"words(#-1 BAD ARGUMENT FORMAT TO GET) = {r2}");

		var r3 = await Eval("words(#-1 CAN'T SEE THAT HERE)");
		Console.WriteLine($"words(#-1 CAN'T SEE THAT HERE) = {r3}");

		await Assert.That(r1).IsEqualTo("5")
			.Because("words() on '#-1 NO SUCH OBJECT VISIBLE' should count 5 words");
		await Assert.That(r2).IsEqualTo("6")
			.Because("words() on '#-1 BAD ARGUMENT FORMAT TO GET' should count 6 words");

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
		await Parser.CommandParse(1, ConnectionService, MModule.single("@tel me=#0"));

		var aName = TestIsolationHelpers.GenerateUniqueName("frcda");
		var bName = TestIsolationHelpers.GenerateUniqueName("frcdb");
		var r1 = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {aName}"));
		var aDbref = r1.Message!.ToPlainText()!.Trim();
		var r2 = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {bName}"));
		var bDbref = r2.Message!.ToPlainText()!.Trim();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&ref {aDbref}=#222"));

		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@force me=@edit {aDbref}/*=#222,{aDbref}"));

		var refVal = await Eval($"get({aDbref}/ref)");
		Console.WriteLine($"After @force @edit: get({aDbref}/ref) = {refVal}");
		await Assert.That(refVal).IsEqualTo(aDbref)
			.Because("@force @edit should have replaced #222 with actual dbref");

		await WaitUntilAsync(async () =>
			!(await Eval($"num({aName})")).Contains("NO MATCH", StringComparison.OrdinalIgnoreCase) &&
			!(await Eval($"num({bName})")).Contains("NO MATCH", StringComparison.OrdinalIgnoreCase),
			"@tel source/destination objects never became locatable by name after @force @edit");

		var errors = await ExecAndCollectErrors($"@tel {aName}={bName}");
		foreach (var e in errors) Console.WriteLine($"@tel ERROR: {e}");

		await Assert.That(errors).IsEmpty()
			.Because("@tel by name should work after @force @edit");
	}
}
