namespace SharpMUSH.Library.API;

/// <summary>Where the value a secret is read with comes from.</summary>
public enum SecretSource
{
	/// <summary>No value anywhere.</summary>
	Unset,

	/// <summary>The value stored in the world, encrypted with the server's key ring.</summary>
	Stored,

	/// <summary><c>SHARPMUSH_SECRET_&lt;NAME&gt;</c>, which overrides a stored value.</summary>
	Environment,

	/// <summary>A file named for the secret in the secrets directory (<c>/run/secrets</c>), which overrides a stored value.</summary>
	File,

	/// <summary>A stored value the key ring on this host cannot decrypt: the key it was written with is missing or revoked.</summary>
	Unreadable
}

/// <summary>Who last set a stored secret: a character's objid and name, or an account's id and name.</summary>
public sealed record SecretAuthor(string Id, string Name);

/// <summary>
/// What may be said about a secret: never its value.
/// </summary>
/// <param name="Name">The secret's name, such as <c>sql.password</c>.</param>
/// <param name="Description">What it is for, when the server or a plugin declared it.</param>
/// <param name="Source">Where its value comes from.</param>
/// <param name="Override">The environment variable or file that sets it, when one does.</param>
/// <param name="Stored">Whether a value is stored in the world, even one an override hides.</param>
/// <param name="SetAt">When the stored value was set.</param>
/// <param name="SetBy">Who set the stored value.</param>
/// <param name="ManagedBy">The permission that may set it besides <c>config.admin</c>, when its declaration names one.</param>
/// <param name="Declared">Whether the server or a plugin reads it.</param>
public sealed record SecretStatus(
	string Name,
	string? Description,
	SecretSource Source,
	string? Override,
	bool Stored,
	DateTimeOffset? SetAt,
	SecretAuthor? SetBy,
	string? ManagedBy,
	bool Declared);

/// <summary><c>PUT api/secrets/{name}</c>: the new value. It is never sent back.</summary>
public sealed record SecretSetRequest(string Value);
