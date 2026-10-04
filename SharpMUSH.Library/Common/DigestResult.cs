using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Common;

/// <summary>
/// A lower-case hex digest, or <see cref="None"/> when the algorithm is not one this server knows.
/// </summary>
public union DigestResult(string, None);
