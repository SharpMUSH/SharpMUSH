using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace SharpMUSH.Library.Models.Portal.Widgets;

/// <summary>One documented key of a widget's config, as shown in the layout editor.</summary>
/// <param name="Key">The JSON key, camelCased from the property name.</param>
/// <param name="TypeLabel">JSON type name — <c>string</c>, <c>integer</c>, <c>boolean</c>, <c>list</c>, <c>object</c>.</param>
/// <param name="Default">Rendered default, or null when the key has none.</param>
/// <param name="DescriptionKey"><c>SharedResource</c> key for the description.</param>
/// <param name="Children">Fields of the element type, for a list of objects.</param>
public sealed record WidgetConfigField(
	string Key,
	string TypeLabel,
	string? Default,
	string DescriptionKey,
	IReadOnlyList<WidgetConfigField> Children);

/// <summary>
/// Turns a widget's <see cref="IPortalWidget.ConfigType"/> into the reference table the layout
/// editor shows, so the admin-facing documentation is derived from the config model rather than
/// maintained beside it and left to drift.
/// </summary>
public static class WidgetConfigSchema
{
	private const DynamicallyAccessedMemberTypes ConfigMembers =
		DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors;

	/// <summary>
	/// Describes the keys of a config model, in declaration order. Returns empty for a null type or
	/// for a type whose properties carry no <see cref="WidgetConfigKeyAttribute"/>.
	/// </summary>
	public static IReadOnlyList<WidgetConfigField> Describe(
		[DynamicallyAccessedMembers(ConfigMembers)] Type? configType)
		=> configType is null ? [] : Describe(configType, depth: 0);

	private static IReadOnlyList<WidgetConfigField> Describe(
		[DynamicallyAccessedMembers(ConfigMembers)] Type configType,
		int depth)
	{
		// One level of nesting is all any shipped config needs (a list of link objects), and the
		// bound keeps a self-referencing model from recursing forever.
		if (depth > 1)
		{
			return [];
		}

		var defaults = ConstructorDefaults(configType);

		return configType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Select(p => (Property: p, Attribute: p.GetCustomAttribute<WidgetConfigKeyAttribute>()))
			.Where(x => x.Attribute is not null)
			.Select(x => Field(x.Property, x.Attribute!, defaults, depth))
			.ToList();
	}

	private static WidgetConfigField Field(
		PropertyInfo property,
		WidgetConfigKeyAttribute attribute,
		IReadOnlyDictionary<string, object?> defaults,
		int depth)
	{
		var elementType = ElementTypeOf(property.PropertyType);

		return new WidgetConfigField(
			JsonNamingPolicy.CamelCase.ConvertName(property.Name),
			TypeLabel(property.PropertyType),
			attribute.Default ?? ClrDefault(property.Name, defaults),
			attribute.DescriptionKey,
			elementType is null ? [] : Describe(elementType, depth + 1));
	}

	/// <summary>
	/// Default values from the record's primary constructor. Positional records expose their
	/// defaults there and nowhere else, so this is the only place to read them from.
	/// </summary>
	private static IReadOnlyDictionary<string, object?> ConstructorDefaults(
		[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type configType)
		=> configType.GetConstructors()
			.OrderByDescending(c => c.GetParameters().Length)
			.FirstOrDefault()
			?.GetParameters()
			.Where(p => p.HasDefaultValue && p.Name is not null)
			.ToDictionary(p => p.Name!, p => p.DefaultValue, StringComparer.OrdinalIgnoreCase)
			?? new Dictionary<string, object?>();

	private static string? ClrDefault(string propertyName, IReadOnlyDictionary<string, object?> defaults)
		=> defaults.TryGetValue(propertyName, out var value) && value is not null
			? Literal(value)
			: null;

	private static string Literal(object value) => value switch
	{
		bool b => b ? "true" : "false",
		string s => $"\"{s}\"",
		_ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
	};

	private static string TypeLabel(Type type)
	{
		var t = Nullable.GetUnderlyingType(type) ?? type;

		if (t == typeof(string))
		{
			return "string";
		}

		if (t == typeof(bool))
		{
			return "boolean";
		}

		if (t == typeof(int) || t == typeof(long))
		{
			return "integer";
		}

		if (t == typeof(double) || t == typeof(float) || t == typeof(decimal))
		{
			return "number";
		}

		return typeof(IEnumerable).IsAssignableFrom(t) ? "list" : "object";
	}

	/// <summary>The element type of a list-valued property, or null when the property is not a list.</summary>
	[UnconditionalSuppressMessage("Trimming", "IL2073",
		Justification = "Element types are the widget config records, rooted through IPortalWidget.ConfigType.")]
	[return: DynamicallyAccessedMembers(ConfigMembers)]
	private static Type? ElementTypeOf(Type type)
	{
		if (type == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(type))
		{
			return null;
		}

		return type.IsGenericType ? type.GetGenericArguments().FirstOrDefault() : null;
	}

	/// <summary>
	/// A pasteable JSON skeleton of every documented key, seeded with the documented defaults.
	/// Gives an admin a starting point in the editor rather than a blank box.
	/// </summary>
	public static string TemplateJson(IReadOnlyList<WidgetConfigField> fields)
	{
		if (fields.Count == 0)
		{
			return "{}";
		}

		var builder = new StringBuilder();
		WriteObject(builder, fields, indent: 1);
		return builder.ToString();
	}

	private static void WriteObject(StringBuilder builder, IReadOnlyList<WidgetConfigField> fields, int indent)
	{
		var pad = new string(' ', indent * 2);
		builder.Append("{\n");

		for (var i = 0; i < fields.Count; i++)
		{
			var field = fields[i];
			builder.Append(pad).Append('"').Append(field.Key).Append("\": ");

			if (field.Children.Count > 0)
			{
				builder.Append("[\n").Append(pad).Append("  ");
				WriteObject(builder, field.Children, indent + 2);
				builder.Append('\n').Append(pad).Append(']');
			}
			else
			{
				builder.Append(field.Default ?? Placeholder(field.TypeLabel));
			}

			builder.Append(i < fields.Count - 1 ? ",\n" : "\n");
		}

		builder.Append(new string(' ', (indent - 1) * 2)).Append('}');
	}

	private static string Placeholder(string typeLabel) => typeLabel switch
	{
		"string" => "\"\"",
		"boolean" => "false",
		"integer" or "number" => "0",
		"list" => "[]",
		_ => "{}"
	};
}
