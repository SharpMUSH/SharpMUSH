using System.Reflection;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Configuration;

public static class ConfigurationMetadata
{
	private static readonly Lazy<Dictionary<string, ConfigurationPropertyInfo>> _propertyMetadata = 
		new Lazy<Dictionary<string, ConfigurationPropertyInfo>>(LoadMetadata);
	
	private static Dictionary<string, ConfigurationPropertyInfo> PropertyMetadata => _propertyMetadata.Value;

	public static ConfigurationPropertyInfo? GetPropertyInfo(string section, string propertyName)
	{
		var key = $"{section}.{propertyName}";
		return PropertyMetadata.TryGetValue(key, out var info) ? info : null;
	}

	public static IEnumerable<ConfigurationPropertyInfo> GetSectionProperties(string section)
	{
		return PropertyMetadata.Values.Where(p => p.Section == section);
	}

	public static IEnumerable<string> GetAllSections()
	{
		return PropertyMetadata.Values.Select(p => p.Section).Distinct();
	}

	private static Dictionary<string, ConfigurationPropertyInfo> LoadMetadata()
	{
		var metadata = new Dictionary<string, ConfigurationPropertyInfo>();
		var optionsType = typeof(PennMUSHOptions);
		var sectionProperties = optionsType.GetProperties();

		foreach (var sectionProperty in sectionProperties)
		{
			var sectionName = sectionProperty.Name;
			var sectionType = sectionProperty.PropertyType;
			var configProperties = sectionType.GetProperties();

			foreach (var configProperty in configProperties)
			{
				var pennConfigAttr = configProperty.GetCustomAttribute<PennConfigAttribute>();
				if (pennConfigAttr != null)
				{
					var propertyInfo = new ConfigurationPropertyInfo(
						Section: sectionName,
						PropertyName: configProperty.Name,
						ConfigName: pennConfigAttr.Name,
						Description: pennConfigAttr.Description ?? GetDefaultDescription(sectionName, configProperty.Name),
						Category: pennConfigAttr.Category ?? sectionName,
						PropertyType: configProperty.PropertyType,
						IsAdvanced: pennConfigAttr.IsAdvanced,
						DefaultValue: pennConfigAttr.DefaultValue,
						ValidationPattern: pennConfigAttr.ValidationPattern,
						HelpText: pennConfigAttr.HelpText
					);

					var key = $"{sectionName}.{configProperty.Name}";
					metadata[key] = propertyInfo;
				}
			}
		}
		
		return metadata;
	}

	private static string GetDefaultDescription(string section, string propertyName)
	{
		// Fallback descriptions based on common patterns
		return propertyName switch
		{
			var name when name.Contains("Port") => $"Network port for {section.ToLower()} connections",
			var name when name.Contains("Addr") => $"IP address for {section.ToLower()} binding",
			var name when name.Contains("File") => $"File path for {section.ToLower()} data",
			var name when name.Contains("Database") => $"Database file for {section.ToLower()} storage",
			var name when name.Contains("Limit") => $"Maximum limit for {section.ToLower()} operations",
			var name when name.Contains("Cost") => $"Cost setting for {section.ToLower()} operations",
			var name when name.Contains("Enable") || name.Contains("Use") => $"Enable/disable {section.ToLower()} functionality",
			_ => $"{section} {propertyName} setting"
		};
	}
}

public record ConfigurationPropertyInfo(
	string Section,
	string PropertyName,
	string ConfigName,
	string Description,
	string Category,
	Type PropertyType,
	bool IsAdvanced = false,
	string? DefaultValue = null,
	string? ValidationPattern = null,
	string? HelpText = null
)
{
	public bool IsBoolean => PropertyType == typeof(bool);
	public bool IsNumber => PropertyType.IsNumericType();
	public bool IsString => PropertyType == typeof(string) || PropertyType.Name == "String";
	public bool IsArray => PropertyType.IsArray;
	public bool IsNullable => Nullable.GetUnderlyingType(PropertyType) != null;

	public string FriendlyTypeName => PropertyType.Name switch
	{
		"Boolean" => "Yes/No",
		"String" => "Text",
		"Int32" or "UInt32" => "Number",
		"Double" or "Single" or "Decimal" => "Decimal",
		"Char" => "Character",
		var t when t.EndsWith("[]") => "List",
		var t when IsNullable => "Optional " + (Nullable.GetUnderlyingType(PropertyType)?.Name ?? "Value"),
		_ => PropertyType.Name
	};
}

public static class TypeExtensions
{
	public static bool IsNumericType(this Type type)
	{
		return type == typeof(int) || type == typeof(uint) || 
		       type == typeof(long) || type == typeof(ulong) ||
		       type == typeof(short) || type == typeof(ushort) ||
		       type == typeof(byte) || type == typeof(sbyte) ||
		       type == typeof(double) || type == typeof(float) ||
		       type == typeof(decimal);
	}
}