using System.Reflection;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Configuration;

/// <summary>
/// The <c>Compatibility</c> options read as feature flags, from their <see cref="CompatibilityFlagAttribute"/>.
/// </summary>
public static class CompatibilityFlags
{
	/// <summary>One compatibility option: its property, its configuration metadata and its flag metadata.</summary>
	public sealed record Flag(PropertyInfo Property, SharpConfigAttribute Config, CompatibilityFlagAttribute Compatibility)
	{
		/// <summary>The option's name, as <c>@config</c> takes it.</summary>
		public string Name => Config.Name;
	}

	/// <summary>Every option of <see cref="CompatibilityOptions"/> that carries both attributes, in declaration order.</summary>
	public static IReadOnlyList<Flag> All { get; } = [.. typeof(CompatibilityOptions)
		.GetProperties(BindingFlags.Public | BindingFlags.Instance)
		.OrderBy(property => property.MetadataToken)
		.Select(property => (property,
			config: property.GetCustomAttribute<SharpConfigAttribute>(),
			flag: property.GetCustomAttribute<CompatibilityFlagAttribute>()))
		.Where(found => found.config is not null && found.flag is not null)
		.Select(found => new Flag(found.property, found.config!, found.flag!))];

	/// <summary>The flag named <paramref name="name"/>, or null.</summary>
	public static Flag? Named(string name)
		=> All.FirstOrDefault(flag => flag.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// The options a <paramref name="source"/> database import sets: every flag marked
	/// <see cref="CompatibilityFlagAttribute.SetBy"/> that import, a boolean as true and <c>dialect</c> as
	/// <paramref name="dialect"/> (left out when none is given).
	/// </summary>
	public static Dictionary<string, object> ImportOptions(CompatibilityServer source, string? dialect = null)
	{
		var options = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
		foreach (var flag in All.Where(flag => flag.Compatibility.SetBy.HasFlag(source)))
		{
			if (flag.Property.PropertyType == typeof(bool))
			{
				options[flag.Name] = true;
			}
			else if (dialect is not null)
			{
				options[flag.Name] = dialect;
			}
		}

		return options;
	}

	/// <summary>The servers in <paramref name="servers"/>, in a fixed order: <c>PennMUSH, TinyMUX</c>.</summary>
	public static string Names(CompatibilityServer servers)
		=> string.Join(", ", Enum.GetValues<CompatibilityServer>()
			.Where(server => server != CompatibilityServer.None && servers.HasFlag(server))
			.Select(server => server.ToString()));
}
