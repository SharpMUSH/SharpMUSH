using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Models;

/// <summary>
/// A sign-in through another service (Discord, Google, ...) linked to an account. It signs in in place of the
/// account's password, or instead of having one.
/// </summary>
/// <param name="AccountId">The account it signs in to.</param>
/// <param name="Provider">The id of the sign-in provider staff set up (<c>discord</c>, <c>google</c>, ...).</param>
/// <param name="Subject">The service's own id for the person: what it answers with every time they sign in.</param>
/// <param name="Name">The name the service last gave for them, to tell links apart on the account page.</param>
/// <param name="CreatedAt">When it was linked.</param>
/// <param name="LastUsedAt">When it last signed in, or null if it never has.</param>
public sealed record AccountExternalLogin(
	string AccountId,
	string Provider,
	string Subject,
	string Name,
	DateTimeOffset CreatedAt,
	DateTimeOffset? LastUsedAt);

/// <summary>What <see cref="IAccountStore.AddAccountExternalLoginAsync"/> did with a new link.</summary>
public enum ExternalLoginAddOutcome
{
	/// <summary>Stored.</summary>
	Added,

	/// <summary>Not stored: the service's identity already signs in to another account.</summary>
	LinkedElsewhere,

	/// <summary>Not stored: the account already has a sign-in through this provider.</summary>
	ProviderAlreadyLinked
}

/// <summary>A new account was not made: the service's identity already signs in to an account.</summary>
public readonly record struct ExternalLoginTaken;

/// <summary>
/// What <see cref="IAccountStore.CreateAccountWithExternalLoginAsync"/> did: the new account; <see cref="ExternalLoginTaken"/>;
/// or why the name or email cannot be used.
/// </summary>
public union ExternalAccountCreation(SharpAccount, ExternalLoginTaken, Error<string>);
