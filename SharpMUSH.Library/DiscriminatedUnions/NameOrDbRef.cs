using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// An object named by a name, or by its <see cref="DBRef"/>.
/// </summary>
public union NameOrDbRef(string, DBRef);
