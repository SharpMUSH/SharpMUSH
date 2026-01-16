using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// Tests for lock string normalization - converting bare dbrefs to objids.
/// </summary>
public class LockNormalizationTests
{
	[ClassDataSource<TestClassFactory>(Shared = SharedType.PerClass)]
	public required TestClassFactory Factory { get; init; }

	private IBooleanExpressionParser BooleanParser => Factory.Services.GetRequiredService<IBooleanExpressionParser>();	
	private ISharpDatabase Database => Factory.Services.GetRequiredService<ISharpDatabase>();
	private IMUSHCodeParser Parser => Factory.FunctionParser;

	[Test]
	public async Task Normalize_ExactObjectLock_BareDbRef_ConvertsToObjId()
	{
		// Create a test object
		var createResult = (await Parser.FunctionParse(MModule.single("create(NormTestObj1)")))?.Message!;
		var testObjDbRefStr = createResult.ToPlainText();
		var testObjDbRef = HelperFunctions.ParseDbRef(testObjDbRefStr).AsValue();
		
		// Create a lock with a bare dbref
		var lockString = $"=#{testObjDbRef.Number}";
		
		// Normalize should convert it to objid
		var normalized = BooleanParser.Normalize(lockString);
		
		// Should contain the objid format with creation time
		await Assert.That(normalized).Contains($"#{testObjDbRef.Number}:{testObjDbRef.CreationMilliseconds}");
	}

	[Test]
	public async Task Normalize_ExactObjectLock_ObjId_RemainsUnchanged()
	{
		// Create a test object
		var createResult = (await Parser.FunctionParse(MModule.single("create(NormTestObjId1)")))?.Message!;
		var testObjDbRefStr = createResult.ToPlainText();
		var testObjDbRef = HelperFunctions.ParseDbRef(testObjDbRefStr).AsValue();
		
		// Create a lock with full objid
		var lockString = $"=#{testObjDbRef.Number}:{testObjDbRef.CreationMilliseconds}";
		
		// Normalize should leave it unchanged
		var normalized = BooleanParser.Normalize(lockString);
		
		// Should be identical
		await Assert.That(normalized).IsEqualTo(lockString);
	}

	[Test]
	public async Task Normalize_ComplexLock_NormalizesAllDbrefs()
	{
		// Create two test objects
		var createResult1 = (await Parser.FunctionParse(MModule.single("create(NormTestComplex1)")))?.Message!;
		var testObjDbRefStr1 = createResult1.ToPlainText();
		var testObjDbRef1 = HelperFunctions.ParseDbRef(testObjDbRefStr1).AsValue();
		
		var createResult2 = (await Parser.FunctionParse(MModule.single("create(NormTestComplex2)")))?.Message!;
		var testObjDbRefStr2 = createResult2.ToPlainText();
		var testObjDbRef2 = HelperFunctions.ParseDbRef(testObjDbRefStr2).AsValue();
		
		// Create a complex lock with multiple bare dbrefs
		var lockString = $"=#{testObjDbRef1.Number} | +#{testObjDbRef2.Number}";
		
		// Normalize should convert both to objids
		var normalized = BooleanParser.Normalize(lockString);
		
		// Should contain both objids
		await Assert.That(normalized).Contains($"#{testObjDbRef1.Number}:{testObjDbRef1.CreationMilliseconds}");
		await Assert.That(normalized).Contains($"#{testObjDbRef2.Number}:{testObjDbRef2.CreationMilliseconds}");
	}

	[Test]
	public async Task Normalize_NonDbrefLock_RemainsUnchanged()
	{
		// Lock with no dbrefs
		var lockString = "flag^WIZARD";
		
		// Normalize should leave it unchanged
		var normalized = BooleanParser.Normalize(lockString);
		
		// Should be identical
		await Assert.That(normalized).IsEqualTo(lockString);
	}

	[Test]
	public async Task Normalize_NameLock_RemainsUnchanged()
	{
		// Lock with name pattern
		var lockString = "name^Test*";
		
		// Normalize should leave it unchanged
		var normalized = BooleanParser.Normalize(lockString);
		
		// Should be identical
		await Assert.That(normalized).IsEqualTo(lockString);
	}
}
