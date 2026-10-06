using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Models;

/// <summary>A character that is already linked to an account other than the one asking for it.</summary>
public sealed record LinkedElsewhere(SharpPlayer Character, SharpAccount Account);

/// <summary>
/// The outcome of an account holder claiming a character by its password: the character, now linked
/// to the account; <see cref="LinkedElsewhere"/> when another account holds it; or
/// <see cref="NotFound"/> when no character matches the name and password.
/// </summary>
public union CharacterClaim(SharpPlayer, LinkedElsewhere, NotFound);

/// <summary>
/// The outcome of staff linking a character to an account: the character, now linked, or
/// <see cref="LinkedElsewhere"/> when another account holds it and has to let it go first.
/// </summary>
public union CharacterLink(SharpPlayer, LinkedElsewhere);
