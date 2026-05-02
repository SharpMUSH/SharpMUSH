namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A union of a helpfile index dictionary and an error.
/// Replaces OneOf&lt;Dictionary&lt;string, string&gt;, Error&lt;string&gt;&gt; in Helpfiles.cs.
/// </summary>
public union HelpIndex(System.Collections.Generic.Dictionary<string, string>, SharpError)
{
	public bool IsIndex => Value is System.Collections.Generic.Dictionary<string, string>;
	public bool IsError => Value is SharpError;

	public System.Collections.Generic.Dictionary<string, string> AsIndex => (System.Collections.Generic.Dictionary<string, string>)Value!;
	public SharpError AsError => (SharpError)Value!;
}

/// <summary>
/// A union of a helpfile positions index dictionary and an error.
/// Replaces OneOf&lt;Dictionary&lt;string, (long Start, long End)&gt;, Error&lt;string&gt;&gt; in Helpfiles.cs.
/// </summary>
public union HelpIndexPositions(System.Collections.Generic.Dictionary<string, (long Start, long End)>, SharpError)
{
	public bool IsIndex => Value is System.Collections.Generic.Dictionary<string, (long Start, long End)>;
	public bool IsError => Value is SharpError;

	public System.Collections.Generic.Dictionary<string, (long Start, long End)> AsIndex => (System.Collections.Generic.Dictionary<string, (long Start, long End)>)Value!;
	public SharpError AsError => (SharpError)Value!;
}
