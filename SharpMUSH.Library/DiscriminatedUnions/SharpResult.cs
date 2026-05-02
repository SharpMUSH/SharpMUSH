namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A union of SharpSuccess and SharpError, replacing OneOf&lt;Success, Error&lt;string&gt;&gt;.
/// Used as a result type for attribute and move operations.
/// </summary>
public union SharpResult(SharpSuccess, SharpError)
{
	public bool        IsSuccess => Value is SharpSuccess;
	public bool        IsError   => Value is SharpError;

	public SharpSuccess AsSuccess => (SharpSuccess)Value!;
	public SharpError   AsError   => (SharpError)Value!;
}
