namespace SharpMUSH.Tests.Functions;

/// <summary><c>sandbox()</c>: an expression evaluated with some functions refused.</summary>
public class SandboxFunctionTests : ServerTestBase
{
	[Test]
	[Arguments("sandbox([add(1,1)] [sub(1,1)],add)", "#-1 PERMISSION DENIED: CANNOT USE ADD 0")]
	[Arguments("sandbox([add(1,1)] [sub(1,1)],ADD sub)", "#-1 PERMISSION DENIED: CANNOT USE ADD #-1 PERMISSION DENIED: CANNOT USE SUB")]
	[Arguments("sandbox([add(1,1)] [sub(1,1)],add,1)", "#-1 FUNCTION (ADD) NOT FOUND 0")]
	[Arguments("sandbox([add(1,1)] [sub(1,1)],add,0,0,1)", "2 #-1 PERMISSION DENIED: CANNOT USE SUB")]
	[Arguments("sandbox([add(1,1)] [sub(1,1)],)", "2 0")]
	[Arguments("sandbox([add(1,1)] [sub(1,1)],[lcstr(ADD)])", "#-1 PERMISSION DENIED: CANNOT USE ADD 0")]
	[Arguments("[sandbox(x,add)][add(1,1)]", "x2")]
	[Arguments("sandbox(map(#apply/abs,-2 -3),abs)", "#-1 PERMISSION DENIED: CANNOT USE ABS #-1 PERMISSION DENIED: CANNOT USE ABS")]
	[Arguments("sandbox(map(#apply/strlen,ab),strlen,1)", "#-1 FUNCTION (STRLEN) NOT FOUND")]
	[Arguments("sandbox(map(#apply/abs,-2),map,0,0,1)", "#-1 PERMISSION DENIED: CANNOT USE ABS")]
	[Arguments("sandbox(sandbox([add(1,1)] [sub(1,1)],sub),add)", "#-1 PERMISSION DENIED: CANNOT USE ADD #-1 PERMISSION DENIED: CANNOT USE SUB")]
	[Arguments("sandbox(sandbox([add(1,1)] [sub(1,1)],add sub,0,0,1),add sandbox,0,0,1)", "2 #-1 PERMISSION DENIED: CANNOT USE SUB")]
	public async Task RefusesTheListedFunctions(string expression, string expected)
		=> await Assert.That(await Eval(expression)).IsEqualTo(expected);

	[Test]
	public async Task ReachesCodeCalledThroughU()
	{
		var attribute = $"SANDBOXU{Guid.NewGuid():N}"[..20];
		await Cmd($"&{attribute} me=[add(2,2)]");
		await Assert.That(await Eval($"sandbox(u(me/{attribute}),add)")).IsEqualTo("#-1 PERMISSION DENIED: CANNOT USE ADD");
		await Assert.That(await Eval($"u(me/{attribute})")).IsEqualTo("4");
	}

	[Test]
	public async Task CoversUserFunctionsOnlyWhenAsked()
	{
		var unique = Guid.NewGuid().ToString("N")[..8];
		var (function, attribute) = ($"sandboxfn{unique}", $"SANDBOXFN{unique}");
		await Cmd($"&{attribute} me=six");
		await Cmd($"@function {function}=me,{attribute}");

		await Assert.That(await Eval($"sandbox({function}(),{function})")).IsEqualTo("six");
		await Assert.That(await Eval($"sandbox({function}(),{function},0,1)"))
			.IsEqualTo($"#-1 PERMISSION DENIED: CANNOT USE {function.ToUpperInvariant()}");
	}
}
