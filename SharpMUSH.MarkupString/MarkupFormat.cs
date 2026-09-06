namespace MarkupString;

/// <summary>
/// An output format a <see cref="MarkupText"/> can be rendered to. Two formats are equal when
/// their <see cref="Name"/>s match ordinally, ignoring case. <see cref="Custom"/> refuses a name
/// that matches one of the six built-ins, so a caller cannot accidentally address a built-in's
/// registry slots under a different <see cref="Encoding"/>.
/// </summary>
public sealed class MarkupFormat : IEquatable<MarkupFormat>
{
	public string Name { get; }
	public TextEncoding Encoding { get; }

	private MarkupFormat(string name, TextEncoding encoding)
	{
		Name = name;
		Encoding = encoding;
	}

	public static readonly MarkupFormat Plain = new("plain", TextEncoding.StripControls);
	public static readonly MarkupFormat Ansi = new("ansi", TextEncoding.None);
	public static readonly MarkupFormat Html = new("html", TextEncoding.Html);
	public static readonly MarkupFormat Pueblo = new("pueblo", TextEncoding.Html);
	public static readonly MarkupFormat Mxp = new("mxp", TextEncoding.Html);
	public static readonly MarkupFormat BBCode = new("bbcode", TextEncoding.StripControls);

	/// <summary>Declares a format outside the built-in six.</summary>
	/// <exception cref="ArgumentException"><paramref name="name"/> matches a built-in format's name, ignoring case.</exception>
	public static MarkupFormat Custom(string name, TextEncoding encoding)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		var builtIn = TryParse(name);
		if (builtIn is not null)
		{
			throw new ArgumentException($"'{name}' names a built-in format; use MarkupFormat.{builtIn.Name} or pick a different name.", nameof(name));
		}
		return new MarkupFormat(name, encoding);
	}

	/// <summary>Resolves one of the six built-in formats by name, ignoring case. Unknown names give <see langword="null"/>.</summary>
	public static MarkupFormat? TryParse(string name) => name switch
	{
		null => null,
		_ when name.Equals(Plain.Name, StringComparison.OrdinalIgnoreCase) => Plain,
		_ when name.Equals(Ansi.Name, StringComparison.OrdinalIgnoreCase) => Ansi,
		_ when name.Equals(Html.Name, StringComparison.OrdinalIgnoreCase) => Html,
		_ when name.Equals(Pueblo.Name, StringComparison.OrdinalIgnoreCase) => Pueblo,
		_ when name.Equals(Mxp.Name, StringComparison.OrdinalIgnoreCase) => Mxp,
		_ when name.Equals(BBCode.Name, StringComparison.OrdinalIgnoreCase) => BBCode,
		_ => null,
	};

	public bool Equals(MarkupFormat? other) =>
		other is not null && (ReferenceEquals(this, other) || string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase));

	public override bool Equals(object? obj) => obj is MarkupFormat other && Equals(other);
	public override int GetHashCode() => string.GetHashCode(Name, StringComparison.OrdinalIgnoreCase);
	public override string ToString() => Name;
	public static bool operator ==(MarkupFormat? a, MarkupFormat? b) => a is null ? b is null : a.Equals(b);
	public static bool operator !=(MarkupFormat? a, MarkupFormat? b) => !(a == b);
}
