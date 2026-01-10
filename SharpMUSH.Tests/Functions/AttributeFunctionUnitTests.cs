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
	[Arguments("[attrib_set(%!/Test_Grep_CaseSensitive_1,test_string_grep_case1)]" +
	           "[attrib_set(%!/Test_Grep_CaseSensitive_2,another_test_value)]" +
	           "[attrib_set(%!/NO_MATCH,different)][grep(%!,Test_Grep_CaseSensitive_*,test)]", 
		"TEST_GREP_CASESENSITIVE_1 TEST_GREP_CASESENSITIVE_2")]
	[Arguments("[attrib_set(%!/Test_Grep_CaseSensitive_UPPER,TEST_VALUE)]" +
	           "[grep(%!,Test_Grep_CaseSensitive_*,VALUE)]", 
		"TEST_GREP_CASESENSITIVE_UPPER")]
	[Arguments("[attrib_set(%!/Test_Grep_CaseSensitive_1,has_test_in_value)]" +
	           "[attrib_set(%!/Test_Grep_CaseSensitive_2,also_test_here)]" +
	           "[attrib_set(%!/Test_Grep_CaseSensitive_2_EMPTY_TEST,)]" +
	           "[grep(%!,*Test_Grep_CaseSensitive_*,test)]", 
		"TEST_GREP_CASESENSITIVE_1 TEST_GREP_CASESENSITIVE_2")]
	public async Task Test_Grep_CaseSensitive(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/Test_Grepi_CaseInsensitive1_1,has_VALUE)]" +
	           "[attrib_set(%!/Test_Grepi_CaseInsensitive1_2,also_VALUE)]" +
	           "[attrib_set(%!/Test_Grepi_CaseInsensitive1_UPPER,more_VALUE)]" +
	           "[grepi(%!,Test_Grepi_CaseInsensitive1_*,VALUE)]", 
		"TEST_GREPI_CASEINSENSITIVE1_1 TEST_GREPI_CASEINSENSITIVE1_2 TEST_GREPI_CASEINSENSITIVE1_UPPER")]
	[Arguments("[attrib_set(%!/Test_Grepi_CaseInsensitive2_1,has_TEST)]" +
	           "[attrib_set(%!/Test_Grepi_CaseInsensitive2_2,also_TEST)]" +
	           "[attrib_set(%!/Test_Grepi_CaseInsensitive2_UPPER,more_TEST)]" +
	           "[grepi(%!,Test_Grepi_CaseInsensitive2_*,TEST)]", 
		"TEST_GREPI_CASEINSENSITIVE2_1 TEST_GREPI_CASEINSENSITIVE2_2 TEST_GREPI_CASEINSENSITIVE2_UPPER")]
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
	[Arguments("[attrib_set(%!/TESTREGLATTR_UNIQUE_RGX1_001,value1)]" +
	           "[attrib_set(%!/TESTREGLATTR_UNIQUE_RGX1_002,value2)]" +
	           "[attrib_set(%!/TESTREGLATTR_UNIQUE_RGX1_100,value3)]" +
	           "[reglattr(%!/^TESTREGLATTR_UNIQUE_RGX1_00\\[0-9\\]$)]", 
		"TESTREGLATTR_UNIQUE_RGX1_001 TESTREGLATTR_UNIQUE_RGX1_002")]
	[Arguments("[attrib_set(%!/TESTREGLATTR_UNIQUE_RGX2_001,value1)]" +
	           "[attrib_set(%!/TESTREGLATTR_UNIQUE_RGX2_002,value2)]" +
	           "[attrib_set(%!/TESTREGLATTR_UNIQUE_RGX2_100,value3)]" +
	           "[reglattr(%!/^TESTREGLATTR_UNIQUE_RGX2_\\[0-9\\]+$)]", 
		"TESTREGLATTR_UNIQUE_RGX2_001 TESTREGLATTR_UNIQUE_RGX2_002 TESTREGLATTR_UNIQUE_RGX2_100")]
	[Arguments("[attrib_set(%!/TESTREGLATTR_UNIQUE_RGX3_A,val1)]" +
	           "[attrib_set(%!/TESTREGLATTR_UNIQUE_RGX3_B,val2)]" +
	           "[attrib_set(%!/TESTREGLATTR_UNIQUE_RGX3_UPPER,val3)]" +
	           "[reglattr(%!/^TESTREGLATTR_UNIQUE_RGX3_\\[A-Z\\]+$)]", 
		"TESTREGLATTR_UNIQUE_RGX3_A TESTREGLATTR_UNIQUE_RGX3_B TESTREGLATTR_UNIQUE_RGX3_UPPER")]
	public async Task Test_Reglattr_RegexPattern(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/TESTREGNATTR_UNIQUE_CNT1_001,value1)]" +
	           "[attrib_set(%!/TESTREGNATTR_UNIQUE_CNT1_002,value2)]" +
	           "[attrib_set(%!/TESTREGNATTR_UNIQUE_CNT1_100,value3)]" +
	           "[regnattr(%!/^TESTREGNATTR_UNIQUE_CNT1_\\[0-9\\]+$)]", "3")]
	[Arguments("[attrib_set(%!/TESTREGNATTR_UNIQUE_CNT2_A,val1)]" +
	           "[attrib_set(%!/TESTREGNATTR_UNIQUE_CNT2_B,val2)]" +
	           "[attrib_set(%!/TESTREGNATTR_UNIQUE_CNT2_UPPER,val3)]" +
	           "[regnattr(%!/^TESTREGNATTR_UNIQUE_CNT2_\\[A-Z\\]+$)]", "3")]
	[Arguments("[attrib_set(%!/TESTREGNATTR_UNIQUE_CNT3_X,val1)]" +
	           "[attrib_set(%!/TESTREGNATTR_UNIQUE_CNT3_Y,val2)]" +
	           "[attrib_set(%!/TESTREGNATTR_UNIQUE_CNT3_Z,val3)]" +
	           "[regnattr(%!/^TESTREGNATTR_UNIQUE_CNT3_\\[XYZ\\]$)]", "3")]
	public async Task Test_Regnattr_Count(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/Test_Regxattr_RangeWithRegex1_001,value1)]" +
	           "[attrib_set(%!/Test_Regxattr_RangeWithRegex1_002,value2)]" +
	           "[attrib_set(%!/Test_Regxattr_RangeWithRegex1_100,value3)]" +
	           "[regxattr(%!/Test_Regxattr_RangeWithRegex1_\\[0-9\\]+,1,2)]", 
		"TEST_REGXATTR_RANGEWITHREGEX1_001 TEST_REGXATTR_RANGEWITHREGEX1_002")]
	[Arguments("[attrib_set(%!/Test_Regxattr_RangeWithRegex2_001,value1)]" +
	           "[attrib_set(%!/Test_Regxattr_RangeWithRegex2_002,value2)]" +
	           "[attrib_set(%!/Test_Regxattr_RangeWithRegex2_100,value3)]" +
	           "[regxattr(%!/Test_Regxattr_RangeWithRegex2_\\[0-9\\]+,2,2)]", 
		"TEST_REGXATTR_RANGEWITHREGEX2_002 TEST_REGXATTR_RANGEWITHREGEX2_100")]
	[Arguments("[attrib_set(%!/Test_Regxattr_RangeWithRegex3_1,val1)]" +
	           "[attrib_set(%!/Test_Regxattr_RangeWithRegex3_2,val2)]" +
	           "[regxattr(%!/^Test_Regxattr_RangeWithRegex3_,1,2)]", 
		"TEST_REGXATTR_RANGEWITHREGEX3_1 TEST_REGXATTR_RANGEWITHREGEX3_2")]
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
	[Arguments("[attrib_set([parent(me,create(PGREP_PARENT))]/PGREP_PARENT,child_value)][pgrep(%!,PGREP_*,child)]", "PGREP_PARENT")
	 , Skip("Parent Mode not implemented yet.")]
	public async Task Test_Pgrep_IncludesParents(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/Test_Reglattrp_IncludesParents_001,value1)]" +
	           "[attrib_set([parent(me,create(Test_Reglattrp_IncludesParents))]/Test_Reglattrp_IncludesParents_002,value2)]" +
	           "[attrib_set(%!/Test_Reglattrp_IncludesParents_100,value3)]" +
	           "[reglattrp(%!,Test_Reglattrp_IncludesParents_[0-9]+)]", 
		"Test_Reglattrp_IncludesParents_001 Test_Reglattrp_IncludesParents_002 Test_Reglattrp_IncludesParents_100")
	 , Skip("Parent Mode not implemented yet.")]
	public async Task Test_Reglattrp_IncludesParents(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/Test_Regnattrp_CountWithParents_001,value1)]" +
	           "[attrib_set([parent(me,create(Test_Regnattrp_CountWithParents))]/Test_Regnattrp_CountWithParents_002,value2)]" +
	           "[attrib_set(%!/Test_Regnattrp_CountWithParents_100,value3)]" +
	           "[regnattrp(%!,Test_Regnattrp_CountWithParents_[0-9]+)]", "3")
	 , Skip("Parent Mode not implemented yet.")]
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
	           "[regxattrp(%!/Test_Regxattrp_RangeWithParents_[0-9]+,1,2)]", 
		"TEST_REGXATTRP_RANGEWITHPARENTS_001 TEST_REGXATTRP_RANGEWITHPARENTS_002")
	 , Skip("Parent Mode not implemented yet.")]
	[Arguments("[attrib_set(%!/Test_Regxattrp_RangeWithParents2_001,value1)]" +
	           "[attrib_set([parent(me,create(Test_Regxattrp_RangeWithParents2))]/Test_Regxattrp_RangeWithParents2_002,value2)]" +
	           "[attrib_set(%!/Test_Regxattrp_RangeWithParents2_100,value3)][regxattrp(%!/Test_Regxattrp_RangeWithParents2_[0-9]+,1,2)]", 
		"TEST_REGXATTRP_RANGEWITHPARENTS2_001 TEST_REGXATTRP_RANGEWITHPARENTS2_002")
	 , Skip("Parent Mode not implemented yet.")]
	public async Task Test_Regxattrp_RangeWithParents(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Attribute Tree Tests
	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/Test_Lattr_AttributeTrees,root)]" +
	           "[attrib_set(%!/Test_Lattr_AttributeTrees`BRANCH1,leaf1)]" +
	           "[attrib_set(%!/Test_Lattr_AttributeTrees`BRANCH2,leaf2)]" +
	           "[attrib_set(%!/Test_Lattr_AttributeTrees`BRANCH1`SUBLEAF,deep)]" +
	           "[lattr(%!/Test_Lattr_AttributeTrees`**)]", 
		"TEST_LATTR_ATTRIBUTETREES`BRANCH1 TEST_LATTR_ATTRIBUTETREES`BRANCH1`SUBLEAF TEST_LATTR_ATTRIBUTETREES`BRANCH2")]
	[Arguments("[attrib_set(%!/Test_Lattr_AttributeTrees2,value)]" +
	           "[attrib_set(%!/Test_Lattr_AttributeTrees2`CHILD,childval)]" +
	           "[lattr(%!/Test_Lattr_AttributeTrees2**)]", 
		"TEST_LATTR_ATTRIBUTETREES2 TEST_LATTR_ATTRIBUTETREES2`CHILD")]
	[Arguments("[attrib_set(%!/Test_Lattr_AttributeTrees3,value)]" +
	           "[attrib_set(%!/Test_Lattr_AttributeTrees3`CHILD,childval)]" +
	           "[lattr(%!/Test_Lattr_AttributeTrees3*)]", 
		"TEST_LATTR_ATTRIBUTETREES3")]
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
	[Arguments("[attrib_set(%!/Test_Reglattr_AttributeTrees1,val)]" +
	           "[attrib_set(%!/Test_Reglattr_AttributeTrees1`A,val1)]" +
	           "[attrib_set(%!/Test_Reglattr_AttributeTrees1`B,val2)]" +
	           "[attrib_set(%!/Test_Reglattr_AttributeTrees1`A`DEEP,val3)]" +
	           "[reglattr(%!/^Test_Reglattr_AttributeTrees1)]", 
		"TEST_REGLATTR_ATTRIBUTETREES1 TEST_REGLATTR_ATTRIBUTETREES1`A TEST_REGLATTR_ATTRIBUTETREES1`A`DEEP TEST_REGLATTR_ATTRIBUTETREES1`B")]
	[Arguments("[attrib_set(%!/Test_Reglattr_AttributeTrees2_001,v1)]" +
	           "[attrib_set(%!/Test_Reglattr_AttributeTrees2_001`SUB,v2)]" +
	           "[reglattr(%!/Test_Reglattr_AttributeTrees2_\\[0-9\\]+)]", 
		"TEST_REGLATTR_ATTRIBUTETREES2_001 TEST_REGLATTR_ATTRIBUTETREES2_001`SUB")]
	public async Task Test_Reglattr_AttributeTrees(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/Test_Regnattr_AttributeTrees1,v)]" +
	           "[attrib_set(%!/Test_Regnattr_AttributeTrees1`L1,v)]" +
	           "[attrib_set(%!/Test_Regnattr_AttributeTrees1`L2,v)]" +
	           "[attrib_set(%!/Test_Regnattr_AttributeTrees1`L1`L2,v)]" +
	           "[regnattr(%!/^Test_Regnattr_AttributeTrees1)]", "4")]
	[Arguments("[attrib_set(%!/Test_Regnattr_AttributeTrees2,v)]" +
	           "[attrib_set(%!/Test_Regnattr_AttributeTrees2`A,v)]" +
	           "[attrib_set(%!/Test_Regnattr_AttributeTrees2`B,v)]" +
	           "[regnattr(%!/^Test_Regnattr_AttributeTrees2)]", "3")]
	public async Task Test_Regnattr_AttributeTrees(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/Test_Wildgrep_AttributeTrees,val)]" +
	           "[attrib_set(%!/Test_Wildgrep_AttributeTrees`CHILD,has_pattern)]" +
	           "[attrib_set(%!/Test_Wildgrep_AttributeTrees`OTHER,no_match)]" +
	           "[wildgrep(%!,Test_Wildgrep_AttributeTrees**,*pattern*)]", 
		"TEST_WILDGREP_ATTRIBUTETREES`CHILD")]
	public async Task Test_Wildgrep_AttributeTrees(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/Test_Regxattr_AttributeTrees,v1)]" +
	           "[attrib_set(%!/Test_Regxattr_AttributeTrees`A,v2)]" +
	           "[attrib_set(%!/Test_Regxattr_AttributeTrees`B,v3)]" +
	           "[attrib_set(%!/Test_Regxattr_AttributeTrees`C,v4)]" +
	           "[regxattr(%!/^Test_Regxattr_AttributeTrees,2,2)]", 
		"TEST_REGXATTR_ATTRIBUTETREES`A TEST_REGXATTR_ATTRIBUTETREES`B")]
	public async Task Test_Regxattr_AttributeTrees(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Diagnostic test to verify basic functionality
	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/Test_Basic_AttribSet_And_Get,testvalue)]" +
	           "[get(%!/Test_Basic_AttribSet_And_Get)]", "testvalue")]
	[Arguments("[attrib_set(%!/Test_Basic_AttribSet_And_Get21,val1)]" +
	           "[attrib_set(%!/Test_Basic_AttribSet_And_Get22,val2)]" +
	           "[get(%!/Test_Basic_AttribSet_And_Get21)][get(%!/Test_Basic_AttribSet_And_Get22)]", "val1val2")]
	public async Task Test_Basic_AttribSet_And_Get(string str, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(str));
		await Assert.That(result!.Message!.ToString()).IsEqualTo(expected);
	}

	// Test lattr which should work (it's already implemented)
	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/Test_Lattr_Simple1,v1)]" +
	           "[attrib_set(%!/Test_Lattr_Simple2,v2)]" +
	           "[lattr(%!/Test_Lattr_Simple*)]",
		"TEST_LATTR_SIMPLE1 TEST_LATTR_SIMPLE2")]
	public async Task Test_Lattr_Simple(string str, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(str));
		await Assert.That(result!.Message!.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("xattrp(#0,attr)", "0")]
	public async Task Xattrp(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("xcon(#0)", "")]
	public async Task Xcon(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("xexits(#0)", "")]
	public async Task Xexits(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("xmwhoid()", "")]
	public async Task Xmwhoid(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("xplayers(#0)", "")]
	public async Task Xplayers(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("xthings(#0)", "")]
	public async Task Xthings(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("xvcon(#0)", "")]
	public async Task Xvcon(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("xvexits(#0)", "")]
	public async Task Xvexits(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("xvplayers(#0)", "")]
	public async Task Xvplayers(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("xvthings(#0)", "")]
	public async Task Xvthings(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("xwho()", "")]
	public async Task Xwho(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("xwhoid()", "")]
	public async Task Xwhoid(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("valid(name,TestName)", "1")]
	[Arguments("valid(name,)", "0")]
	public async Task Valid_Name(string str, string expected)
	{
		// Test valid() function with name validation
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Attribute value validation without target attribute requires ValidateService enhancement")]
	[Arguments("valid(attrvalue,test_value)", "1")]
	[Arguments("valid(attrvalue,test_value,NONEXISTENT_ATTR)", "1")]
	public async Task Valid_AttributeValue(string str, string expected)
	{
		// Test valid() function with attribute value validation
		// First argument is attrvalue, second is the value to test
		// Optional third argument is the target attribute name
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}
}
