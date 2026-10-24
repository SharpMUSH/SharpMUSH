using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary><c>standin(&lt;text&gt;, &lt;stand-in&gt;)</c>: the text, sent as the stand-in to a client that cannot show it.</summary>
public class StandInFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	private async Task<MString> Eval(string code) => await Parser.EvaluateAsync(MarkupText.Plain(code));

	[Test]
	public async Task StandIn_IsTheTextForAReaderWhoShowsIt()
	{
		var result = await Eval("standin(chr(128204),P) Rules");

		await Assert.That(result.ToPlainText()).IsEqualTo("\U0001F4CC Rules");
		await Assert.That(AsciiFold.Default.Fold(result).ToPlainText()).IsEqualTo("P Rules");
	}

	[Test]
	public async Task StandIn_ReplacesTheWholeText()
		=> await Assert.That(AsciiFold.Default.Fold(await Eval("standin([chr(10003)] done,\\[x\\] done)")).ToPlainText()).IsEqualTo("[x] done");

	[Test]
	public async Task StandIn_RefusesAStandInThatIsNotPlainAscii()
		=> await Assert.That((await Eval("standin(a,[chr(233)])")).ToPlainText()).IsEqualTo("#-1 INVALID ARGUMENT");
}
