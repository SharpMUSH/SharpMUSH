using System.Text.Json;
using SharpMUSH.Library.Models.Portal.Applications;

namespace SharpMUSH.Tests.Applications;

public class ApplicationInstanceTests
{
	[Test]
	public async Task RegistryRecord_PreservesDefinitionAndIndependentJsonConfig()
	{
		var json = """
			{"Slug":"east","DisplayName":"East","Kind":0,"SchemaUrl":"","MinimumRole":2,"Order":0,
			 "RenderKind":"Hosted","DefinitionId":"graph-proof","ConfigVersion":1,"Config":{"rooms":[1,2]}}
			""";
		var app = JsonSerializer.Deserialize<RegisteredApplication>(json)!;
		using var output = JsonDocument.Parse(JsonSerializer.Serialize(app));
		await Assert.That(output.RootElement.TryGetProperty("DefinitionId", out var id)).IsTrue();
		await Assert.That(id.GetString()).IsEqualTo("graph-proof");
		await Assert.That(output.RootElement.GetProperty("Config").GetProperty("rooms")[0].GetInt32()).IsEqualTo(1);
	}
}
