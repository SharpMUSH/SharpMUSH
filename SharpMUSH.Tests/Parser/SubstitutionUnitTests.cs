using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Parser;

public class SubstitutionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.Services.GetRequiredService<IMUSHCodeParser>();

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();

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
	[Arguments("think [setq(10,foo)][strcat(%q<[strcat(1,0)]>)]7", "foo7")]
	[Arguments("think [setq(0,foo,foo,dog)][strcat(%q<%q0>)]8", "dog8")]
	[Arguments("think [setq(0,hello)][setr(1,0)]%q<[r(1)]>", "0hello")]
	[Arguments("think [setq(2,result)]%q<[add(1,1)]>", "result")]
	[Arguments("think [setq(0,a)][setq(a,found)]%q<%q0>", "found")]
	[Arguments("think %q<nonexistent>-", "-")]
	[Arguments("think [setq(99,big)]%q<99>", "big")]
	// Nested %q<> inside %q<>: inner resolves first, outer uses result
	[Arguments("think [setq(5,mid)][setq(mid,deep)]%q<%q<5>>end", "deepend")]
	[Arguments("think [setr(ab,X)]%q<a[strcat(b)]>", "XX")]
	[Arguments("think [setq(0,a)][setq(a,b)][setq(b,final)]%q<%q<%q0>>", "final")]
	[Arguments("think %q<[setq(x,y)]x>%qx", "yy")]
	[Arguments("think [setq(,hello)]%q<>-", "#-1 REGISTER NAME INVALID-")]
	// %b in %q<> produces space in name → invalid
	[Arguments("think [setq(a b,spaced)]%q<a%bb>", "#-1 REGISTER NAME INVALID")]
	// Unknown % substitutions: PennMUSH strips the % and outputs just the letter
	[Arguments("think %z", "z")]
	[Arguments("think %Z", "Z")]
	[Arguments("think %%", "%")]
	[Arguments(@"think hello\%", @"hello\%")]
	[Arguments("think [setq(0,X)][iter(a b,##%q0)]", "aX bX")]
	// #@ is 1-indexed in iter
	[Arguments("think [iter(a b c,#@)]", "1 2 3")]
	[Arguments("think [iter(a b,[iter(1 2,[itext(1)]-[itext(0)])])]", "a-1 a-2 b-1 b-2")]
	// ## does NOT expand in switch — literal ##
	[Arguments("think [switch(x,x,##)]", "##")]
	[Arguments("think [iter(a|b|c,##!,|,!)]", "a!!b!!c!")]
	// Double evaluation via s() — %%q1 stores as %q1, s() evaluates it
	[Arguments("think [setq(0,%%q1)][setq(1,deep)][s(%q0)]", "deep")]
	[Arguments(@"think [lit([add(1,2)])]", "[add(1,2)]")]
	// PennMUSH double-evaluates ##: item text is substituted, then body is evaluated
	// SharpMUSH intentionally does NOT double-evaluate ## (safer, no re-parsing)
	// iter([lit([add(1,2)])], ##) — PennMUSH gives 3, SharpMUSH gives [add(1,2)]
	[Arguments("think [iter([lit([add(1,2)])],cat(##))]", "[add(1,2)]")]
	[Arguments("think [iter([lit([add(1,2)])],strlen(##))]", "10")]
	public async Task Test(string str, string? expected = null)
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		Console.WriteLine("Testing: {0}", str);

		await Parser.CommandParse(1, ConnectionService, MModule.single(str));

		if (expected is not null)
		{
			await NotifyService.Notify(TestHelpers.MatchingObject(executor),
				expected,
				null,
				INotifyService.NotificationType.Announce);
		}
	}
}