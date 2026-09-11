using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// An object given by reference, or already loaded.
/// </summary>
public partial union DbRefOrObject(DBRef, AnySharpObject);
