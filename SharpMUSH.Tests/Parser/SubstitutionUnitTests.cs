using NSubstitute.ReceivedExtensions;

namespace SharpMUSH.Tests.Parser;

public class SubstitutionUnitTests : BaseUnitTest
{
	[Test]
	[Arguments("think %t", "\t")]
	[Arguments("think %#", "#1")]
	[Arguments("think %!", "#1")]
	[Arguments("think %@", "#1")]
	[Arguments("think [strcat(%!,5)]", "#15")]
	[Arguments("think %!5", "#15")]
	// [Arguments("strcat(%q0)")]
	// [Arguments("strcat(%q<test>)")]
	// [Arguments("%s")]
	// [Arguments("%q<test>")]
	// [Arguments("%q<0>")]
	// [Arguments("strcat(%q<0>)")]
	// [Arguments("strcat(%q<word()>)")]
	// [Arguments("strcat(%q<[strcat(1,0)]>)")]
	// [Arguments("strcat(%q<%s>)")]
	// [Arguments("strcat(%q<%q0>)")]
	// [Arguments("strcat(%q<Word %q<5> [strcat(%q<6six>)]>)")]
	public async Task Test(string str, string? expected = null)
	{
		Console.WriteLine("Testing: {0}", str);
		var parser = TestParser();
		await parser.CommandParse("1", MModule.single(str));

		if (expected != null)
		{
			await parser.NotifyService
				.Received(Quantity.Exactly(1))
				.Notify(parser.CurrentState.Executor!.Value, expected);
		}
	}
}