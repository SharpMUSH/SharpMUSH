using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// Inside <see cref="SideEffectFreeEvaluation"/>, every side-effect function answers <c>#-1 FUNCTION DISABLED</c>,
/// in the expression itself and in anything it reaches through <c>u()</c>; reads still work, and the refusal ends with
/// the scope.
/// </summary>
public class SideEffectFreeEvaluationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private async Task<string> Command(string command)
		=> (await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command))).Message.ToPlainText().Trim();

	private async Task<string> Evaluate(string expression)
		=> (await Parser.FunctionParse(MarkupText.Plain(expression)))!.Message.ToPlainText();

	private async Task<string> Thing(string prefix)
		=> await Command($"@create {TestIsolationHelpers.GenerateUniqueName(prefix)}");

	[Test]
	public async Task ASideEffectFunctionIsRefused()
	{
		var thing = await Thing("NoFxDirect");

		string result;
		using (SideEffectFreeEvaluation.Enter())
			result = await Evaluate($"[attrib_set({thing}/MARK,set)]");

		await Assert.That(result).IsEqualTo("#-1 FUNCTION DISABLED");
		await Assert.That(await Evaluate($"[hasattr({thing},MARK)]")).IsEqualTo("0");
	}

	[Test]
	public async Task ASideEffectFunctionReachedThroughUIsRefused()
	{
		var thing = await Thing("NoFxNested");
		await Command($"&FN {thing}=[attrib_set(%!/MARK,set)]done");

		string result;
		using (SideEffectFreeEvaluation.Enter())
			result = await Evaluate($"[u({thing}/FN)]");

		await Assert.That(result).IsEqualTo("#-1 FUNCTION DISABLEDdone");
		await Assert.That(await Evaluate($"[hasattr({thing},MARK)]")).IsEqualTo("0");
	}

	[Test]
	[Arguments("sql(SELECT 1)")]
	[Arguments("mapsql(me/NOSUCH,SELECT 1)")]
	public async Task AnSqlQueryIsRefused(string call)
	{
		string result;
		using (SideEffectFreeEvaluation.Enter())
			result = await Evaluate($"[{call}]");

		await Assert.That(result).IsEqualTo("#-1 FUNCTION DISABLED").Because("a query can write to the database it reaches");
	}

	[Test]
	public async Task ReadsStillWork()
	{
		var thing = await Thing("NoFxRead");
		await Command($"&DATA {thing}=stone");
		await Command($"&FN {thing}=[ucstr(v(DATA))]");

		string result;
		using (SideEffectFreeEvaluation.Enter())
			result = await Evaluate($"[u({thing}/FN)]");

		await Assert.That(result).IsEqualTo("STONE");
	}

	[Test]
	public async Task SideEffectsReturnWhenTheScopeEnds()
	{
		var thing = await Thing("NoFxAfter");
		using (SideEffectFreeEvaluation.Enter())
			await Evaluate("[add(1,1)]");

		await Evaluate($"[attrib_set({thing}/MARK,set)]");

		await Assert.That(await Evaluate($"[get({thing}/MARK)]")).IsEqualTo("set");
	}
}
