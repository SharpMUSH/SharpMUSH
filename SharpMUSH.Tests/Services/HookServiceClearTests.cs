using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// <see cref="HookService.ClearHooksOnAsync"/> is given the objects that are going. An object's number
/// can be given to a new object once the old one is destroyed, so a full objid clears only hooks on
/// that object, while a bare dbref (the PennMUSH importer's, which has no creation stamps) clears by
/// number.
/// </summary>
public class HookServiceClearTests
{
	private static readonly DBRef Old = new(4242, 1_000);
	private static readonly DBRef Recycled = new(4242, 2_000);

	[Test]
	public async Task AnObjidLeavesAHookOnTheObjectThatReusedItsNumber()
	{
		var hooks = new HookService();
		await hooks.SetHookAsync("+ZHOOK", "BEFORE", Recycled, "CMD`BEFORE", false, false, false, false);

		var cleared = await hooks.ClearHooksOnAsync([Old]);

		await Assert.That(cleared).IsEmpty();
		await Assert.That((await hooks.GetAllHooksAsync("+ZHOOK")).ContainsKey("BEFORE")).IsTrue();
	}

	[Test]
	public async Task AnObjidClearsAHookOnThatObject()
	{
		var hooks = new HookService();
		await hooks.SetHookAsync("+ZHOOK", "BEFORE", Old, "CMD`BEFORE", false, false, false, false);

		var cleared = await hooks.ClearHooksOnAsync([Old]);

		await Assert.That(cleared).IsEquivalentTo([new ClearedHook("+ZHOOK", "BEFORE")]);
		await Assert.That(await hooks.GetAllHooksAsync("+ZHOOK")).IsEmpty();
	}

	[Test]
	public async Task ABareDbrefClearsByNumber()
	{
		var hooks = new HookService();
		await hooks.SetHookAsync("+ZHOOK", "AFTER", Recycled, "CMD`AFTER", false, false, false, false);

		var cleared = await hooks.ClearHooksOnAsync([new DBRef(4242)]);

		await Assert.That(cleared).IsEquivalentTo([new ClearedHook("+ZHOOK", "AFTER")]);
	}
}
