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
	[NotInParallel]
	[Arguments("[attrib_set(%!/TEST_GREP_1,test_string_grep_case1)][attrib_set(%!/TEST_GREP_2,another_test_value)][attrib_set(%!/NO_MATCH,different)][grep(%!,TEST_*,test)]", "TEST_GREP_1 TEST_GREP_2")]
	[Arguments("[attrib_set(%!/TEST_GREP_UPPER,TEST_VALUE)][grep(%!,TEST_*,VALUE)]", "TEST_GREP_UPPER")]
	[Arguments("[attrib_set(%!/TEST_GREP_1,has_test_in_value)][attrib_set(%!/TEST_GREP_2,also_test_here)][attrib_set(%!/EMPTY_TEST,)][grep(%!,*TEST*,test)]", "TEST_GREP_1 TEST_GREP_2")]
	public async Task Test_Grep_CaseSensitive(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/TEST_GREP_1,has_VALUE)][attrib_set(%!/TEST_GREP_2,also_VALUE)][attrib_set(%!/TEST_GREP_UPPER,more_VALUE)][grepi(%!,TEST_*,VALUE)]", "TEST_GREP_1 TEST_GREP_2 TEST_GREP_UPPER")]
	[Arguments("[attrib_set(%!/TEST_GREP_1,has_TEST)][attrib_set(%!/TEST_GREP_2,also_TEST)][attrib_set(%!/TEST_GREP_UPPER,more_TEST)][grepi(%!,TEST_GREP_*,TEST)]", "TEST_GREP_1 TEST_GREP_2 TEST_GREP_UPPER")]
	public async Task Test_Grepi_CaseInsensitive(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/WILDGREP_1,test_wildcard_*_match)][attrib_set(%!/WILDGREP_2,different)][wildgrep(%!,WILDGREP_*,*wildcard*)]", "WILDGREP_1")]
	[Arguments("[attrib_set(%!/WILDGREP_1,test_wildcard_value_match)][wildgrep(%!,WILDGREP_*,test_*_match)]", "WILDGREP_1")]
	public async Task Test_Wildgrep_Pattern(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/WILDGREP_1,has_WILDCARD)][attrib_set(%!/WILDGREP_UPPER,TEST_WILDCARD)][wildgrepi(%!,WILDGREP_*,*WILDCARD*)]", "WILDGREP_1 WILDGREP_UPPER")]
	public async Task Test_Wildgrepi_CaseInsensitive(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/ATTR_001,value1)][attrib_set(%!/ATTR_002,value2)][attrib_set(%!/ATTR_100,value3)][reglattr(%!,ATTR_0+)]", "ATTR_001 ATTR_002")]
	[Arguments("[attrib_set(%!/ATTR_001,value1)][attrib_set(%!/ATTR_002,value2)][attrib_set(%!/ATTR_100,value3)][reglattr(%!,ATTR_[0-9]+)]", "ATTR_001 ATTR_002 ATTR_100")]
	[Arguments("[attrib_set(%!/TEST_GREP_1,val1)][attrib_set(%!/TEST_GREP_2,val2)][attrib_set(%!/TEST_GREP_UPPER,val3)][reglattr(%!,^TEST_)]", "TEST_GREP_1 TEST_GREP_2 TEST_GREP_UPPER")]
	public async Task Test_Reglattr_RegexPattern(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/ATTR_001,value1)][attrib_set(%!/ATTR_002,value2)][attrib_set(%!/ATTR_100,value3)][regnattr(%!,ATTR_[0-9]+)]", "3")]
	[Arguments("[attrib_set(%!/TEST_GREP_1,val1)][attrib_set(%!/TEST_GREP_2,val2)][attrib_set(%!/TEST_GREP_UPPER,val3)][regnattr(%!,^TEST_)]", "3")]
	[Arguments("[attrib_set(%!/WILDGREP_1,val1)][attrib_set(%!/WILDGREP_2,val2)][attrib_set(%!/WILDGREP_UPPER,val3)][regnattr(%!,WILDGREP_)]", "3")]
	public async Task Test_Regnattr_Count(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
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
	[NotInParallel]
	[Arguments("[attrib_set(%!/PGREP_CHILD,child_value)][pgrep(%!,PGREP_*,child)]", "PGREP_CHILD")]
	public async Task Test_Pgrep_IncludesParents(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/ATTR_001,value1)][attrib_set(%!/ATTR_002,value2)][attrib_set(%!/ATTR_100,value3)][reglattrp(%!,ATTR_[0-9]+)]", "ATTR_001 ATTR_002 ATTR_100")]
	public async Task Test_Reglattrp_IncludesParents(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/ATTR_001,value1)][attrib_set(%!/ATTR_002,value2)][attrib_set(%!/ATTR_100,value3)][regnattrp(%!,ATTR_[0-9]+)]", "3")]
	public async Task Test_Regnattrp_CountWithParents(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/Test_Regxattrp_RangeWithParents_001,value1)]" +
	           "[attrib_set(%!/Test_Regxattrp_RangeWithParents_002,value2)]" +
	           "[attrib_set(%!/Test_Regxattrp_RangeWithParents_100,value3)]" +
	           "[regxattrp(%!/Test_Regxattrp_RangeWithParents_[0-9]+,1,2)]", "TEST_REGXATTRP_RANGEWITHPARENTS_001 TEST_REGXATTRP_RANGEWITHPARENTS_002")]
	[Arguments("[attrib_set(%!/Test_Regxattrp_RangeWithParents2_001,value1)]" +
	           "[attrib_set([parent(me,create(Test_Regxattrp_RangeWithParents2))]/Test_Regxattrp_RangeWithParents2_002,value2)]" +
	           "[attrib_set(%!/Test_Regxattrp_RangeWithParents2_100,value3)][regxattrp(%!/Test_Regxattrp_RangeWithParents2_[0-9]+,1,2)]", "TEST_REGXATTRP_RANGEWITHPARENTS2_001 TEST_REGXATTRP_RANGEWITHPARENTS2_002")]
	public async Task Test_Regxattrp_RangeWithParents(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Attribute Tree Tests
	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/Test_Lattr_AttributeTrees,root)][attrib_set(%!/Test_Lattr_AttributeTrees`BRANCH1,leaf1)][attrib_set(%!/Test_Lattr_AttributeTrees`BRANCH2,leaf2)][attrib_set(%!/Test_Lattr_AttributeTrees`BRANCH1`SUBLEAF,deep)][lattr(%!/Test_Lattr_AttributeTrees**)]", "TEST_LATTR_ATTRIBUTETREES TEST_LATTR_ATTRIBUTETREES`BRANCH1 TREE`BRANCH1`SUBLEAF TEST_LATTR_ATTRIBUTETREES`BRANCH2")]
	[Arguments("[attrib_set(%!/Test_Lattr_AttributeTrees2,value)][attrib_set(%!/Test_Lattr_AttributeTrees2`CHILD,childval)][lattr(%!/Test_Lattr_AttributeTrees2**)]", "Test_Lattr_AttributeTrees2 Test_Lattr_AttributeTrees2`CHILD")]
	[Arguments("[attrib_set(%!/Test_Lattr_AttributeTrees3,value)][attrib_set(%!/Test_Lattr_AttributeTrees3`CHILD,childval)][lattr(%!/Test_Lattr_AttributeTrees3*)]", "Test_Lattr_AttributeTrees3")]
	public async Task Test_Lattr_AttributeTrees(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/Test_Grep_AttributeTrees,root)]" +
	           "[attrib_set(%!/Test_Grep_AttributeTrees`BRANCH1,has_search_term)]" +
	           "[attrib_set(%!/Test_Grep_AttributeTrees`BRANCH2,different)]" +
	           "[grep(%!,Test_Grep_AttributeTrees**,search)]", "TEST_GREP_ATTRIBUTETREES`BRANCH1")]
	[Arguments("[attrib_set(%!/Test_Grep_AttributeTrees_2,test)]" +
	           "[attrib_set(%!/Test_Grep_AttributeTrees_2`SUB1,contains_test)]" +
	           "[attrib_set(%!/Test_Grep_AttributeTrees_2`SUB2,no_match)]" +
	           "[grep(%!,Test_Grep_AttributeTrees_2**,test)]", "TEST_GREP_ATTRIBUTETREES_2 TEST_GREP_ATTRIBUTETREES_2`SUB1")]
	public async Task Test_Grep_AttributeTrees(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/ROOT,val)][attrib_set(%!/ROOT`A,val1)][attrib_set(%!/ROOT`B,val2)][attrib_set(%!/ROOT`A`DEEP,val3)][reglattr(%!/^ROOT)]", "ROOT ROOT`A ROOT`A`DEEP ROOT`B")]
	[Arguments("[attrib_set(%!/ATTR_001,v1)][attrib_set(%!/ATTR_001`SUB,v2)][reglattr(%!/ATTR_[0-9]+)]", "ATTR_001 ATTR_001`SUB")]
	public async Task Test_Reglattr_AttributeTrees(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/BASE,v)][attrib_set(%!/BASE`L1,v)][attrib_set(%!/BASE`L2,v)][attrib_set(%!/BASE`L1`L2,v)][regnattr(%!/^BASE)]", "4")]
	[Arguments("[attrib_set(%!/TEST,v)][attrib_set(%!/TEST`A,v)][attrib_set(%!/TEST`B,v)][regnattr(%!/^TEST)]", "3")]
	public async Task Test_Regnattr_AttributeTrees(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/WILD,val)][attrib_set(%!/WILD`CHILD,has_pattern)][attrib_set(%!/WILD`OTHER,no_match)][wildgrep(%!,WILD*,*pattern*)]", "WILD`CHILD")]
	public async Task Test_Wildgrep_AttributeTrees(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/RANGE,v1)][attrib_set(%!/RANGE`A,v2)][attrib_set(%!/RANGE`B,v3)][attrib_set(%!/RANGE`C,v4)][regxattr(%!/^RANGE,2,2)]", "RANGE`A RANGE`B")]
	public async Task Test_Regxattr_AttributeTrees(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Diagnostic test to verify basic functionality
	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/TESTATTR,testvalue)][get(%!/TESTATTR)]", "testvalue")]
	[Arguments("[attrib_set(%!/ATTR1,val1)][attrib_set(%!/ATTR2,val2)][get(%!/ATTR1)][get(%!/ATTR2)]", "val1val2")]
	public async Task Test_Basic_AttribSet_And_Get(string str, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(str));
		await Assert.That(result!.Message!.ToString()).IsEqualTo(expected);
	}

	// Test lattr which should work (it's already implemented)
	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/TEST1,v1)][attrib_set(%!/TEST2,v2)][lattr(%!/TEST*)]", "TEST1 TEST2")]
	public async Task Test_Lattr_Simple(string str, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(str));
		await Assert.That(result!.Message!.ToString()).IsEqualTo(expected);
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
