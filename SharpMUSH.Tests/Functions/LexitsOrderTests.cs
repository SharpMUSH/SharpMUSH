namespace SharpMUSH.Tests.Functions;

/// <summary>
/// <c>lexits()</c> lists a room's exits newest first, as PennMUSH's exit list does: the World map builder checks a
/// join's way back as the first exit of the room it joined.
/// </summary>
public class LexitsOrderTests : ServerTestBase
{
	[Test]
	public async Task TheWayBackAnOpenMadeIsTheFirstExitOfItsRoom()
	{
		var from = (await Cmd($"@dig {TestIsolationHelpers.GenerateUniqueName("JoinFrom")}")).Split(':')[0];
		var to = (await Cmd($"@dig {TestIsolationHelpers.GenerateUniqueName("JoinTo")}")).Split(':')[0];
		var elsewhere = (await Cmd($"@dig {TestIsolationHelpers.GenerateUniqueName("Elsewhere")}")).Split(':')[0];
		await Cmd($"@open Old way={elsewhere},,{to}");

		await Cmd($"@open Way={to},Back,{from}");

		await Assert.That(await Eval($"[name(first(lexits({to})))] [num(loc(first(lexits({to}))))]")).IsEqualTo($"Back {from}");
		await Assert.That(await Eval($"name(last(lexits({to})))")).IsEqualTo("Old way");
	}
}
