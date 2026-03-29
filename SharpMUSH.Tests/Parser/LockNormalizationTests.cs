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
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IBooleanExpressionParser BooleanParser => WebAppFactoryArg.Services.GetRequiredService<IBooleanExpressionParser>();
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	public async Task Normalize_ExactObjectLock_BareDbRef_ConvertsToObjId()
	{
		// Create a test object
		var createResult = (await Parser.FunctionParse(MModule.single("create(NormTestObj1)")))?.Message!;
		var testObjDbRefStr = createResult.ToPlainText();
		var testObjDbRef = HelperFunctions.ParseDbRef(testObjDbRefStr).AsValue();
		// Get creation time from the database (create() returns bare #N; objid includes timestamp)
		var testObj = (await Database.GetObjectNodeAsync(testObjDbRef)).Known();
		var testObjFullDbRef = testObj.Object().DBRef;

		// Create a lock with a bare dbref
		var lockString = $"=#{testObjFullDbRef.Number}";

		// Normalize should convert it to objid
		var normalized = BooleanParser.Normalize(lockString);

		// Should contain the objid format with creation time
		await Assert.That(normalized).Contains($"#{testObjFullDbRef.Number}:{testObjFullDbRef.CreationMilliseconds}");
	}

	[Test]
	public async Task Normalize_ExactObjectLock_ObjId_RemainsUnchanged()
	{
		// Create a test object
		var createResult = (await Parser.FunctionParse(MModule.single("create(NormTestObjId1)")))?.Message!;
		var testObjDbRefStr = createResult.ToPlainText();
		var testObjDbRef = HelperFunctions.ParseDbRef(testObjDbRefStr).AsValue();
		// Get creation time from the database (create() returns bare #N; objid includes timestamp)
		var testObj = (await Database.GetObjectNodeAsync(testObjDbRef)).Known();
		var testObjFullDbRef = testObj.Object().DBRef;

		// Create a lock with full objid
		var lockString = $"=#{testObjFullDbRef.Number}:{testObjFullDbRef.CreationMilliseconds}";

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
		// Get creation times from database objects (create() returns bare #N; objid includes timestamp)
		var testObj1 = (await Database.GetObjectNodeAsync(testObjDbRef1)).Known();
		var testObjFullDbRef1 = testObj1.Object().DBRef;

		var createResult2 = (await Parser.FunctionParse(MModule.single("create(NormTestComplex2)")))?.Message!;
		var testObjDbRefStr2 = createResult2.ToPlainText();
		var testObjDbRef2 = HelperFunctions.ParseDbRef(testObjDbRefStr2).AsValue();
		var testObj2 = (await Database.GetObjectNodeAsync(testObjDbRef2)).Known();
		var testObjFullDbRef2 = testObj2.Object().DBRef;

		// Create a complex lock with multiple bare dbrefs
		var lockString = $"=#{testObjFullDbRef1.Number} | +#{testObjFullDbRef2.Number}";

		// Normalize should convert both to objids
		var normalized = BooleanParser.Normalize(lockString);

		// Should contain both objids
		await Assert.That(normalized).Contains($"#{testObjFullDbRef1.Number}:{testObjFullDbRef1.CreationMilliseconds}");
		await Assert.That(normalized).Contains($"#{testObjFullDbRef2.Number}:{testObjFullDbRef2.CreationMilliseconds}");
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
