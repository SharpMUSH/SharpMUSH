using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Parsing;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services.Imports;

namespace SharpMUSH.Tests.Services;

public class ImportedCallScanTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.Services.GetRequiredService<IMUSHCodeParser>();
	private IConnectionService Connections => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private ITaskScheduler Scheduler => WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>();

	private SoftcodeSyntaxAnalyzer Analyzer => new(Parser.FunctionLibrary,
		WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>());

	[Test]
	[Arguments("add(1,mul(2,3))", "add mul")]
	[Arguments("He smile(s) at [add(1,2)].", "add")]
	[Arguments("x [first(rest(a b c))] after(2)", "first rest")]
	[Arguments("$go *:@pemit %#=[ucstr(%0)]", "ucstr")]
	[Arguments("think strtrunc(x,3)", "")]
	[Arguments("No calls here.", "")]
	public async Task CallsAreTheBracketedOnesAndALeadingOne(string text, string expected)
		=> await Assert.That(string.Join(' ', Analyzer.CalledFunctions(MarkupText.Plain(text)))).IsEqualTo(expected);

	[Test]
	public async Task ReportsEachMissingFunctionOnceWithWhereItIsCalled()
	{
		var id = TestIsolationHelpers.GenerateUniqueName("s")[^5..].Replace("_", "");
		var thing = $"ScanThing{id}";
		foreach (var command in new[]
		{
			$"@create {thing}",
			$"&FN {thing}=zzlead{id}(1,[zzinner{id}()])",
			$"&CMD {thing}=$scan{id} *:@pemit %#=[zzlead{id}(%0)]",
			$"&DESC {thing}=It smile{id}(s).",
			$"&STARTUP {thing}=@function zzmine{id}=me,FN",
			$"&USE {thing}=[zzmine{id}()] [add(1,2)]"
		})
			await Parser.CommandParse(1, Connections, MarkupText.Plain(command));
		await Scheduler.SettleForTestsAsync();

		var result = new ConversionResult();
		await ImportedCallScan.ReportAsync(WebAppFactoryArg.Services.GetRequiredService<IMediator>(), Parser.FunctionLibrary,
			Analyzer, null, result, CancellationToken.None);
		var text = string.Join(" | ", result.Warnings.Select(w => w.Text));

		var lead = result.Warnings.Single(w => w.Text.Contains($"zzlead{id}()"));
		await Assert.That(lead.Kind).IsEqualTo(ImportWarningKind.Softcode);
		await Assert.That(lead.Text).Contains("is called 2 times").And.Contains("/FN").And.Contains("/CMD");
		await Assert.That(text).Contains($"zzinner{id}() is called once");
		await Assert.That(text).DoesNotContain($"smile{id}").Because("a name in prose is not a call");
		await Assert.That(text).DoesNotContain($"zzmine{id}").Because("an @function in the world registers it");
		await Assert.That(text).DoesNotContain(": add()").Because("add() is a built-in function");
	}
}
