using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// RhostMUSH's ueval() and streval(): code evaluated under a privilege ceiling, which is a role. Under it the caller
/// keeps only what the roles at or below that role's priority grant.
/// </summary>
public class RhostCeilingFunctionTests : ServerTestBase
{
	private async Task<TestIsolationHelpers.TestPlayer> MortalAsync()
		=> await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "Ceiling");

	[Test]
	public async Task StrEvalLowersToTheNamedRole()
	{
		var mortal = await MortalAsync();
		try
		{
			var check = $"[controls(%#,{mortal.DbRef})]";
			await Assert.That(await Eval($"controls(%#,{mortal.DbRef})")).IsEqualTo("1");
			await Assert.That(await Eval($"streval({check},citizen)")).IsEqualTo("0");
			await Assert.That(await Eval($"streval({check},ci)")).IsEqualTo("0");
			await Assert.That(await Eval($"streval({check},player)")).IsEqualTo("0");
			await Assert.That(await Eval($"streval({check},wizard)")).IsEqualTo("1");
			await Assert.That(await Eval($"streval({check},god)")).IsEqualTo("1");
			await Assert.That(await Eval($"streval({check},nosuchrole)")).IsEqualTo(ErrorMessages.Returns.NoSuchRole);
		}
		finally
		{
			await ConnectionService.Disconnect(mortal.Handle);
		}
	}

	[Test]
	public async Task TheCeilingEndsWithTheCall()
	{
		var mortal = await MortalAsync();
		try
		{
			await Assert.That(await Eval($"[streval(x,citizen)][controls(%#,{mortal.DbRef})]")).IsEqualTo("x1");
		}
		finally
		{
			await ConnectionService.Disconnect(mortal.Handle);
		}
	}

	[Test]
	public async Task ARoleAboveYourOwnChangesNothing()
	{
		var mortal = await MortalAsync();
		try
		{
			await Assert.That(await EvalAs(mortal.DbRef, "streval([controls(%#,#1)],god)")).IsEqualTo("0");
			await Assert.That(await EvalAs(mortal.DbRef, "streval([controls(%#,%#)],wizard)")).IsEqualTo("1");
		}
		finally
		{
			await ConnectionService.Disconnect(mortal.Handle);
		}
	}

	[Test]
	[Arguments("streval(%0 %1,citizen,[add(1,1)],b)", "[add(1,1)] b")]
	[Arguments("streval([add(%0,1)],citizen,2)", "3")]
	public async Task StrEvalLeavesItsArgumentsUnevaluated(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	public async Task UEvalRunsTheAttributeUnderTheCeiling()
	{
		var mortal = await MortalAsync();
		var attribute = TestIsolationHelpers.GenerateUniqueName("CHECK").ToUpperInvariant();
		try
		{
			await Cmd($"&{attribute} me=[controls(%#,{mortal.DbRef})] %0");
			await Assert.That(await Eval($"u({attribute},x)")).IsEqualTo("1 x");
			await Assert.That(await Eval($"ueval({attribute}/wizard,x)")).IsEqualTo("1 x");
			await Assert.That(await Eval($"ueval(me/{attribute}/citizen,x)")).IsEqualTo("0 x");
			await Assert.That(await Eval($"ueval({attribute}/sub,x)")).IsEqualTo($"[controls(#1,{mortal.DbRef})] x");
			await Assert.That(await Eval($"ueval({attribute})")).IsEqualTo("#-1 NO LEVEL SPECIFIED");
			await Assert.That(await Eval($"ueval({attribute}/citzen,x)")).IsEqualTo(ErrorMessages.Returns.NoSuchRole);
		}
		finally
		{
			await Cmd($"&{attribute} me=");
			await ConnectionService.Disconnect(mortal.Handle);
		}
	}

	[Test]
	public async Task BitTypeRanksTheOwnersHighestRole()
	{
		var mortal = await MortalAsync();
		try
		{
			await Assert.That(await Eval($"bittype({mortal.DbRef})")).IsEqualTo("1");
			await Cmd($"@role/assign {mortal.DbRef}=builder");
			await Assert.That(await Eval($"bittype({mortal.DbRef})")).IsEqualTo("3");
			await Cmd($"@role/assign {mortal.DbRef}=royalty");
			await Assert.That(await Eval($"bittype({mortal.DbRef})")).IsEqualTo("4");
			await Cmd($"@role/assign {mortal.DbRef}=wizard");
			await Assert.That(await Eval($"bittype({mortal.DbRef})")).IsEqualTo("5");
		}
		finally
		{
			await ConnectionService.Disconnect(mortal.Handle);
		}
	}
}
