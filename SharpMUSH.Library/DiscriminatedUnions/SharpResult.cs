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

	// Backward-compat aliases
	public bool        IsT0 => IsSuccess;
	public bool        IsT1 => IsError;
	public SharpSuccess AsT0 => AsSuccess;
	public SharpError   AsT1 => AsError;
}
