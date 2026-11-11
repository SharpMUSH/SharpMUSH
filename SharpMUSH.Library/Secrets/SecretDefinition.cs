namespace SharpMUSH.Library.Secrets;

/// <summary>
/// A secret the server or a plugin reads. Registered as a singleton (a plugin does it from its
/// <c>IServiceRegistrar</c>), so <c>@secret</c> and the portal can say what it is for and that it is missing.
/// </summary>
/// <param name="Name">Its name, as <see cref="SecretNames.IsValid"/> allows.</param>
/// <param name="Description">What it is for, in a sentence.</param>
/// <param name="ManagedBy">A permission that may set it besides <c>config.admin</c>, such as <c>discord.admin</c>.</param>
public sealed record SecretDefinition(string Name, string Description, string? ManagedBy = null)
{
	/// <summary>The SQL server's password, for <c>sql()</c> and <c>@sql</c>.</summary>
	public static readonly SecretDefinition SqlPassword = new("sql.password", "The password of sql_username on the SQL server.");

	/// <summary>The SMTP server's password, for <c>@email</c>.</summary>
	public static readonly SecretDefinition EmailPassword = new("email.password", "The password of email_username on the SMTP server.");
}
