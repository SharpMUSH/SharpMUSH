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
	
	
	[Test]
	[Arguments("[attrib_set(%!/TEST_GREP_1,test_string_grep_case1)][attrib_set(%!/TEST_GREP_2,another_test_value)][attrib_set(%!/NO_MATCH,different)][grep(%!,TEST_*,test)]", "TEST_GREP_1 TEST_GREP_2")]
	[Arguments("[attrib_set(%!/TEST_GREP_UPPER,TEST_VALUE)][grep(%!,TEST_*,VALUE)]", "TEST_GREP_UPPER")]
	[Arguments("[attrib_set(%!/EMPTY_TEST,)][grep(%!,*TEST*,test)]", "TEST_GREP_1 TEST_GREP_2")]
	public async Task Test_Grep_CaseSensitive(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[DependsOn(nameof(Test_Grep_CaseSensitive))]
	[Arguments("[grepi(%!,TEST_*,VALUE)]", "TEST_GREP_1 TEST_GREP_2 TEST_GREP_UPPER")]
	[Arguments("[grepi(%!,TEST_GREP_*,TEST)]", "TEST_GREP_1 TEST_GREP_2 TEST_GREP_UPPER")]
	public async Task Test_Grepi_CaseInsensitive(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("[attrib_set(%!/WILDGREP_1,test_wildcard_*_match)][attrib_set(%!/WILDGREP_2,different)][wildgrep(%!,WILDGREP_*,*wildcard*)]", "WILDGREP_1")]
	[Arguments("[wildgrep(%!,WILDGREP_*,test_*_match)]", "WILDGREP_1")]
	public async Task Test_Wildgrep_Pattern(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[DependsOn(nameof(Test_Wildgrep_Pattern))]
	[Arguments("[attrib_set(%!/WILDGREP_UPPER,TEST_WILDCARD)][wildgrepi(%!,WILDGREP_*,*WILDCARD*)]", "WILDGREP_1 WILDGREP_UPPER")]
	public async Task Test_Wildgrepi_CaseInsensitive(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("[attrib_set(%!/ATTR_001,value1)][attrib_set(%!/ATTR_002,value2)][attrib_set(%!/ATTR_100,value3)][reglattr(%!,ATTR_0+)]", "ATTR_001 ATTR_002")]
	[Arguments("[attrib_set(%!/ATTR_001,value1)][attrib_set(%!/ATTR_002,value2)][attrib_set(%!/ATTR_100,value3)][reglattr(%!,ATTR_[0-9]+)]", "ATTR_001 ATTR_002 ATTR_100")]
	[Arguments("[attrib_set(%!/TEST_GREP_1,val1)][attrib_set(%!/TEST_GREP_2,val2)][attrib_set(%!/TEST_GREP_UPPER,val3)][reglattr(%!,^TEST_)]", "TEST_GREP_1 TEST_GREP_2 TEST_GREP_UPPER")]
	public async Task Test_Reglattr_RegexPattern(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[DependsOn(nameof(Test_Reglattr_RegexPattern))]
	[Arguments("[attrib_set(%!/ATTR_001,value1)][attrib_set(%!/ATTR_002,value2)][attrib_set(%!/ATTR_100,value3)][regnattr(%!,ATTR_[0-9]+)]", "3")]
	[Arguments("[attrib_set(%!/TEST_GREP_1,val1)][attrib_set(%!/TEST_GREP_2,val2)][attrib_set(%!/TEST_GREP_UPPER,val3)][regnattr(%!,^TEST_)]", "3")]
	[Arguments("[attrib_set(%!/WILDGREP_1,val1)][attrib_set(%!/WILDGREP_2,val2)][attrib_set(%!/WILDGREP_UPPER,val3)][regnattr(%!,WILDGREP_)]", "3")]
	public async Task Test_Regnattr_Count(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[DependsOn(nameof(Test_Regnattr_Count))]
	[Arguments("[attrib_set(%!/ATTR_001,value1)][attrib_set(%!/ATTR_002,value2)][attrib_set(%!/ATTR_100,value3)][regxattr(%!/ATTR_[0-9]+,1,2)]", "ATTR_001 ATTR_002")]
	[Arguments("[attrib_set(%!/ATTR_001,value1)][attrib_set(%!/ATTR_002,value2)][attrib_set(%!/ATTR_100,value3)][regxattr(%!/ATTR_[0-9]+,2,2)]", "ATTR_002 ATTR_100")]
	[Arguments("[attrib_set(%!/TEST_GREP_1,val1)][attrib_set(%!/TEST_GREP_2,val2)][regxattr(%!/^TEST_,1,2)]", "TEST_GREP_1 TEST_GREP_2")]
	public async Task Test_Regxattr_RangeWithRegex(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Zones Not Yet Implemented")]
	[Arguments("zfun(TEST_ATTR)", "#-1 ZONES NOT YET IMPLEMENTED")]
	public async Task Test_Zfun_NotImplemented(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("regrep(%#,test,*)", "")]
	public async Task Regrep(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("regrepi(%#,test,*)", "")]
	public async Task Regrepi(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("regedit(obj/attr,pattern,replacement)", "")]
	public async Task Regedit(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("xattr(#0,attr)", "")]
	public async Task Xattr(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("[attrib_set(%!/PGREP_CHILD,child_value)][pgrep(%!,PGREP_*,child)]", "PGREP_CHILD")]
	public async Task Test_Pgrep_IncludesParents(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[DependsOn(nameof(Test_Reglattr_RegexPattern))]
	[Arguments("[attrib_set(%!/ATTR_001,value1)][attrib_set(%!/ATTR_002,value2)][attrib_set(%!/ATTR_100,value3)][reglattrp(%!,ATTR_[0-9]+)]", "ATTR_001 ATTR_002 ATTR_100")]
	public async Task Test_Reglattrp_IncludesParents(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[DependsOn(nameof(Test_Regnattr_Count))]
	[Arguments("[attrib_set(%!/ATTR_001,value1)][attrib_set(%!/ATTR_002,value2)][attrib_set(%!/ATTR_100,value3)][regnattrp(%!,ATTR_[0-9]+)]", "3")]
	public async Task Test_Regnattrp_CountWithParents(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[DependsOn(nameof(Test_Regxattr_RangeWithRegex))]
	[Arguments("[attrib_set(%!/ATTR_001,value1)][attrib_set(%!/ATTR_002,value2)][attrib_set(%!/ATTR_100,value3)][regxattrp(%!/ATTR_[0-9]+,1,2)]", "ATTR_001 ATTR_002")]
	public async Task Test_Regxattrp_RangeWithParents(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("xattrp(#0,attr)", "0")]
	public async Task Xattrp(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("xcon(#0)", "")]
	public async Task Xcon(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("xexits(#0)", "")]
	public async Task Xexits(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("xmwhoid()", "")]
	public async Task Xmwhoid(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("xplayers(#0)", "")]
	public async Task Xplayers(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("xthings(#0)", "")]
	public async Task Xthings(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("xvcon(#0)", "")]
	public async Task Xvcon(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("xvexits(#0)", "")]
	public async Task Xvexits(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("xvplayers(#0)", "")]
	public async Task Xvplayers(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("xvthings(#0)", "")]
	public async Task Xvthings(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("xwho()", "")]
	public async Task Xwho(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("xwhoid()", "")]
	public async Task Xwhoid(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}
}
