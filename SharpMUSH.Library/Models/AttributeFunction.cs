using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Models;

/// <summary>
/// An attribute fetched to be run as a function: the object it was read from, its full name, and its
/// code. <see cref="Owner"/> is who the code runs as.
/// </summary>
public readonly record struct AttributeFunction(AnySharpObject Owner, string Name, MString Code);
