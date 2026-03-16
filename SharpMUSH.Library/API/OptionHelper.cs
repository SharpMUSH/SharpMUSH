using SharpMUSH.Configuration.Generated;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Library.API;

public static class OptionHelper
{
	public static ConfigurationResponse OptionsToConfigurationResponse(SharpMUSHOptions options)
		=> new()
		{
			Configuration = options,
			Metadata = ConfigMetadata.PropertyMetadata.ToDictionary(),
			Schema = SchemaBuilder.BuildSchema(options)
		};
}
