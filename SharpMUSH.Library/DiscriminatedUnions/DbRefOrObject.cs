using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// An object given by reference, or already loaded.
/// </summary>
public union DbRefOrObject(DBRef, AnySharpObject);
