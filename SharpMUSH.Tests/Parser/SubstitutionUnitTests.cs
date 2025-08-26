using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Parser;

public class SubstitutionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => (IMUSHCodeParser)WebAppFactoryArg.Services.GetService(typeof(IMUSHCodeParser))!;

	[Test]
	[Arguments("think %t", "\t")]
	[Arguments("think %#", "#1")]
	[Arguments("think %!", "#1")]
	[Arguments("think %@", "#1")]
	[Arguments("think [strcat(%!,5)]", "#15")]
	[Arguments("think %!6", "#16")]
	[Arguments("think [setq(0,foo)][strcat(%q0,2)]", "foo2")]
	[Arguments("think [setq(test,foo)][strcat(%q<test>,3)]", "foo3")]
	[Arguments("think %s", "they")]
	[Arguments("think [setq(test,foo)]%q<test>4", "foo4")]
	[Arguments("think [setq(0,foo)]%q<0>5", "foo5")]
	[Arguments("think [setq(0,foo)][strcat(%q<0>,6)]", "foo6")]
	// [Arguments("think [setq(word\\(\\),foo)][strcat(%q<word()>)]","foo")]
	[Arguments("think [setq(10,foo)][strcat(%q<[strcat(1,0)]>)]7", "foo7")]
	// [Arguments("think strcat(%q<%s>)")]
	[Arguments("think [setq(0,foo,foo,dog)][strcat(%q<%q0>)]8", "dog8")]
	// [Arguments("think strcat(%q<Word %q<5> [strcat(%q<6six>)]>)")]
	public async Task Test(string str, string? expected = null)
	{
		Console.WriteLine("Testing: {0}", str);

		await Parser!.CommandParse(1, MModule.single(str));

		if (expected is not null)
		{
			await Parser.NotifyService
				.Notify(Arg.Any<AnySharpObject>(), expected);
		}
	}
}