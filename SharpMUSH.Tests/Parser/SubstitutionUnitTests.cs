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
	[Arguments("think [setq(0,foo)][strcat(%q0)]","foo")]
	[Arguments("think [setq(test,foo)][strcat(%q<test>)]", "foo")]
	// [Arguments("think %s")]
	// [Arguments("think %q<test>")]
	// [Arguments("think %q<0>")]
	[Arguments("think [setq(0,foo)][strcat(%q<0>)]","foo")]
	// [Arguments("think strcat(%q<word()>)")]
	// [Arguments("think strcat(%q<[strcat(1,0)]>)")]
	// [Arguments("think strcat(%q<%s>)")]
	[Arguments("think [setq(0,foo,foo,dog)][strcat(%q<%q0>)]","dog")]
	// [Arguments("think strcat(%q<Word %q<5> [strcat(%q<6six>)]>)")]
	public async Task Test(string str, string? expected = null)
	{
		Console.WriteLine("Testing: {0}", str);
		var parser = TestParser();
		await parser.CommandParse("1", MModule.single(str));

		if (expected is not null)
		{
			await parser.NotifyService
				.Received(Quantity.Exactly(1))
				.Notify(parser.CurrentState.Executor!.Value, expected);
		}
	}
}