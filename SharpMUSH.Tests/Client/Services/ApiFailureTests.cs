using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Client.Services;

public class ApiFailureTests
{
	[Test]
	public async Task AnInvalidManifestRefusalCarriesItsIssues()
		=> await Assert.That(ApiFailure.ServerSentence(
				"""{"message":"The manifest is invalid.","issues":["Theme files cannot be used: tidepool: images/shell.png is not the kind of picture its name says."]}"""))
			.IsEqualTo("The manifest is invalid. Theme files cannot be used: tidepool: images/shell.png is not the kind of picture its name says.");

	[Test]
	public async Task AMessageWithoutIssuesIsTheMessage()
		=> await Assert.That(ApiFailure.ServerSentence("""{"message":"The remote is unreachable."}""")).IsEqualTo("The remote is unreachable.");

	[Test]
	public async Task AnErrorStillWins()
		=> await Assert.That(ApiFailure.ServerSentence("""{"error":"Refused.","message":"Other."}""")).IsEqualTo("Refused.");
}
