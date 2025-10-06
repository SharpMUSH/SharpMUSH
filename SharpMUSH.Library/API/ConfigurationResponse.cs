using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Library.API;

public class ConfigurationResponse
{
	public SharpMUSHOptions Configuration { get; set; } = null!;
	public Dictionary<string, SharpConfigAttribute> Metadata { get; set; } = [];
}