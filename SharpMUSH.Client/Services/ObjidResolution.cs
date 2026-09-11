using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The objid a character name resolved to, <see cref="NotFound"/>, or <see cref="Error"/> when the
/// directory could not be read.
/// </summary>
public partial union ObjidResolution(string, NotFound, Error);
