using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A union of MString and plain string, replacing OneOf&lt;MString, string&gt;.
/// Used pervasively in notify/prompt/message service signatures.
/// </summary>
public union SharpMessage(MString, string)
{
	/// <summary>True when the value is empty regardless of which case holds it.</summary>
	public bool IsEmpty => Value switch
	{
		MString ms => MModule.getLength(ms) == 0,
		string s => s.Length == 0,
		_ => true
	};

	/// <summary>Returns the raw string representation (with any ANSI markup intact).</summary>
	public override string ToString() => Value switch
	{
		MString ms => ms.ToString() ?? string.Empty,
		string s => s,
		_ => string.Empty
	};

	/// <summary>Returns the plain-text content, stripping any ANSI markup.</summary>
	public string ToPlainText() => Value switch
	{
		MString ms => ms.ToPlainText(),
		string s => s,
		_ => string.Empty
	};

	/// <summary>Returns the value as an MString, converting a plain string if necessary.</summary>
	public MString ToMString() => Value switch
	{
		MString ms => ms,
		string s when !string.IsNullOrEmpty(s) => MModule.single(s),
		_ => MModule.empty()
	};
}
