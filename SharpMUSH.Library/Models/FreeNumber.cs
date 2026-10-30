namespace SharpMUSH.Library.Models;

/// <summary>
/// A dbref number no object holds, and when the allocator may hand it out unasked: null for never. Anyone allowed to
/// pick a number may ask for it at any time.
/// </summary>
public sealed record FreeNumber(int Number, DateTimeOffset? ReusableAt);

/// <summary>An attribute that names <paramref name="Number"/> as a bare <c>#N</c>: <paramref name="Holder"/>'s <paramref name="Attribute"/>.</summary>
public sealed record NumberReference(int Holder, string Attribute, int Number);
