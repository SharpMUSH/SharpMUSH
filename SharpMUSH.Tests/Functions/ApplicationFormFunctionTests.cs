using System.Text.Json;

namespace SharpMUSH.Tests.Functions;

public class ApplicationFormFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	[Arguments("0", true)]
	[Arguments("json(string,0)", false)]
	public async Task DeclaredNumberAcceptsZeroButRejectsTextThroughFunctionDispatch(string value, bool expected)
	{
		const string declaration = "json(array,json(object,key,json(string,count),type,json(string,number)))";
		var result = await Factory.FunctionParser.EvaluateAsync(MarkupText.Plain($"appfields({declaration},json(object,count,{value}))"));
		using var response = JsonDocument.Parse(result.ToPlainText());
		await Assert.That(response.RootElement.GetProperty("ok").GetBoolean()).IsEqualTo(expected);
		if (!expected)
			await Assert.That(response.RootElement.GetProperty("errors").GetProperty("count").GetString()).IsEqualTo("count has the wrong JSON type.");
	}
}
