namespace SharpMUSH.Library.Models.Portal.Widgets;

/// <summary>
/// Documents one key of a widget's config model so the layout editor can show admins what that
/// widget accepts. Without it a key is invisible in the UI — the reference table is built from
/// these, not from the property list, so an undocumented key is a deliberate omission rather than
/// an accident of reflection.
/// </summary>
/// <remarks>
/// On a positional record the attribute goes on the property, not the parameter:
/// <c>[property: WidgetConfigKey("LayCfgSpacerHeight")] int Height = 24</c>.
/// </remarks>
/// <param name="descriptionKey">
/// <c>SharedResource</c> key for the one-line description. Use the <c>Lay</c> prefix: these are
/// staff-surface strings and the locale coverage gate exempts that prefix from translation.
/// </param>
[AttributeUsage(AttributeTargets.Property)]
public sealed class WidgetConfigKeyAttribute(string descriptionKey) : Attribute
{
	/// <summary><c>SharedResource</c> key for this key's description.</summary>
	public string DescriptionKey { get; } = descriptionKey;

	/// <summary>
	/// The default a reader should assume, when that is not the CLR default of the property. Wiki
	/// Body's <c>namespace</c> is the example: the record defaults it to null, but null means the
	/// Main namespace, and "main" is the useful thing to show.
	/// </summary>
	public string? Default { get; init; }

	/// <summary>
	/// <c>SharedResource</c> key for the field's label in the layout editor's settings form. Unset,
	/// the form labels the field with its key, spaced out ("newTab" reads "New tab").
	/// </summary>
	public string? LabelKey { get; init; }

	/// <summary>
	/// A sample value the settings form shows, greyed, in the empty field ("house-rules" for a wiki
	/// slug), so an admin sees the shape of a value before typing one. A documented default wins.
	/// </summary>
	public string? Example { get; init; }

	/// <summary>Whether a string key holds several lines (Markdown, say), so the form gives it a text area.</summary>
	public bool Multiline { get; init; }

	/// <summary>
	/// Whether a string key holds a picture's address, so the form offers the media uploads to pick
	/// one from or add one to beside the address field.
	/// </summary>
	public bool Image { get; init; }
}
