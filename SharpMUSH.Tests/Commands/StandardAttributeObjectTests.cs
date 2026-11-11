using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// PennMUSH's ATTRIB_SET is <c>CMD_T_EQSPLIT | CMD_T_RS_NOPARSE</c>: an <c>@&lt;attribute&gt;</c> command evaluates
/// the object before its <c>=</c> and stores the value after it as written. Imported code relies on the first half
/// (GMCCG's <c>@startup [v( d.sfp )]=...</c>).
/// </summary>
public class StandardAttributeObjectTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private async ValueTask<string> God(string command)
		=> (await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command))).Message?.ToPlainText() ?? string.Empty;

	private async ValueTask<string> Eval(string expression)
		=> (await Parser.FunctionParse(MarkupText.Plain(expression)))?.Message?.ToPlainText() ?? string.Empty;

	[Test]
	public async ValueTask ObjectIsEvaluatedAndValueIsNot()
	{
		var thing = await God($"@create {TestIsolationHelpers.GenerateUniqueName("SAObj")}");
		var pointer = TestIsolationHelpers.GenerateUniqueName("SAPTR").ToUpperInvariant();
		await God($"&{pointer} {thing}={thing}");

		await God($"@describe [get( {thing}/{pointer} )]=hello [add(1,1)]");
		await Assert.That(await Eval($"[get({thing}/DESCRIBE)]")).IsEqualTo("hello [add(1,1)]");

		await God($"@describe [get( {thing}/{pointer} )]");
		await Assert.That(await Eval($"[hasattr({thing},DESCRIBE)]")).IsEqualTo("0");

		await God($"@describe [setq(0,a=b)]{thing}=split [r(0)]");
		await Assert.That(await Eval($"[get({thing}/DESCRIBE)]")).IsEqualTo("split [r(0)]");

		await God($"@describe {thing}");
		await Assert.That(await Eval($"[hasattr({thing},DESCRIBE)]")).IsEqualTo("0");
	}
}
