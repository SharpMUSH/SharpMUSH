using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using System.Reflection;

namespace SharpMUSH.Library.API;

public static class OptionHelper
{
	private static readonly PropertyInfo[] Properties = typeof(SharpMUSHOptions).GetProperties();
	private static readonly Lazy<Dictionary<string, SharpConfigAttribute>> KeyValues = new(OptionToKv);

	public static ConfigurationResponse OptionsToConfigurationResponse(SharpMUSHOptions options)
		=> new()
		{
			Configuration = options,
			Metadata = KeyValues.Value.ToDictionary()
		};

	private static Dictionary<string, SharpConfigAttribute> OptionToKv() 
		=> Properties
			.SelectMany(category => category.PropertyType.GetProperties())
			.Select(property => (property,attribute: property.GetCustomAttribute<SharpConfigAttribute>()!))
			.Select(pa => new KeyValuePair<string, SharpConfigAttribute>(pa.property.Name, pa.attribute))
			.ToDictionary();
}
