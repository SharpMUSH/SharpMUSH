using System.Text;
using System.Text.Json;
using SharpMUSH.Library.Applications;

namespace SharpMUSH.Tests.Applications;

public class UiDefinitionTests
{
	public const string DefinitionJson = """
		{"version":1,"id":"graph-proof","entry":"index.html","configVersion":1,
		 "configSchema":{"type":"object","additionalProperties":false,"required":["rooms"],"properties":{
		 "rooms":{"type":"array","minItems":1,"maxItems":100,"items":{"type":"integer","minimum":0}}}}}
		""";

	[Test]
	public async Task InvalidVersionTypesBoundsAndUnknownPropertiesAreRejected()
	{
		var definition = UiDefinition.Parse(Encoding.UTF8.GetBytes(DefinitionJson));
		await Assert.That(definition.ValidateConfig(1, JsonSerializer.SerializeToElement(new { rooms = new[] { 1, 2 } }))).IsNull();
		await Assert.That(definition.ValidateConfig(2, JsonSerializer.SerializeToElement(new { rooms = new[] { 1 } }))).IsNotNull();
		await Assert.That(definition.ValidateConfig(1, JsonSerializer.SerializeToElement(new { rooms = new[] { -1 } }))).IsNotNull();
		await Assert.That(definition.ValidateConfig(1, JsonSerializer.SerializeToElement(new { rooms = new[] { "1" } }))).IsNotNull();
		await Assert.That(definition.ValidateConfig(1, JsonSerializer.SerializeToElement(new { rooms = new int[101] }))).IsNotNull();
		await Assert.That(definition.ValidateConfig(1, JsonSerializer.SerializeToElement(new { rooms = new[] { 1 }, secret = true }))).IsNotNull();
	}

	[Test]
	public async Task UnsupportedSchemaDoesNotSilentlyPass()
	{
		var invalid = DefinitionJson.Replace("\"type\":\"integer\"", "\"type\":\"integer\",\"pattern\":\".*\"");
		await Assert.That(() => UiDefinition.Parse(Encoding.UTF8.GetBytes(invalid))).Throws<FormatException>();
	}
}
