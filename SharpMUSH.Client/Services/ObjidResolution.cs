using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The objid a character name resolved to, <see cref="NotFound"/>, or the <see cref="ApiFailure"/>
/// that stopped the directory being read.
/// </summary>
public union ObjidResolution(string, NotFound, ApiFailure);

/// <summary>
/// The directory row a character name resolved to, <see cref="NotFound"/>, or the <see cref="ApiFailure"/>
/// that stopped the directory being read.
/// </summary>
public union CharacterLookup(CharacterDirectoryService.CharacterSummary, NotFound, ApiFailure);
