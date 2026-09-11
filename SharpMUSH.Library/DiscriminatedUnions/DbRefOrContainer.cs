using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A container given by reference, or already loaded.
/// </summary>
public partial union DbRefOrContainer(DBRef, AnySharpContainer);
