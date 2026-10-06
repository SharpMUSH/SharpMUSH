using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Models;

/// <summary>
/// The outcome of signing in with an identifier and a password: the account, the account when the
/// credentials matched but it may not sign in, or <see cref="NotFound"/> when nothing matched.
/// </summary>
public union AccountSignIn(SharpAccount, AccountUnavailable, NotFound);
