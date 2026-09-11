using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// An object named by its <see cref="DBRef"/>, or by a name.
/// </summary>
public union DbRefOrName(DBRef, string);
