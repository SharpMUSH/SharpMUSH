using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// What a value is being validated for: an object, an attribute entry, a channel, or nothing in
/// particular.
/// </summary>
public union ValidationTarget(AnySharpObject, SharpAttributeEntry, SharpChannel, None);
