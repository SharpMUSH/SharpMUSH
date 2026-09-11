using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Implementation.Common;

/// <summary>
/// A lower-case hex digest, or <see cref="None"/> when the algorithm is not one this server knows.
/// </summary>
public partial union DigestResult(string, None);
