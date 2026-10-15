using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>An <see cref="ISecretStore"/> that answers one secret, for a service built by hand.</summary>
public static class FixedSecrets
{
	public static ISecretStore With(string name, string value)
	{
		var secrets = Substitute.For<ISecretStore>();
		secrets.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(call => ValueTask.FromResult<FoundResult<string>>(
				call.ArgAt<string>(0) == name ? value : new NotFound()));
		return secrets;
	}
}
