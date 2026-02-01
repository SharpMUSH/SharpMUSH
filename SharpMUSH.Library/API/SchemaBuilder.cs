using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using System.Reflection;

namespace SharpMUSH.Library.API;

/// <summary>
/// Builds enhanced configuration schema from SharpMUSHOptions
/// </summary>
public static class SchemaBuilder
{
	public static ConfigurationSchema BuildSchema(SharpMUSHOptions options)
	{
		var schema = new ConfigurationSchema();
		
		// Build from reflection + attributes
		schema.Properties = BuildPropertiesFromReflection(options);
		schema.Categories = BuildCategoriesFromProperties(schema.Properties);
		
		return schema;
	}
	
	private static List<CategoryMetadata> BuildCategoriesFromProperties(Dictionary<string, PropertyMetadata> properties)
	{
		var categories = new Dictionary<string, CategoryMetadata>();
		var groups = new Dictionary<string, HashSet<GroupMetadata>>();
		
		foreach (var prop in properties.Values)
		{
			// Ensure category exists
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
				groups[prop.Category] = new HashSet<GroupMetadata>(new GroupMetadataComparer());
			}
			
			// Add group if specified
			if (!string.IsNullOrEmpty(prop.Group))
			{
				groups[prop.Category].Add(new GroupMetadata
				{
					Name = prop.Group,
					DisplayName = prop.Group,
					Order = 0 // Will be sorted by first property order
				});
			}
		}
		
		// Convert groups to lists and sort
		foreach (var category in categories.Values)
		{
			if (groups.TryGetValue(category.Name, out var categoryGroups))
			{
				category.Groups = categoryGroups.OrderBy(g => g.Order).ToList();
			}
		}
		
		return categories.Values.OrderBy(c => c.Order).ToList();
	}
	
	private static Dictionary<string, PropertyMetadata> BuildPropertiesFromReflection(SharpMUSHOptions options)
	{
		var properties = new Dictionary<string, PropertyMetadata>();
		var optionsType = typeof(SharpMUSHOptions);
		
		// Iterate through all option category properties (Net, Chat, Limit, etc.)
		foreach (var categoryProp in optionsType.GetProperties())
		{
			var categoryValue = categoryProp.GetValue(options);
			if (categoryValue == null) continue;
			
			var categoryType = categoryProp.PropertyType;
			var categoryName = categoryProp.Name;
			
			// Get the default instance to extract default values
			var defaultInstance = GetDefaultInstance(categoryType);
			
			// Iterate through properties in this category
			foreach (var prop in categoryType.GetProperties())
			{
				var attr = prop.GetCustomAttribute<SharpConfigAttribute>();
				if (attr == null) continue;
				
				var path = $"{categoryName}.{prop.Name}";
				var currentValue = prop.GetValue(categoryValue);
				var defaultValue = defaultInstance != null ? prop.GetValue(defaultInstance) : null;
				
				properties[path] = new PropertyMetadata
				{
					Name = prop.Name,
					DisplayName = attr.Name,
					Description = attr.Description,
					Category = categoryName,
					Group = attr.Group,
					Order = attr.Order,
					Type = GetPropertyTypeName(prop.PropertyType),
					Component = InferComponentType(prop.PropertyType),
					DefaultValue = defaultValue,
					Min = attr.Min,
					Max = attr.Max,
					Pattern = attr.ValidationPattern,
					Required = !IsNullable(prop.PropertyType),
					Tooltip = attr.Tooltip,
					ReadOnly = false,
					Path = path
				};
			}
		}
		
		return properties;
	}
	
	private static object? GetDefaultInstance(Type type)
	{
		try
		{
			// Try to create instance with default constructor
			return Activator.CreateInstance(type);
		}
		catch
		{
			// If no default constructor, return null
			return null;
		}
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
		
		return "text";
	}
	
	private static bool IsNullable(Type type)
	{
		return !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
	}
	
	private static string FormatCategoryDisplayName(string categoryName)
	{
		// Remove "Options" suffix if present
		if (categoryName.EndsWith("Options"))
		{
			categoryName = categoryName.Substring(0, categoryName.Length - 7);
		}
		
		// Add spaces before capital letters
		return System.Text.RegularExpressions.Regex.Replace(categoryName, "([A-Z])", " $1").Trim();
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
	
	private class GroupMetadataComparer : IEqualityComparer<GroupMetadata>
	{
		public bool Equals(GroupMetadata? x, GroupMetadata? y)
		{
			if (x == null || y == null) return false;
			return x.Name == y.Name;
		}
		
		public int GetHashCode(GroupMetadata obj)
		{
			return obj.Name.GetHashCode();
		}
	}
}
