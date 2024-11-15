using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Implementation;
using SharpMUSH.IntegrationTests;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Parser;

public class SubstitutionUnitTests : BaseUnitTest
{
	private static ISharpDatabase? database;
	private static Infrastructure? infrastructure;

	[Before(Class)]
	public static async Task OneTimeSetup()
	{
		(database,infrastructure) = await IntegrationServer();
	}
	
	[After(Class)]
	public static async Task OneTimeTeardown()
	{
		await Task.Delay(1);
		infrastructure!.Dispose();
	}

	
	[Test]
	[Arguments("think %t", "\t")]
	[Arguments("think %#", "#1")]
	[Arguments("think %!", "#1")]
	[Arguments("think %@", "#1")]
	[Arguments("think [strcat(%!,5)]", "#15")]
	[Arguments("think %!5", "#15")]
	[Arguments("think [setq(0,foo)][strcat(%q0)]","foo")]
	[Arguments("think [setq(test,foo)][strcat(%q<test>)]", "foo")]
	[Arguments("think %s", "they")]
	[Arguments("think [setq(test,foo)]%q<test>", "foo")]
	[Arguments("think [setq(0,foo)]%q<0>","foo")]
	[Arguments("think [setq(0,foo)][strcat(%q<0>)]","foo")]
	// [Arguments("think [setq(word\\(\\),foo)][strcat(%q<word()>)]","foo")]
	[Arguments("think [setq(10,foo)][strcat(%q<[strcat(1,0)]>)]","foo")]
	// [Arguments("think strcat(%q<%s>)")]
	[Arguments("think [setq(0,foo,foo,dog)][strcat(%q<%q0>)]","dog")]
	// [Arguments("think strcat(%q<Word %q<5> [strcat(%q<6six>)]>)")]
	public async Task Test(string str, string? expected = null)
	{
		Console.WriteLine("Testing: {0}", str);

		var parser = TestParser(
			ds: database,
			ps: infrastructure!.Services.GetService(typeof(IPermissionService)) as IPermissionService,
			ls: infrastructure!.Services.GetService(typeof(ILocateService)) as ILocateService
			);
		await parser!.CommandParse("1", MModule.single(str));

		if (expected is not null)
		{
			await parser.NotifyService
				.Received(Quantity.Exactly(1))
				.Notify(Arg.Any<AnySharpObject>(), expected);
		}
	}
}