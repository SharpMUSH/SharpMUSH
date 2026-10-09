using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// The action list of <c>@assert</c> and <c>@break</c> runs as commands, which evaluate it as they go
/// (PennMUSH <c>do_break</c>). They used to evaluate it once more before running it, so text a player
/// typed into a <c>$</c>-command's <c>%0</c> ran as code with the object's powers.
/// </summary>
public class BreakActionEvaluationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private async ValueTask<string> Eval(string expression)
		=> (await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"think {expression}"))).Message.ToPlainText()?.Trim() ?? "";

	private async ValueTask Cmd(string command)
		=> await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	[Test]
	[Arguments("@assert 0")]
	[Arguments("@break 1")]
	public async ValueTask TheActionList_IsEvaluatedOnce(string guard)
	{
		var verb = TestIsolationHelpers.GenerateUniqueName("brkev").ToLowerInvariant();
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "BrkEval");
		await Cmd($"&CMD`GUARDED {obj}=${verb}g *:{guard}=@set %!=OUT`GUARDED:%0");
		await Cmd($"&CMD`DIRECT {obj}=${verb}d *:@set %!=OUT`DIRECT:%0");

		await Cmd($"{verb}g \\[strlen(abc)\\]");
		await Cmd($"{verb}d \\[strlen(abc)\\]");

		var direct = await Eval($"get({obj}/OUT`DIRECT)");
		await Assert.That(direct).IsEqualTo("[strlen(abc)]").Because("text a player typed is stored, not run");
		await Assert.That(await Eval($"get({obj}/OUT`GUARDED)")).IsEqualTo(direct)
			.Because("a guard's action list gets the same evaluation as the same command run without it");
	}
}
