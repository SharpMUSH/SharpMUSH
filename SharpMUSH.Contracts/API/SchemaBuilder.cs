using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Generated;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.API;

/// <summary>
/// Builds enhanced configuration schema from SharpMUSHOptions
/// </summary>
public static partial class SchemaBuilder
{
	/// <summary>
	/// Builds the configuration schema. It describes the shape of the options — names, types, groups,
	/// declared defaults — none of which depends on the values currently loaded, so it takes no options
	/// instance. Everything comes from the tables the config generators emit.
	/// </summary>
	public static ConfigurationSchema BuildSchema()
	{
		var schema = new ConfigurationSchema();

		schema.Properties = BuildProperties();
		schema.Categories = BuildCategoriesFromProperties(schema.Properties);

		return schema;
	}

	private static List<CategoryMetadata> BuildCategoriesFromProperties(Dictionary<string, PropertyMetadata> properties)
	{
		var categories = new Dictionary<string, CategoryMetadata>();
		var groups = new Dictionary<string, Dictionary<string, GroupMetadata>>();

		foreach (var prop in properties.Values)
		{
			if (!categories.ContainsKey(prop.Category))
			{
				categories[prop.Category] = new CategoryMetadata
				{
					Name = prop.Category,
					DisplayName = FormatCategoryDisplayName(prop.Category),
					Description = GetCategoryDescription(prop.Category),
					Icon = GetCategoryIcon(prop.Category),
					Order = GetCategoryOrder(prop.Category),
					Groups = new List<GroupMetadata>()
				};
				groups[prop.Category] = new Dictionary<string, GroupMetadata>();
			}

			if (!string.IsNullOrEmpty(prop.Group) && !groups[prop.Category].ContainsKey(prop.Group))
			{
				groups[prop.Category][prop.Group] = new GroupMetadata
				{
					Name = prop.Group,
					DisplayName = prop.Group,
					Order = prop.Order // first property's order; ties keep encounter order (stable sort below)
				};
			}
		}

		foreach (var category in categories.Values)
		{
			if (groups.TryGetValue(category.Name, out var categoryGroups))
			{
				category.Groups = categoryGroups.Values.OrderBy(g => g.Order).ToList();
			}
		}

		return categories.Values.OrderBy(c => c.Order).ToList();
	}

	private static Dictionary<string, PropertyMetadata> BuildProperties()
	{
		var properties = new Dictionary<string, PropertyMetadata>();

		// Declaration order, not dictionary order: BuildCategoriesFromProperties records a group the first
		// time it sees one, and orders tied groups by that encounter.
		foreach (var propertyName in ConfigMetadata.PropertyNames)
		{
			var attr = ConfigMetadata.PropertyMetadata[propertyName];
			var type = ConfigAccessor.GetPropertyType(propertyName)!;
			var category = attr.Category;
			var path = $"{category}.{propertyName}";

			properties[path] = new PropertyMetadata
			{
				Name = propertyName,
				DisplayName = FormatPropertyDisplayName(attr.Name),
				Description = attr.Description,
				Category = category,
				Group = attr.Group,
				Order = attr.Order,
				Type = GetPropertyTypeName(type),
				Component = InferComponentType(type),
				DefaultValue = ConfigAccessor.GetDeclaredDefault(propertyName),
				Min = attr.Min,
				Max = attr.Max,
				Pattern = attr.ValidationPattern,
				Required = !IsNullable(type),
				Tooltip = attr.Tooltip,
				ReadOnly = false,
				Path = path
			};
		}

		return properties;
	}

	private static string FormatPropertyDisplayName(string name)
	{
		if (string.IsNullOrEmpty(name)) return name;

		if (name.Contains('_'))
		{
			return string.Join(" ", name.Split('_', StringSplitOptions.RemoveEmptyEntries)
				.Select(word => char.ToUpper(word[0]) + word.Substring(1).ToLower()));
		}

		if (name.All(char.IsLower))
		{
			return char.ToUpper(name[0]) + name.Substring(1);
		}

		return PascalCaseSplitRegex().Replace(name, " $1").Trim();
	}

	private static string GetPropertyTypeName(Type type)
	{
		var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

		if (underlyingType == typeof(bool)) return "boolean";
		if (underlyingType == typeof(int) || underlyingType == typeof(uint) ||
				underlyingType == typeof(long) || underlyingType == typeof(ulong) ||
				underlyingType == typeof(short) || underlyingType == typeof(ushort)) return "integer";
		if (underlyingType == typeof(float) || underlyingType == typeof(double) ||
				underlyingType == typeof(decimal)) return "number";
		if (underlyingType == typeof(string)) return "string";
		if (underlyingType.IsEnum) return "enum";
		if (underlyingType == typeof(string[])) return "array";
		if (underlyingType == typeof(Dictionary<string, string[]>)) return "dictionary";

		return "string";
	}

	private static string InferComponentType(Type type)
	{
		var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

		if (underlyingType == typeof(bool)) return "switch";
		if (underlyingType == typeof(int) || underlyingType == typeof(uint) ||
				underlyingType == typeof(long) || underlyingType == typeof(ulong) ||
				underlyingType == typeof(short) || underlyingType == typeof(ushort) ||
				underlyingType == typeof(float) || underlyingType == typeof(double) ||
				underlyingType == typeof(decimal)) return "numeric";
		if (underlyingType.IsEnum) return "select";
		if (underlyingType == typeof(string[])) return "stringlist";
		if (underlyingType == typeof(Dictionary<string, string[]>)) return "dictionary";

		return "text";
	}

	private static bool IsNullable(Type type)
	{
		return !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
	}

	private static string FormatCategoryDisplayName(string categoryName)
	{
		if (categoryName.EndsWith("Options"))
		{
			categoryName = categoryName.Substring(0, categoryName.Length - 7);
		}

		return PascalCaseSplitRegex().Replace(categoryName, " $1").Trim();
	}

	private static string? GetCategoryDescription(string categoryName)
	{
		return categoryName switch
		{
			"Net" => "Server connection and network settings",
			"Limit" => "Resource and capacity limits",
			"Chat" => "Chat and communication settings",
			"Database" => "Database configuration",
			"Command" => "Command processing settings",
			"Log" => "Logging and audit settings",
			"Message" => "System messages and prompts",
			_ => null
		};
	}

	private static string? GetCategoryIcon(string categoryName)
	{
		return categoryName switch
		{
			"Net" => "mdi-network",
			"Limit" => "mdi-speedometer",
			"Chat" => "mdi-chat",
			"Database" => "mdi-database",
			"Command" => "mdi-console",
			"Log" => "mdi-file-document",
			"Message" => "mdi-message-text",
			_ => "mdi-cog"
		};
	}

	private static int GetCategoryOrder(string categoryName)
	{
		return categoryName switch
		{
			"Net" => 1,
			"Database" => 2,
			"Limit" => 3,
			"Chat" => 4,
			"Command" => 5,
			"Log" => 6,
			"Message" => 7,
			_ => 99
		};
	}

	[GeneratedRegex("([A-Z])")]
	private static partial Regex PascalCaseSplitRegex();
}
