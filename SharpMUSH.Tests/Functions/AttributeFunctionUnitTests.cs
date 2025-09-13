using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class AttributeFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/attribute,ZAP!)][get(%!/attribute)]", "ZAP!")]
	[Arguments("[attrib_set(%!/attribute,ansi(hr,ZAP!))][get(%!/attribute)]", "\e[1;31mZAP!\e[0m")]
	[Arguments("[attrib_set(%!/attribute,ansi(hr,ZIP!))][get(%!/attribute)][attrib_set(%!/attribute,ansi(hr,ZAP!))][get(%!/attribute)]", "\e[1;31mZIP!\e[0m\e[1;31mZAP!\e[0m")]
	public async Task SetAndGet(string input, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(input));
		await Assert.That(result!.Message!.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("%s", "they")]
	[Arguments("%a", "theirs")]
	[Arguments("%p", "their")]
	[Arguments("%o", "them")]
	[Arguments("subj(%#)", "they")]
	[Arguments("aposs(%#)", "theirs")]
	[Arguments("poss(%#)", "their")]
	[Arguments("obj(%#)", "them")]
	public async Task GenderTest1(string input, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(input));
		await Assert.That(result!.Message!.ToString()).IsEqualTo(expected);
	}
	
	[Test]
	[DependsOn(nameof(GenderTest1))]
	[Arguments("%s", "she")]
	[Arguments("%a", "hers")]
	[Arguments("%p", "her")]
	[Arguments("%o", "her")]
	[Arguments("subj(%#)", "she")]
	[Arguments("aposs(%#)", "hers")]
	[Arguments("poss(%#)", "her")]
	[Arguments("obj(%#)", "her")]
	public async Task GenderTest2(string input, string expected)
	{
		await Parser.CommandParse(1,ConnectionService, MModule.single("&GENDER me=F"));
		
		var result = await Parser.FunctionParse(MModule.single(input));
		await Assert.That(result!.Message!.ToString()).IsEqualTo(expected);
	}
	
	[Test]
	[DependsOn(nameof(GenderTest2))]
	[Arguments("%s", "he")]
	[Arguments("%a", "his")]
	[Arguments("%p", "his")]
	[Arguments("%o", "him")]
	[Arguments("subj(%#)", "he")]
	[Arguments("aposs(%#)", "his")]
	[Arguments("poss(%#)", "his")]
	[Arguments("obj(%#)", "him")]
	public async Task GenderTest3(string input, string expected)
	{
		await Parser.CommandParse(1,ConnectionService, MModule.single("&GENDER me=M"));
		
		var result = await Parser.FunctionParse(MModule.single(input));
		await Assert.That(result!.Message!.ToString()).IsEqualTo(expected);
	}
}
