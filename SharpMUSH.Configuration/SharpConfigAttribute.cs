using System.Text.Json.Serialization;

namespace SharpMUSH.Configuration;

[AttributeUsage(AttributeTargets.Property)]
public class SharpConfigAttribute : Attribute
{
	[JsonIgnore] public override object TypeId => base.TypeId;

	public required string Name { get; set; }
	public required string Description { get; set; }
	public required string Category { get; set; }
	public string? ValidationPattern { get; set; }

	/// <summary>
	/// UI group name within the category (e.g., "Connection Settings", "Connection Limits")
	/// If null, property appears in a default/ungrouped section
	/// </summary>
	public string? Group { get; set; }

	/// <summary>
	/// Sort order within the group (lower = earlier)
	/// </summary>
	public int Order { get; set; } = 0;

	/// <summary>
	/// Minimum value for numeric types
	/// </summary>
	public object? Min { get; set; }

	/// <summary>
	/// Maximum value for numeric types
	/// </summary>
	public object? Max { get; set; }

	/// <summary>
	/// Additional tooltip text (shown in addition to Description)
	/// </summary>
	public string? Tooltip { get; set; }

	/// <summary>
	/// The option names an object, so <c>config()</c> and <c>@config</c> print its value as a dbref.
	/// PennMUSH decides this by handler: an option registered with <c>cf_dbref</c> is displayed as
	/// <c>#&lt;n&gt;</c> (<c>src/conf.c:1707</c>), and an unset one holds <c>NOTHING</c>, printing
	/// <c>#-1</c>. The property's CLR type cannot carry that — a dbref option and a plain count are
	/// both <c>uint</c> — so it is declared here. See <see cref="ConfigValueDisplay"/>.
	/// </summary>
	public bool Dbref { get; set; }

	/// <summary>
	/// The option is a default-flag list, PennMUSH's <c>cf_flag</c> handler (<c>src/conf.c:709</c>). That
	/// handler appends each configured word after a space, so the stored text of a non-empty list
	/// starts with one; <c>config()</c> and <c>@config</c> print it as stored. See
	/// <see cref="ConfigValueDisplay"/>.
	/// </summary>
	public bool Flag { get; set; }

	/// <summary>
	/// SharpMUSH reads the option from <c>mush.cnf</c> and acts on nothing it says; set when that is so,
	/// to the sentence naming what decides it instead (a deployment setting, or a fixed value). The
	/// configuration page marks the field with it.
	/// </summary>
	public string? Unused { get; set; }
}