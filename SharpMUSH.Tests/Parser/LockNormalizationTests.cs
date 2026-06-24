using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// Tests for lock string normalization.
/// PennMUSH preserves bare dbrefs in lock() readback — normalization does NOT expand to objids.
/// </summary>
public class LockNormalizationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IBooleanExpressionParser BooleanParser => WebAppFactoryArg.Services.GetRequiredService<IBooleanExpressionParser>();
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	public async Task Normalize_ExactObjectLock_BareDbRef_PreservedAsIs()
	{
		var createResult = (await Parser.FunctionParse(MModule.single("create(NormTestObj1)")))?.Message!;
		var testObjDbRefStr = createResult.ToPlainText();
		var testObjDbRef = HelperFunctions.ParseDbRef(testObjDbRefStr).AsValue();
		var testObj = (await Database.GetObjectNodeAsync(testObjDbRef)).Known();
		var testObjFullDbRef = testObj.Object().DBRef;

		var lockString = $"=#{testObjFullDbRef.Number}";

		// Normalize preserves bare dbrefs (matches PennMUSH lock() readback)
		var normalized = BooleanParser.Normalize(lockString);

		await Assert.That(normalized).IsEqualTo($"=#{testObjFullDbRef.Number}");
	}

	[Test]
	public async Task Normalize_ExactObjectLock_ObjId_PreservesObjId()
	{
		var createResult = (await Parser.FunctionParse(MModule.single("create(NormTestObjId1)")))?.Message!;
		var testObjDbRefStr = createResult.ToPlainText();
		var testObjDbRef = HelperFunctions.ParseDbRef(testObjDbRefStr).AsValue();
		var testObj = (await Database.GetObjectNodeAsync(testObjDbRef)).Known();
		var testObjFullDbRef = testObj.Object().DBRef;

		var lockString = $"=#{testObjFullDbRef.Number}:{testObjFullDbRef.CreationMilliseconds}";

		var normalized = BooleanParser.Normalize(lockString);

		await Assert.That(normalized).IsEqualTo(lockString);
	}

	[Test]
	public async Task Normalize_ComplexLock_PreservesBareDbrefs()
	{
		var createResult1 = (await Parser.FunctionParse(MModule.single("create(NormTestComplex1)")))?.Message!;
		var testObjDbRefStr1 = createResult1.ToPlainText();
		var testObjDbRef1 = HelperFunctions.ParseDbRef(testObjDbRefStr1).AsValue();
		var testObj1 = (await Database.GetObjectNodeAsync(testObjDbRef1)).Known();
		var testObjFullDbRef1 = testObj1.Object().DBRef;

		var createResult2 = (await Parser.FunctionParse(MModule.single("create(NormTestComplex2)")))?.Message!;
		var testObjDbRefStr2 = createResult2.ToPlainText();
		var testObjDbRef2 = HelperFunctions.ParseDbRef(testObjDbRefStr2).AsValue();
		var testObj2 = (await Database.GetObjectNodeAsync(testObjDbRef2)).Known();
		var testObjFullDbRef2 = testObj2.Object().DBRef;

		var lockString = $"=#{testObjFullDbRef1.Number} | +#{testObjFullDbRef2.Number}";

		var normalized = BooleanParser.Normalize(lockString);

		await Assert.That(normalized).IsEqualTo($"=#{testObjFullDbRef1.Number} | +#{testObjFullDbRef2.Number}");
	}

	[Test]
	public async Task Normalize_NonDbrefLock_RemainsUnchanged()
	{
		var lockString = "FLAG^WIZARD";

		var normalized = BooleanParser.Normalize(lockString);

		await Assert.That(normalized).IsEqualTo(lockString);
	}

	[Test]
	public async Task Normalize_NameLock_RemainsUnchanged()
	{
		var lockString = "NAME^TEST*";

		var normalized = BooleanParser.Normalize(lockString);

		await Assert.That(normalized).IsEqualTo(lockString);
	}
}
