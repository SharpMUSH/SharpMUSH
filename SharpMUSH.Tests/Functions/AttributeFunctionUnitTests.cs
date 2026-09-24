using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class AttributeFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/attribute,ZAP!)][get(%!/attribute)]", "ZAP!")]
	[Arguments("[attrib_set(%!/attribute,ansi(hr,ZAP!))][get(%!/attribute)]", "\e[1;31mZAP!\e[0m")]
	[Arguments("[attrib_set(%!/attribute,ansi(hr,ZIP!))][get(%!/attribute)][attrib_set(%!/attribute,ansi(hr,ZAP!))][get(%!/attribute)]", "\e[1;31mZIP!ZAP!\e[0m")]
	public async Task SetAndGet(string input, string expected)
	{
		var result = await Parser.FunctionParse(MarkupText.Plain(input));
		// The ANSI byte stream, which ToString() no longer produces: it is the plain text now.
		await Assert.That(result!.Message!.Render(MarkupFormat.Ansi)).IsEqualTo(expected);
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
		var result = await Parser.FunctionParse(MarkupText.Plain(input));
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
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("&GENDER me=F"));

		var result = await Parser.FunctionParse(MarkupText.Plain(input));
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
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("&GENDER me=M"));

		var result = await Parser.FunctionParse(MarkupText.Plain(input));
		await Assert.That(result!.Message!.ToString()).IsEqualTo(expected);
	}

	/// <summary>
	/// Runs after all GenderTest2 and GenderTest3 cases complete. Wipes the GENDER attribute
	/// from player #1 so that the shared state does not affect any subsequent tests or retries
	/// that rely on the default (gender-neutral) pronouns.
	/// </summary>
	[Test]
	[DependsOn(nameof(GenderTest2))]
	[DependsOn(nameof(GenderTest3))]
	public async Task GenderCleanup()
	{
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("&GENDER me="));
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
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
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
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/WILDGREP_1,test_wildcard_*_match)][attrib_set(%!/WILDGREP_2,different)][wildgrep(%!,WILDGREP_*,*wildcard*)]", "WILDGREP_1")]
	[Arguments("[attrib_set(%!/WILDGREP_1,test_wildcard_value_match)][wildgrep(%!,WILDGREP_*,test_*_match)]", "WILDGREP_1")]
	public async Task Test_Wildgrep_Pattern(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/WILDGREP_1,has_WILDCARD)][attrib_set(%!/WILDGREP_UPPER,TEST_WILDCARD)][wildgrepi(%!,WILDGREP_*,*WILDCARD*)]", "WILDGREP_1 WILDGREP_UPPER")]
	public async Task Test_Wildgrepi_CaseInsensitive(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
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
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
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
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
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
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/Test_Xattr_FirstMatch1_A,v1)]" +
						 "[attrib_set(%!/Test_Xattr_FirstMatch1_B,v2)]" +
						 "[attrib_set(%!/Test_Xattr_FirstMatch1_C,v3)]" +
						 "[xattr(%!/Test_Xattr_FirstMatch1_*,1,2)]",
		"TEST_XATTR_FIRSTMATCH1_A TEST_XATTR_FIRSTMATCH1_B")]
	[Arguments("[attrib_set(%!/Test_Xattr_StartExceeds1_A,v1)]" +
						 "[attrib_set(%!/Test_Xattr_StartExceeds1_B,v2)]" +
						 "[attrib_set(%!/Test_Xattr_StartExceeds1_C,v3)]" +
						 "[xattr(%!/Test_Xattr_StartExceeds1_*,5,2)]",
		"")]
	[Arguments("[xattr(%!/Test_Xattr_ArgRangeStart1_*,0,2)]", ErrorMessages.Returns.ArgRange)]
	[Arguments("[xattr(%!/Test_Xattr_ArgRangeCount1_*,1,0)]", ErrorMessages.Returns.ArgRange)]
	[Arguments("[xattr(%!/Test_Xattr_NonInteger1_*,x,2)]", ErrorMessages.Returns.Integer)]
	public async Task Test_Xattr_RangeAndErrors(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/Test_Xattrp_FirstMatch1_A,v1)]" +
						 "[attrib_set(%!/Test_Xattrp_FirstMatch1_B,v2)]" +
						 "[attrib_set(%!/Test_Xattrp_FirstMatch1_C,v3)]" +
						 "[xattrp(%!/Test_Xattrp_FirstMatch1_*,1,2)]",
		"TEST_XATTRP_FIRSTMATCH1_A TEST_XATTRP_FIRSTMATCH1_B")]
	[Arguments("[attrib_set(%!/Test_Xattrp_StartExceeds1_A,v1)]" +
						 "[attrib_set(%!/Test_Xattrp_StartExceeds1_B,v2)]" +
						 "[attrib_set(%!/Test_Xattrp_StartExceeds1_C,v3)]" +
						 "[xattrp(%!/Test_Xattrp_StartExceeds1_*,5,2)]",
		"")]
	[Arguments("[xattrp(%!/Test_Xattrp_ArgRangeStart1_*,0,2)]", ErrorMessages.Returns.ArgRange)]
	[Arguments("[xattrp(%!/Test_Xattrp_ArgRangeCount1_*,1,0)]", ErrorMessages.Returns.ArgRange)]
	[Arguments("[xattrp(%!/Test_Xattrp_NonInteger1_*,x,2)]", ErrorMessages.Returns.Integer)]
	public async Task Test_Xattrp_RangeAndErrors(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[regxattr(%!/Test_Regxattr_CountZero1_\\[0-9\\]+,1,0)]", ErrorMessages.Returns.ArgRange)]
	public async Task Test_Regxattr_CountZero(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[regxattrp(%!/Test_Regxattrp_CountZero1_\\[0-9\\]+,1,0)]", ErrorMessages.Returns.ArgRange)]
	public async Task Test_Regxattrp_CountZero(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("zfun(TEST_ATTR)", "#-1 NO ZONE SET")]
	public async Task Test_Zfun_NoZoneSet(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/Regrep_Unique_Attr_A1,hello_world)][attrib_set(%!/Regrep_Unique_Attr_A2,goodbye)][regrep(%!,Regrep_Unique_Attr_A*,hello)]",
		"REGREP_UNIQUE_ATTR_A1")]
	[Arguments("[attrib_set(%!/Regrep_Unique_Attr_B1,match_prefix_value)][attrib_set(%!/Regrep_Unique_Attr_B2,no_match)][regrep(%!,Regrep_Unique_Attr_B*,match_prefix)]",
		"REGREP_UNIQUE_ATTR_B1")]
	public async Task Regrep(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/Regrepi_Unique_Attr_C1,HELLO_WORLD)][attrib_set(%!/Regrepi_Unique_Attr_C2,goodbye)][regrepi(%!,Regrepi_Unique_Attr_C*,hello)]",
		"REGREPI_UNIQUE_ATTR_C1")]
	[Arguments("[attrib_set(%!/Regrepi_Unique_Attr_D1,MixedCase_Value)][attrib_set(%!/Regrepi_Unique_Attr_D2,other)][regrepi(%!,Regrepi_Unique_Attr_D*,mixedcase)]",
		"REGREPI_UNIQUE_ATTR_D1")]
	public async Task Regrepi(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("regedit(hello world,hello,goodbye)", "goodbye world")]
	[Arguments("regedit(test_value,value,replacement)", "test_replacement")]
	[Arguments("regedit(aaa,a,b)", "baa")]
	public async Task Regedit(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("xattr(#0,attr)", "")]
	public async Task Xattr(string str, string expected)
	{
		TestDiagnostics.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/PGREP_CHILD,child_value)][pgrep(%!,PGREP_*,child)]", "PGREP_CHILD")]
	[Arguments(
		"[setq(0,create(AttrFuncTest_Pgrep_ChildObj_ParentInherit))][setq(1,parent(%q0,create(AttrFuncTest_Pgrep_ParentObj_ParentInherit)))]" +
		"[attrib_set(%q1/PGREP_PARENTINHERIT_ATTR,child_value)]" +
		"[pgrep(%q0,PGREP_PARENTINHERIT_*,child)]",
		"PGREP_PARENTINHERIT_ATTR")]
	public async Task Test_Pgrep_IncludesParents(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments(
		"[setq(0,create(AttrFuncTest_Reglattrp_ChildObj_ParentInherit))][setq(1,parent(%q0,create(AttrFuncTest_Reglattrp_ParentObj_ParentInherit)))]" +
		"[attrib_set(%q0/REGLATTRP_PARENTINHERIT_001,value1)]" +
		"[attrib_set(%q1/REGLATTRP_PARENTINHERIT_002,value2)]" +
		"[attrib_set(%q0/REGLATTRP_PARENTINHERIT_100,value3)]" +
		"[reglattrp(%q0/^REGLATTRP_PARENTINHERIT_)]",
		"REGLATTRP_PARENTINHERIT_001 REGLATTRP_PARENTINHERIT_002 REGLATTRP_PARENTINHERIT_100")]
	public async Task Test_Reglattrp_IncludesParents(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments(
		"[setq(0,create(AttrFuncTest_Regnattrp_ChildObj_ParentInherit))][setq(1,parent(%q0,create(AttrFuncTest_Regnattrp_ParentObj_ParentInherit)))]" +
		"[attrib_set(%q0/REGNATTRP_PARENTINHERIT_001,value1)]" +
		"[attrib_set(%q1/REGNATTRP_PARENTINHERIT_002,value2)]" +
		"[attrib_set(%q0/REGNATTRP_PARENTINHERIT_100,value3)]" +
		"[regnattrp(%q0/^REGNATTRP_PARENTINHERIT_)]",
		"3")]
	public async Task Test_Regnattrp_CountWithParents(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/Test_Regxattrp_RangeWithParents_001,value1)]" +
						 "[attrib_set(%!/Test_Regxattrp_RangeWithParents_002,value2)]" +
						 "[attrib_set(%!/Test_Regxattrp_RangeWithParents_100,value3)]" +
						 "[regxattrp(%!/Test_Regxattrp_RangeWithParents_\\[0-9\\]+,1,2)]",
		"TEST_REGXATTRP_RANGEWITHPARENTS_001 TEST_REGXATTRP_RANGEWITHPARENTS_002")]
	[Arguments(
		"[setq(0,create(AttrFuncTest_Regxattrp_ChildObj_ParentInherit))][setq(1,parent(%q0,create(AttrFuncTest_Regxattrp_ParentObj_ParentInherit)))]" +
		"[attrib_set(%q0/REGXATTRP_PARENTINHERIT_001,value1)]" +
		"[attrib_set(%q1/REGXATTRP_PARENTINHERIT_002,value2)]" +
		"[attrib_set(%q0/REGXATTRP_PARENTINHERIT_100,value3)]" +
		"[regxattrp(%q0/^REGXATTRP_PARENTINHERIT_,1,2)]",
		"REGXATTRP_PARENTINHERIT_001 REGXATTRP_PARENTINHERIT_002")]
	public async Task Test_Regxattrp_RangeWithParents(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

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
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
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
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
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
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
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
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
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
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
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
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/Test_Basic_AttribSet_And_Get,testvalue)]" +
						 "[get(%!/Test_Basic_AttribSet_And_Get)]", "testvalue")]
	[Arguments("[attrib_set(%!/Test_Basic_AttribSet_And_Get21,val1)]" +
						 "[attrib_set(%!/Test_Basic_AttribSet_And_Get22,val2)]" +
						 "[get(%!/Test_Basic_AttribSet_And_Get21)][get(%!/Test_Basic_AttribSet_And_Get22)]", "val1val2")]
	public async Task Test_Basic_AttribSet_And_Get(string str, string expected)
	{
		var result = await Parser.FunctionParse(MarkupText.Plain(str));
		await Assert.That(result!.Message!.ToString()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/Test_Lattr_Simple1,v1)]" +
						 "[attrib_set(%!/Test_Lattr_Simple2,v2)]" +
						 "[lattr(%!/Test_Lattr_Simple*)]",
		"TEST_LATTR_SIMPLE1 TEST_LATTR_SIMPLE2")]
	public async Task Test_Lattr_Simple(string str, string expected)
	{
		var result = await Parser.FunctionParse(MarkupText.Plain(str));
		await Assert.That(result!.Message!.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("xattrp(#0,attr)", "0")]
	public async Task Xattrp(string str, string expected)
	{
		TestDiagnostics.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("xcon(#0)", "")]
	public async Task Xcon(string str, string expected)
	{
		TestDiagnostics.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("xexits(#0)", "")]
	public async Task Xexits(string str, string expected)
	{
		TestDiagnostics.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("xmwhoid()", "")]
	public async Task Xmwhoid(string str, string expected)
	{
		TestDiagnostics.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("xplayers(#0)", "")]
	public async Task Xplayers(string str, string expected)
	{
		TestDiagnostics.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("xthings(#0)", "")]
	public async Task Xthings(string str, string expected)
	{
		TestDiagnostics.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("xvcon(#0)", "")]
	public async Task Xvcon(string str, string expected)
	{
		TestDiagnostics.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("xvexits(#0)", "")]
	public async Task Xvexits(string str, string expected)
	{
		TestDiagnostics.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("xvplayers(#0)", "")]
	public async Task Xvplayers(string str, string expected)
	{
		TestDiagnostics.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("xvthings(#0)", "")]
	public async Task Xvthings(string str, string expected)
	{
		TestDiagnostics.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("xwho()", "")]
	public async Task Xwho(string str, string expected)
	{
		TestDiagnostics.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("xwhoid()", "")]
	public async Task Xwhoid(string str, string expected)
	{
		TestDiagnostics.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("valid(name,TestName)", "1")]
	[Arguments("valid(name,)", "0")]
	public async Task Valid_Name(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("valid(attrvalue,test_value)", "1")]
	[Arguments("valid(attrvalue,test_value,NONEXISTENT_ATTR)", "1")]
	public async Task Valid_AttributeValue(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
	[Arguments("[attrib_set(%!/Test_V_AttrName,groupsvalue)][v(Test_V_AttrName)]", "groupsvalue")]
	[Arguments("[attrib_set(%!/Test_V_AttrName2,hello world)][v(Test_V_AttrName2)]", "hello world")]
	public async Task Test_V_AttributeName(string str, string expected)
	{
		var result = await Parser.FunctionParse(MarkupText.Plain(str));
		await Assert.That(result!.Message!.ToString()).IsEqualTo(expected);
	}

	/// <summary>
	/// Direct regression test for a bug found while implementing @CLONE's creator preservation
	/// (Task 2, fix round 1, M2): <c>owner(obj/attr)</c> (<see cref="AttributeFunctions.Owner"/>,
	/// <c>AttributeFunctions.cs:829</c>) called
	/// <c>GetAttributeAsync(executor, executor, attribute, ...)</c> - passing <c>executor</c> for
	/// BOTH the executor and the object argument, discarding the object <c>LocateService</c> had
	/// just resolved from the <c>obj</c> half of <c>obj/attr</c>. <c>owner(otherObj/attr)</c>
	/// therefore always read <c>attr</c> off the calling player, never off <c>otherObj</c>. No
	/// prior test exercised <c>owner()</c> with an attribute argument at all.
	/// </summary>
	[Test]
	public async Task Owner_AttributeArg_ReadsFromLocatedObject_NotExecutor()
	{
		var uid = TestIsolationHelpers.GenerateUniqueName("OWN");

		var createResult = await Parser.FunctionParse(MarkupText.Plain($"create(OwnerTarget_{uid})"));
		var otherObj = createResult!.Message!.ToPlainText();

		await Parser.FunctionParse(MarkupText.Plain($"[attrib_set({otherObj}/OW{uid},val_{uid})]"));

		// Positive controls: the attribute genuinely exists on the OTHER object, and NOT on the
		// executor (self) - otherwise a buggy owner() that reads off the executor instead of the
		// located object could coincidentally still appear to pass.
		var hasAttrOther = await Parser.FunctionParse(MarkupText.Plain($"hasattr({otherObj},OW{uid})"));
		await Assert.That(hasAttrOther!.Message!.ToPlainText()).IsEqualTo("1")
			.Because("the attribute must actually exist on the OTHER object for this test to mean anything");
		var hasAttrSelf = await Parser.FunctionParse(MarkupText.Plain($"hasattr(%!,OW{uid})"));
		await Assert.That(hasAttrSelf!.Message!.ToPlainText()).IsEqualTo("0")
			.Because("the executor must NOT carry this attribute name, so a buggy owner() reading off the executor would report NO SUCH ATTRIBUTE rather than coincidentally succeeding");

		var ownerResult = await Parser.FunctionParse(MarkupText.Plain($"owner({otherObj}/OW{uid})"));
		await Assert.That(ownerResult!.Message!.ToPlainText())
			.IsEqualTo($"#{WebAppFactoryArg.ExecutorDBRef.Number}")
			.Because("owner(obj/attr) must resolve the attribute on the LOCATED object (obj), not the calling executor - red before the fix, since the located object argument was discarded in favour of executor");
	}

	/// <summary>
	/// <c>fun_hasattr</c> takes the <c>&lt;object&gt;/&lt;attribute&gt;</c> pair in one argument when
	/// called with one (<c>src/fundb.c:222-231</c>), and answers
	/// <c>#-1 BAD ARGUMENT FORMAT TO &lt;called_as&gt;</c> when that argument carries no slash. All
	/// four names shared a body that read <c>ArgumentsOrdered["1"]</c> unconditionally, so the
	/// one-argument form was an arity error.
	/// </summary>
	[Test]
	[Arguments("hasattr", "1")]
	[Arguments("hasattrp", "1")]
	[Arguments("hasattrval", "1")]
	[Arguments("hasattrpval", "1")]
	public async Task HasattrTakesTheObjectAndAttributeInOneArgument(string function, string expected)
	{
		var result = await Parser.FunctionParse(MarkupText.Plain(
			$"[attrib_set(me/HASATTRONEARG,value)][{function}(me/HASATTRONEARG)]"));

		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("hasattr")]
	[Arguments("hasattrp")]
	[Arguments("hasattrval")]
	[Arguments("hasattrpval")]
	public async Task HasattrWithoutASlashIsABadArgumentFormat(string function)
	{
		var result = await Parser.FunctionParse(MarkupText.Plain($"{function}(me)"));

		await Assert.That(result!.Message!.ToPlainText())
			.IsEqualTo($"#-1 BAD ARGUMENT FORMAT TO {function.ToUpperInvariant()}");
	}

	/// <summary>
	/// PennMUSH's <c>empty_attrs</c> defaults on (<c>src/conf.c:1216</c>, <c>game/mushcnf.dst:779</c>),
	/// and a value of exactly one space counts as a value only while it is on — that single space
	/// being how an attribute set to nothing is stored when it is off (<c>src/fundb.c:245-250</c>).
	/// The two places SharpMUSH writes the default down disagreed: the PennMUSH config importer said
	/// true, <c>OptionsService</c> said false, so the same game answered differently depending on
	/// whether it had ever read a <c>mush.cnf</c>.
	/// </summary>
	[Test]
	public async Task HasattrvalCountsASingleSpaceOnlyWhileEmptyAttrsIsOn()
	{
		await Parser.FunctionParse(MarkupText.Plain("attrib_set(me/HASATTRVALSPACE,%b)"));

		await Assert.That((await Parser.FunctionParse(MarkupText.Plain("strlen(get(me/HASATTRVALSPACE))")))!
			.Message!.ToPlainText()).IsEqualTo("1").Because("the fixture must hold exactly one space");

		using (TestOptionsOverride.Scope(o => o with { Attribute = o.Attribute with { EmptyAttributes = true } }))
		{
			var on = await Parser.FunctionParse(MarkupText.Plain("hasattrval(me/HASATTRVALSPACE)"));
			await Assert.That(on!.Message!.ToPlainText()).IsEqualTo("1");
		}

		using (TestOptionsOverride.Scope(o => o with { Attribute = o.Attribute with { EmptyAttributes = false } }))
		{
			var off = await Parser.FunctionParse(MarkupText.Plain("hasattrval(me/HASATTRVALSPACE)"));
			await Assert.That(off!.Message!.ToPlainText()).IsEqualTo("0");
		}
	}

	/// <summary>
	/// An attribute the caller may not read is <c>e_perm</c>, not "no" (<c>src/fundb.c:243-256</c>),
	/// and the one-argument and two-argument spellings have to say the same thing — they are the same
	/// <c>fun_hasattr</c> reached two ways.
	/// </summary>
	/// <remarks>
	/// Driven as a MORTAL on purpose. The fixtures run as God, who may examine anything, so the
	/// refusal branch is unreachable from <see cref="HasattrTakesTheObjectAndAttributeInOneArgument"/>
	/// and every one of these four names would answer <c>1</c> there whatever the branch did.
	/// Reporting <c>0</c> instead would tell a mortal the attribute is not there, which is a
	/// different and wrong answer.
	/// </remarks>
	[Test]
	[Arguments("hasattr")]
	[Arguments("hasattrp")]
	[Arguments("hasattrval")]
	[Arguments("hasattrpval")]
	public async Task HasattrRefusesAnUnreadableAttributeIdenticallyInBothForms(string function)
	{
		await Parser.FunctionParse(MarkupText.Plain("attrib_set(me/HASATTRUNREADABLE,hidden)"));

		var god = WebAppFactoryArg.ExecutorDBRef.Number;
		var mortal = WebAppFactoryArg.FunctionParserFor(await MintMortalAsync("HasAttrM"));

		var oneArgument = await mortal.FunctionParse(MarkupText.Plain($"{function}(#{god}/HASATTRUNREADABLE)"));
		var twoArguments = await mortal.FunctionParse(MarkupText.Plain($"{function}(#{god},HASATTRUNREADABLE)"));

		await Assert.That(oneArgument!.Message!.ToPlainText()).IsEqualTo(ErrorMessages.Returns.AttrPermissions)
			.Because("an attribute that exists but cannot be read is a refusal, not an absence");
		await Assert.That(twoArguments!.Message!.ToPlainText()).IsEqualTo(oneArgument.Message!.ToPlainText())
			.Because("obj/attr in one argument and obj,attr in two are the same call");
	}

	/// <summary>
	/// The <c>P</c> forms walk the parent in the one-argument spelling too — the switch comes out of
	/// <c>called_as</c> (<c>strchr(called_as, 'P')</c>, <c>src/fundb.c:215-260</c>) and has nothing
	/// to do with how many arguments arrived. The two-argument parent case is pinned in
	/// <c>AttributeTreeParentPermissionTests.Parent_HasattrInherited</c>; this is the other spelling.
	/// </summary>
	[Test]
	[Arguments("hasattr", "0")]
	[Arguments("hasattrval", "0")]
	[Arguments("hasattrp", "1")]
	[Arguments("hasattrpval", "1")]
	public async Task HasattrpFollowsTheParentInTheOneArgumentForm(string function, string expected)
	{
		var attribute = $"HAP{Guid.NewGuid():N}"[..11].ToUpperInvariant();
		var parent = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "HasAttrPP");
		var child = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "HasAttrPC");

		await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&{attribute} {parent}=inherited"));
		await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@parent {child}={parent}"));

		var result = await Parser.FunctionParse(MarkupText.Plain($"{function}({child}/{attribute})"));

		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>
	/// What the <c>VAL</c> forms count as a value: only the empty string is empty, plus a value of
	/// exactly one space while <c>empty_attrs</c> is off (<c>src/fundb.c:245-250</c>). Two spaces and
	/// a tab are values under both settings — a blanket whitespace test answers 0 for them, which is
	/// the mistake this exists to catch, and the lone-space case alone cannot catch it.
	/// </summary>
	/// <remarks>
	/// The tab is written through <see cref="IAttributeService"/> rather than as softcode because
	/// nothing in the MUSH-code path can produce one: <c>%t</c> and <c>chr(9)</c> both evaluate to
	/// zero characters here, and a literal tab inside an argument is eaten by the lexer. That is a
	/// separate defect in the substitutions, which this lane does not own.
	/// </remarks>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task HasattrvalCountsTwoSpacesAndATabAsValuesUnderEitherSetting(bool emptyAttributes)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(WebAppFactoryArg.ExecutorDBRef))).Expect<AnySharpObject>();
		await AttributeService.SetAttributeAsync(god, god, "HASATTRVALTAB", MarkupText.Plain("\t"));

		await Parser.FunctionParse(MarkupText.Plain("attrib_set(me/HASATTRVALTWOSPACES,%b%b)"));
		await Parser.FunctionParse(MarkupText.Plain("attrib_set(me/HASATTRVALEMPTY,)"));

		using (TestOptionsOverride.Scope(o => o with { Attribute = o.Attribute with { EmptyAttributes = emptyAttributes } }))
		{
			await Assert.That((await Parser.FunctionParse(MarkupText.Plain("hasattrval(me/HASATTRVALTWOSPACES)")))!
				.Message!.ToPlainText()).IsEqualTo("1").Because("two spaces are a value under either setting");
			await Assert.That((await Parser.FunctionParse(MarkupText.Plain("hasattrval(me/HASATTRVALTAB)")))!
				.Message!.ToPlainText()).IsEqualTo("1").Because("a tab is a value under either setting");
			await Assert.That((await Parser.FunctionParse(MarkupText.Plain("hasattr(me/HASATTRVALEMPTY)")))!
				.Message!.ToPlainText()).IsEqualTo("1").Because("the attribute is there; it just holds nothing");
			await Assert.That((await Parser.FunctionParse(MarkupText.Plain("hasattrval(me/HASATTRVALEMPTY)")))!
				.Message!.ToPlainText()).IsEqualTo("0").Because("the empty string is the one value that is not one");
		}
	}

	private async Task<DBRef> MintMortalAsync(string prefix)
	{
		var name = $"{prefix}{Guid.NewGuid():N}"[..14];
		var created = await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@pcreate {name}=pw_{name}"));
		return DBRef.Parse(created.Message!.ToPlainText()!);
	}

	/// <summary>The shipped default has to be the one PennMUSH ships, in both places that spell it.</summary>
	[Test]
	public async Task EmptyAttrsDefaultsOnEverywhereItIsWrittenDown()
	{
		await Assert.That(ReadPennMushConfig.Create(EmptyConfigFile()).Attribute.EmptyAttributes).IsTrue();
		await Assert.That(OptionsService.Default().Attribute.EmptyAttributes).IsTrue();
	}

	private static string EmptyConfigFile()
	{
		var path = Path.Combine(Path.GetTempPath(), $"sharpmush-empty-attrs-{Guid.NewGuid():N}.cnf");
		File.WriteAllText(path, string.Empty);
		return path;
	}
}
