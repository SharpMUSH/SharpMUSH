using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Configuration.Generated;

namespace SharpMUSH.Library.API;

public static class OptionHelper
{
	public static ConfigurationResponse OptionsToConfigurationResponse(SharpMUSHOptions options)
		=> new()
		{
			Configuration = options,
			Metadata = ConfigMetadata.PropertyMetadata.ToDictionary()
		};
}
