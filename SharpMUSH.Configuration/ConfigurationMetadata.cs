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

	public static object? GetDefaultValue(string section, string propertyName)
	{
		var key = $"{section}.{propertyName}";
		return PropertyMetadata.TryGetValue(key, out var info) ? info.DefaultValue : null;
	}

	public static PennMUSHOptions CreateDefaultOptions()
	{
		var optionsType = typeof(PennMUSHOptions);
		var options = Activator.CreateInstance(optionsType);
		
		foreach (var sectionProperty in optionsType.GetProperties())
		{
			var sectionInstance = CreateSectionWithDefaults(sectionProperty.PropertyType, sectionProperty.Name);
			sectionProperty.SetValue(options, sectionInstance);
		}
		
		return (PennMUSHOptions)options!;
	}

	private static object CreateSectionWithDefaults(Type sectionType, string sectionName)
	{
		var parameters = new List<object?>();
		var constructor = sectionType.GetConstructors().First();
		
		foreach (var parameter in constructor.GetParameters())
		{
			var parameterType = parameter.ParameterType;
			var defaultValue = GetDefaultValue(sectionName, parameter.Name!);
			
			var convertedValue = ConvertDefaultValue(defaultValue, parameterType);
			parameters.Add(convertedValue);
		}
		
		return Activator.CreateInstance(sectionType, parameters.ToArray())!;
	}

	private static object? ConvertDefaultValue(object? defaultValue, Type targetType)
	{
		if (defaultValue == null)
		{
			return GetTypeDefault(targetType);
		}

		// Handle nullable types
		if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>))
		{
			var underlyingType = Nullable.GetUnderlyingType(targetType);
			if (defaultValue == null || (defaultValue is string str && str.Equals("null", StringComparison.OrdinalIgnoreCase)))
				return null;
			return ConvertDefaultValue(defaultValue, underlyingType!);
		}

		// If types match, return as-is
		if (targetType.IsAssignableFrom(defaultValue.GetType()))
		{
			return defaultValue;
		}

		// Handle string conversion
		var stringValue = defaultValue.ToString();
		if (string.IsNullOrEmpty(stringValue))
		{
			return GetTypeDefault(targetType);
		}

		// Handle string arrays
		if (targetType == typeof(string[]))
		{
			return stringValue.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		}

		// Handle basic types
		return targetType.Name switch
		{
			nameof(String) => stringValue,
			nameof(Boolean) => bool.Parse(stringValue),
			nameof(Int32) => int.Parse(stringValue),
			nameof(UInt32) => uint.Parse(stringValue),
			nameof(Char) => stringValue.FirstOrDefault(),
			_ when targetType.IsEnum => Enum.Parse(targetType, stringValue),
			_ => GetTypeDefault(targetType)
		};
	}

	private static object? GetTypeDefault(Type type)
	{
		return type.IsValueType ? Activator.CreateInstance(type) : null;
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
						Nullable: pennConfigAttr.Nullable,
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
	bool Nullable = false,
	object? DefaultValue = null,
	string? ValidationPattern = null,
	string? HelpText = null
)
{
	public bool IsBoolean => PropertyType == typeof(bool);
	public bool IsNumber => PropertyType.IsNumericType();
	public bool IsString => PropertyType == typeof(string) || PropertyType.Name == "String";
	public bool IsArray => PropertyType.IsArray;
}

public static class TypeExtensions
{
	public static bool IsNumericType(this Type type)
	{
		return type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) ||
			   type == typeof(short) || type == typeof(ushort) || type == typeof(byte) || type == typeof(sbyte) ||
			   type == typeof(float) || type == typeof(double) || type == typeof(decimal) ||
			   type == typeof(int?) || type == typeof(uint?) || type == typeof(long?) || type == typeof(ulong?) ||
			   type == typeof(short?) || type == typeof(ushort?) || type == typeof(byte?) || type == typeof(sbyte?) ||
			   type == typeof(float?) || type == typeof(double?) || type == typeof(decimal?);
	}
}