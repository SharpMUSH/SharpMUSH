using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// Comprehensive lock function integration tests based on PennMUSH oracle verification.
/// These tests set up lock state via commands, then verify function behavior.
/// Oracle test file: SharpMUSH.Tests/PennMUSH/test_locks.t
/// </summary>
public class LockIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private async Task<string> Eval(string expr)
	{
		var result = (await FunctionParser.FunctionParse(MModule.single(expr)))?.Message!;
		return result.ToPlainText();
	}

	private async Task Command(string cmd)
	{
		await CommandParser.CommandParse(1, ConnectionService, MModule.single(cmd));
	}

	private async Task<DBRef> CreateObject(string name)
	{
		var result = await CommandParser.CommandParse(1, ConnectionService, MModule.single($"@create {name}"));
		return DBRef.Parse(result.Message!.ToPlainText()!);
	}

	// === lock() case insensitivity ===
	// Oracle: lock(obj/Basic), lock(obj/basic), lock(obj/BASIC) all return same result

	[Test]
	[Arguments("Basic")]
	[Arguments("basic")]
	[Arguments("BASIC")]
	public async Task Elock_CaseInsensitive_NoLock(string lockName)
	{
		// Create a dedicated object to avoid parallel test interference
		var obj = await CreateObject($"ElockCaseNoLock_{lockName}");

		// No lock set = passes (TRUE_BOOLEXP)
		var result = await Eval($"elock(#{obj.Number}/{lockName},%#)");
		await Assert.That(result).IsEqualTo("1");
	}

	[Test]
	public async Task Elock_CaseInsensitive_NoLock_DefaultLock()
	{
		var obj = await CreateObject("ElockCaseNoLockDefault");

		// elock(obj, victim) without /lockname defaults to Basic
		var result = await Eval($"elock(#{obj.Number},%#)");
		await Assert.That(result).IsEqualTo("1");
	}

	// === testlock() with #TRUE and #FALSE ===
	// Oracle confirmed: testlock(#TRUE, me) = 1, testlock(#FALSE, me) = 0

	[Test]
	[Arguments("testlock(#TRUE,%#)", "1")]
	[Arguments("testlock(#FALSE,%#)", "0")]
	[Arguments("testlock(#1,%#)", "1")]
	public async Task Testlock_Constants(string str, string expected)
	{
		var result = await Eval(str);
		await Assert.That(result).IsEqualTo(expected);
	}

	// === Boolean lock operators ===
	// Oracle confirmed: AND (&), OR (|), NOT (!) all work in testlock

	[Test]
	[Arguments("testlock(#TRUE&#TRUE,%#)", "1")]
	[Arguments("testlock(#TRUE&#FALSE,%#)", "0")]
	[Arguments("testlock(#FALSE&#FALSE,%#)", "0")]
	public async Task Testlock_BooleanAnd(string str, string expected)
	{
		var result = await Eval(str);
		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("testlock(#TRUE|#FALSE,%#)", "1")]
	[Arguments("testlock(#FALSE|#FALSE,%#)", "0")]
	[Arguments("testlock(#TRUE|#TRUE,%#)", "1")]
	public async Task Testlock_BooleanOr(string str, string expected)
	{
		var result = await Eval(str);
		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("testlock(!#FALSE,%#)", "1")]
	[Arguments("testlock(!#TRUE,%#)", "0")]
	public async Task Testlock_BooleanNot(string str, string expected)
	{
		var result = await Eval(str);
		await Assert.That(result).IsEqualTo(expected);
	}

	// === elock with #TRUE and #FALSE locks set ===
	// Oracle: @lock/use obj=#TRUE → elock(obj/Use, victim) = 1 for everyone
	// Oracle: @lock/use obj=#FALSE → elock(obj/Use, victim) = 0 for everyone (including god)

	[Test]
	public async Task Elock_TrueLock_PassesForAll()
	{
		var obj = await CreateObject("ElockTrueTest");
		await Command($"@lock/use #{obj.Number}=#TRUE");

		var result = await Eval($"elock(#{obj.Number}/Use,%#)");
		await Assert.That(result).IsEqualTo("1");
	}

	[Test]
	public async Task Elock_FalseLock_FailsForAll()
	{
		var obj = await CreateObject("ElockFalseTest");
		await Command($"@lock/use #{obj.Number}=#FALSE");

		// Oracle confirmed: even god fails #FALSE lock
		var result = await Eval($"elock(#{obj.Number}/Use,%#)");
		await Assert.That(result).IsEqualTo("0");
	}

	// === Attribute locks (ATTR:pattern) ===
	// Oracle: testlock(RACE:Elf, me) = 1 when &RACE me=Elf

	[Test]
	public async Task Testlock_AttributeLock_ExactMatch()
	{
		var obj = await CreateObject("AttrLockExactObj");
		await Command($"&LOCKTEST_RACE #{obj.Number}=Elf");

		var result = await Eval($"testlock(LOCKTEST_RACE:Elf,#{obj.Number})");
		await Assert.That(result).IsEqualTo("1");
	}

	[Test]
	public async Task Testlock_AttributeLock_NoMatch()
	{
		var obj = await CreateObject("AttrLockNoMatchObj");
		await Command($"&LOCKTEST_RACE2 #{obj.Number}=Elf");

		var result = await Eval($"testlock(LOCKTEST_RACE2:Dwarf,#{obj.Number})");
		await Assert.That(result).IsEqualTo("0");
	}

	[Test]
	public async Task Testlock_AttributeLock_Wildcard()
	{
		var obj = await CreateObject("AttrLockWildObj");
		await Command($"&LOCKTEST_RACE3 #{obj.Number}=Elf");

		var result = await Eval($"testlock(LOCKTEST_RACE3:E*,#{obj.Number})");
		await Assert.That(result).IsEqualTo("1");
	}

	// === Eval locks (ATTR/pattern) ===
	// Oracle: @lock obj=CHECK/PASS → elock passes when &CHECK obj=PASS

	[Test]
	public async Task Elock_EvalLock_Passes()
	{
		var obj = await CreateObject("EvalLockPassTest");
		await Command($"&CHECK #{obj.Number}=PASS");
		await Command($"@lock #{obj.Number}=CHECK/PASS");

		var result = await Eval($"elock(#{obj.Number}/Basic,%#)");
		await Assert.That(result).IsEqualTo("1");
	}

	[Test]
	public async Task Elock_EvalLock_Fails()
	{
		var obj = await CreateObject("EvalLockFailTest");
		await Command($"&CHECK #{obj.Number}=FAIL");
		await Command($"@lock #{obj.Number}=CHECK/PASS");

		var result = await Eval($"elock(#{obj.Number}/Basic,%#)");
		await Assert.That(result).IsEqualTo("0");
	}

	// === Flag locks (FLAG^flagname) ===
	// Oracle: testlock(FLAG^WIZARD, me) = 1 for wizards, 0 for mortals

	[Test]
	public async Task Testlock_FlagLock_Wizard()
	{
		// Player #1 (executor) should be a wizard
		var result = await Eval("testlock(FLAG^WIZARD,%#)");
		await Assert.That(result).IsEqualTo("1");
	}

	// === Indirect locks (@obj/locktype) ===
	// Oracle: testlock(@obj/Use, me) checks obj's Use lock

	[Test]
	public async Task Testlock_IndirectLock_Passes()
	{
		var obj = await CreateObject("IndirectLockPassTest");
		await Command($"@lock/use #{obj.Number}=#TRUE");

		var result = await Eval($"testlock(@#{obj.Number}/Use,%#)");
		await Assert.That(result).IsEqualTo("1");
	}

	[Test]
	public async Task Testlock_IndirectLock_Fails()
	{
		var obj = await CreateObject("IndirectLockFailTest");
		await Command($"@lock/use #{obj.Number}=#FALSE");

		var result = await Eval($"testlock(@#{obj.Number}/Use,%#)");
		await Assert.That(result).IsEqualTo("0");
	}

	// === llocks() with multiple lock types ===
	// Oracle: llocks(obj) returns "Basic Use" when both are set

	[Test]
	public async Task Llocks_MultipleLocks()
	{
		var obj = await CreateObject("LlocksMultiTest");
		await Command($"@lock #{obj.Number}=#TRUE");
		await Command($"@lock/use #{obj.Number}=#TRUE");

		var result = await Eval($"llocks(#{obj.Number})");
		// Should contain both Basic and Use
		await Assert.That(result).Contains("Basic");
		await Assert.That(result).Contains("Use");
	}

	// === lockowner() with lock set ===
	// Oracle: lockowner(obj/Basic) returns the dbref of who set the lock

	[Test]
	public async Task Lockowner_WithLockSet()
	{
		var obj = await CreateObject("LockOwnerTest");
		await Command($"@lock #{obj.Number}=#TRUE");

		var result = await Eval($"lockowner(#{obj.Number}/Basic)");
		// Lock was set by player #1
		await Assert.That(result).IsEqualTo("#1");
	}

	// === lock() returns lock string ===

	[Test]
	public async Task Lock_ReturnsLockString()
	{
		var obj = await CreateObject("LockStringTest");
		await Command($"@lock #{obj.Number}=#1");

		var result = await Eval($"lock(#{obj.Number})");
		await Assert.That(result).IsEqualTo("#1");
	}

	[Test]
	public async Task Lock_ReturnsUnlocked_WhenNoLock()
	{
		var obj = await CreateObject("LockUnlockedTest");

		var result = await Eval($"lock(#{obj.Number})");
		await Assert.That(result).IsEqualTo("*UNLOCKED*");
	}

	// === lock() case insensitive lock name ===

	[Test]
	public async Task Lock_CaseInsensitiveLockName()
	{
		var obj = await CreateObject("LockCaseTest");
		await Command($"@lock #{obj.Number}=#1");

		var basic = await Eval($"lock(#{obj.Number}/Basic)");
		var lower = await Eval($"lock(#{obj.Number}/basic)");
		var upper = await Eval($"lock(#{obj.Number}/BASIC)");

		await Assert.That(basic).IsEqualTo("#1");
		await Assert.That(lower).IsEqualTo("#1");
		await Assert.That(upper).IsEqualTo("#1");
	}

	// === testlock with IS prefix (=) ===
	// Oracle: testlock(=me, me) = 1 (exact IS check)

	[Test]
	public async Task Testlock_IsPrefix()
	{
		var result = await Eval("testlock(=%#,%#)");
		await Assert.That(result).IsEqualTo("1");
	}

	// === testlock with carry prefix (+) ===
	// Oracle: testlock(+me, me) = 0 (can't carry yourself)

	[Test]
	public async Task Testlock_CarryPrefix_Self()
	{
		var result = await Eval("testlock(+%#,%#)");
		await Assert.That(result).IsEqualTo("0");
	}

	// === testlock with carry prefix (+) on carried object ===
	// Oracle: testlock(+num(TestObj), me) = 1 when TestObj is in inventory

	[Test]
	public async Task Testlock_CarryPrefix_CarriedObject()
	{
		var obj = await CreateObject("CarryLockTest");
		// Object was created by executor, so it's in their inventory
		var result = await Eval($"testlock(+#{obj.Number},%#)");
		await Assert.That(result).IsEqualTo("1");
	}

	// === testlock with owner prefix ($) ===
	// Oracle: testlock($num(TestObj), me) = 1 when me owns TestObj

	[Test]
	public async Task Testlock_OwnerPrefix()
	{
		var obj = await CreateObject("OwnerLockTest");
		var result = await Eval($"testlock($#{obj.Number},%#)");
		await Assert.That(result).IsEqualTo("1");
	}
}
