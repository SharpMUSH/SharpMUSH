using System.Text.Json;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class SoftcodeDiagnosticsFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private async Task<AnySharpObject> God()
		=> (await Factory.Services.GetRequiredService<IMediator>()
			.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>();

	[Test]
	public async Task SyntaxDiagnosticsReadsStoredCodeWithoutExecutingIt()
	{
		var god = await God();
		await Factory.Services.GetRequiredService<IAttributeService>()
			.SetAttributeAsync(god, god, "EDITORTEST", MarkupText.Plain("[attrib_set(#1/EDITORTESTEXECUTED,yes)]["));

		var result = await Factory.FunctionParser.EvaluateAsync(
			MarkupText.Plain("[syntaxdiagnostics(#1/EDITORTEST,function)]"));
		var diagnostics = JsonDocument.Parse(result.ToPlainText()).RootElement;

		await Assert.That(diagnostics.GetArrayLength()).IsGreaterThan(0);
		await Assert.That(diagnostics[0].GetProperty("severity").GetString()).IsEqualTo("error");
		var executed = await Factory.Services.GetRequiredService<IAttributeService>()
			.GetAttributeAsync(god, god, "EDITORTESTEXECUTED", IAttributeService.AttributeMode.Read, parent: false);
		await Assert.That(executed.IsAttribute).IsFalse()
			.Because("reading diagnostics must not execute the stored attrib_set() call");
	}

	[Test]
	public async Task SyntaxDiagnosticsReturnsAnEmptyArrayForValidCommandCode()
	{
		var god = await God();
		await Factory.Services.GetRequiredService<IAttributeService>()
			.SetAttributeAsync(god, god, "EDITORTESTVALID", MarkupText.Plain("think [add(1,2)]"));

		var result = await Factory.FunctionParser.EvaluateAsync(
			MarkupText.Plain("[syntaxdiagnostics(#1/EDITORTESTVALID,command)]"));

		await Assert.That(result.ToPlainText()).IsEqualTo("[]");
	}
}
