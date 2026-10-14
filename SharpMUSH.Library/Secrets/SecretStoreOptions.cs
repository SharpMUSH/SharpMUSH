namespace SharpMUSH.Library.Secrets;

/// <summary>
/// Where an operator's overrides come from. One per host, so a test host can give its own.
/// </summary>
/// <param name="FilesPath">
/// The directory holding one file per secret, named for it: <c>SHARPMUSH_SECRETS_PATH</c>, or
/// <c>/run/secrets</c>, where Docker and Kubernetes mount secrets.
/// </param>
/// <param name="Environment">Reads an environment variable.</param>
public sealed record SecretStoreOptions(string FilesPath, Func<string, string?> Environment)
{
	/// <summary>The default secrets directory.</summary>
	public const string DefaultFilesPath = "/run/secrets";

	/// <summary>The process's environment and the directory it names.</summary>
	public static SecretStoreOptions FromEnvironment() => new(
		System.Environment.GetEnvironmentVariable("SHARPMUSH_SECRETS_PATH") is { Length: > 0 } path ? path : DefaultFilesPath,
		System.Environment.GetEnvironmentVariable);
}
