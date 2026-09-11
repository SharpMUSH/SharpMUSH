using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// What a help topic resolved to: one entry, several candidates to choose between, or nothing.
/// </summary>
public partial union HelpResolution(HelpEntry, HelpCandidates, None);
