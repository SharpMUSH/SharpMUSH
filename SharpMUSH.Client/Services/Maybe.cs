using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>
/// A value the server returned, or <see cref="None"/> when it had none to give.
/// </summary>
public partial union Maybe<T>(T, None);
