using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using System.Reflection;

namespace SharpMUSH.Library.API;

public static class OptionHelper
{
	private static readonly PropertyInfo[] properties = typeof(SharpMUSHOptions).GetProperties();
	private static readonly Lazy<IEnumerable<KeyValuePair<string, SharpConfigAttribute>>> KV = new(OptionToKV);

	public static ConfigurationResponse OptionsToConfigurationResponse(SharpMUSHOptions options)
		=> new()
		{
			Configuration = options,
			Metadata = KV.Value.ToDictionary()
		};

	private static IEnumerable<KeyValuePair<string, SharpConfigAttribute>> OptionToKV()
	{
		foreach (var category in properties)
		{
			foreach (var property in category.PropertyType.GetProperties())
			{
				var customAttribute = property.GetCustomAttribute<SharpConfigAttribute>()!;
				yield return new KeyValuePair<string, SharpConfigAttribute>(customAttribute.Name, customAttribute);
			}
		}
	}
}
