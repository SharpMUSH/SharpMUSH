using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Generated;
using SharpMUSH.Configuration.Options;
using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.API;

/// <summary>
/// Builds enhanced configuration schema from SharpMUSHOptions
/// </summary>
public static partial class SchemaBuilder
{
	/// <summary>
	/// Builds the configuration schema. It describes the shape of the options — names, types, groups,
	/// shipped defaults — none of which depends on the values currently loaded, so it takes no options
	/// instance. Everything but the defaults comes from the tables the config generators emit; the
	/// defaults come from <see cref="SharpMUSHOptions.Default"/>, the one place a shipped default is
	/// written down.
	/// </summary>
	public static ConfigurationSchema BuildSchema()
	{
		// Kept as a list as well as a dictionary: category and group ordering breaks ties by first-seen
		// property, and Dictionary<,> enumeration order is an implementation detail, not a guarantee.
		var ordered = BuildProperties();

		return new ConfigurationSchema
		{
			Properties = ordered.ToDictionary(property => property.Path),
			Categories = BuildCategoriesFromProperties(ordered)
		};
	}

	private static List<CategoryMetadata> BuildCategoriesFromProperties(List<PropertyMetadata> properties)
	{
		var categories = new Dictionary<string, CategoryMetadata>();
		var groups = new Dictionary<string, Dictionary<string, GroupMetadata>>();

		foreach (var prop in properties)
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

	/// <summary>Every configured property, in the order its category and then its record declares it.</summary>
	private static List<PropertyMetadata> BuildProperties()
	{
		var properties = new List<PropertyMetadata>(ConfigMetadata.PropertyNames.Length);
		var defaults = SharpMUSHOptions.Default();

		foreach (var propertyName in ConfigMetadata.PropertyNames)
		{
			var attr = ConfigMetadata.PropertyMetadata[propertyName];
			var type = ConfigAccessor.GetPropertyType(propertyName)!;
			var category = attr.Category;
			var path = $"{category}.{propertyName}";

			properties.Add(new PropertyMetadata
			{
				Name = propertyName,
				DisplayName = FormatPropertyDisplayName(attr.Name),
				Description = attr.Description,
				Category = category,
				Group = attr.Group,
				Order = attr.Order,
				Type = GetPropertyTypeName(type),
				Component = attr.Image ? "image" : InferComponentType(type),
				DefaultValue = ConfigAccessor.GetValue(defaults, propertyName),
				Min = attr.Min,
				Max = attr.Max,
				Pattern = attr.ValidationPattern,
				Required = !IsNullable(type),
				Tooltip = attr.Tooltip,
				ReadOnly = false,
				Path = path,
				Unused = attr.Unused
			});
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

	/// <summary>The kinds of value an option holds, which decide both its schema type and its editor.</summary>
	private enum ValueShape { Boolean, Integer, Number, Enum, StringList, Dictionary, Text }

	private static readonly FrozenDictionary<Type, ValueShape> ShapesByType = new Dictionary<Type, ValueShape>
	{
		[typeof(bool)] = ValueShape.Boolean,
		[typeof(int)] = ValueShape.Integer,
		[typeof(uint)] = ValueShape.Integer,
		[typeof(long)] = ValueShape.Integer,
		[typeof(ulong)] = ValueShape.Integer,
		[typeof(short)] = ValueShape.Integer,
		[typeof(ushort)] = ValueShape.Integer,
		[typeof(float)] = ValueShape.Number,
		[typeof(double)] = ValueShape.Number,
		[typeof(decimal)] = ValueShape.Number,
		[typeof(string[])] = ValueShape.StringList,
		[typeof(Dictionary<string, string[]>)] = ValueShape.Dictionary
	}.ToFrozenDictionary();

	private static ValueShape ShapeOf(Type type)
	{
		var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
		return ShapesByType.TryGetValue(underlyingType, out var shape) ? shape
			: underlyingType.IsEnum ? ValueShape.Enum
			: ValueShape.Text;
	}

	private static string GetPropertyTypeName(Type type) => ShapeOf(type) switch
	{
		ValueShape.Boolean => "boolean",
		ValueShape.Integer => "integer",
		ValueShape.Number => "number",
		ValueShape.Enum => "enum",
		ValueShape.StringList => "array",
		ValueShape.Dictionary => "dictionary",
		_ => "string"
	};

	private static string InferComponentType(Type type) => ShapeOf(type) switch
	{
		ValueShape.Boolean => "switch",
		ValueShape.Integer or ValueShape.Number => "numeric",
		ValueShape.Enum => "select",
		ValueShape.StringList => "stringlist",
		ValueShape.Dictionary => "dictionary",
		_ => "text"
	};

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
			_ => 99
		};
	}

	[GeneratedRegex("([A-Z])")]
	private static partial Regex PascalCaseSplitRegex();
}
