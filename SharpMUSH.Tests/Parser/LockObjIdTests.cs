using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// Tests for lock keys with object IDs (objids) to ensure PennMUSH compatibility.
/// Tests verify that locks properly handle DBRefs with creation timestamps to prevent
/// issues when objects are destroyed and their dbrefs are recycled.
/// </summary>
public class LockObjIdTests : TestsBase
{
	private IBooleanExpressionParser BooleanParser => Services.GetRequiredService<IBooleanExpressionParser>();	
	private ISharpDatabase Database => Services.GetRequiredService<ISharpDatabase>();
	private IMUSHCodeParser Parser => FunctionParser;

	[Test]
	public async Task ExactObjectLock_BareDbRef_MatchesAnyObjectWithSameNumber()
	{
		var createResult = (await Parser.FunctionParse(MModule.single("create(LockTestObj1)")))?.Message!;
		var testObjDbRefStr = createResult.ToPlainText();
		var testObjDbRef = HelperFunctions.ParseDbRef(testObjDbRefStr).AsValue();
		var testObj = (await Database.GetObjectNodeAsync(testObjDbRef)).Known();
		
		// Lock string with bare dbref (no creation time)
		var lockString = $"=#{testObjDbRef.Number}";
		
		var bep = BooleanParser;
		var god = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();
		
		// Should validate
		await Assert.That(bep.Validate(lockString, god)).IsTrue();
		
		// Should match the object (ignoring creation time since lock doesn't specify it)
		await Assert.That(bep.Compile(lockString)(god, testObj)).IsTrue();
	}

	[Test]
	public async Task ExactObjectLock_ObjId_MatchesOnlyObjectWithSameNumberAndCreationTime()
	{
		// Create a test object using the create() function
		var createResult = (await Parser.FunctionParse(MModule.single("create(LockTestObjId1)")))?.Message!;
		var testObjDbRefStr = createResult.ToPlainText();
		var testObjDbRef = HelperFunctions.ParseDbRef(testObjDbRefStr).AsValue();
		var testObj = (await Database.GetObjectNodeAsync(testObjDbRef)).Known();
		
		// Lock string with full objid (dbref:creationtime)
		var lockString = $"=#{testObjDbRef.Number}:{testObjDbRef.CreationMilliseconds}";
		
		var bep = BooleanParser;
		var god = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();
		
		// Should validate
		await Assert.That(bep.Validate(lockString, god)).IsTrue();
		
		// Should match the object (both number and creation time match)
		await Assert.That(bep.Compile(lockString)(god, testObj)).IsTrue();
	}

	[Test]
	public async Task ExactObjectLock_ObjId_DoesNotMatchObjectWithDifferentCreationTime()
	{
		// Create a test object using the create() function
		var createResult = (await Parser.FunctionParse(MModule.single("create(LockTestObjId2)")))?.Message!;
		var testObjDbRefStr = createResult.ToPlainText();
		var testObjDbRef = HelperFunctions.ParseDbRef(testObjDbRefStr).AsValue();
		var testObj = (await Database.GetObjectNodeAsync(testObjDbRef)).Known();
		
		// Lock string with full objid but DIFFERENT creation time
		var differentCreationTime = (testObjDbRef.CreationMilliseconds ?? 0) + 1000;
		var lockString = $"=#{testObjDbRef.Number}:{differentCreationTime}";
		
		var bep = BooleanParser;
		var god = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();
		
		// Should validate
		await Assert.That(bep.Validate(lockString, god)).IsTrue();
		
		// Should NOT match the object (creation time differs)
		await Assert.That(bep.Compile(lockString)(god, testObj)).IsFalse();
	}

	[Test]
	public async Task DbRefListLock_BareDbRef_MatchesAnyObjectWithSameNumber()
	{
		// Create a test object
		var createResult = (await Parser.FunctionParse(MModule.single("create(LockTestListObj1)")))?.Message!;
		var testObjDbRefStr = createResult.ToPlainText();
		var testObjDbRef = HelperFunctions.ParseDbRef(testObjDbRefStr).AsValue();
		var testObj = (await Database.GetObjectNodeAsync(testObjDbRef)).Known();
		
		// Create an object to hold the dbref list
		var lockHolderResult = (await Parser.FunctionParse(MModule.single("create(LockHolder1)")))?.Message!;
		var lockHolderDbRefStr = lockHolderResult.ToPlainText();
		var lockHolderDbRef = HelperFunctions.ParseDbRef(lockHolderDbRefStr).AsValue();
		var lockHolder = (await Database.GetObjectNodeAsync(lockHolderDbRef)).Known();
		
		// Set an attribute with a bare dbref  
		await Parser.FunctionParse(MModule.single($"attrib_set({lockHolderDbRefStr}/allowedlist,#{testObjDbRef.Number})"));
		
		var lockString = "dbreflist^allowedlist";
		
		var bep = BooleanParser;
		
		// Should validate
		await Assert.That(bep.Validate(lockString, lockHolder)).IsTrue();
		
		// Should match the object (ignoring creation time since list has bare dbref)
		await Assert.That(bep.Compile(lockString)(lockHolder, testObj)).IsTrue();
	}

	[Test]
	public async Task DbRefListLock_ObjId_MatchesOnlyObjectWithSameNumberAndCreationTime()
	{
		// Create a test object
		var createResult = (await Parser.FunctionParse(MModule.single("create(LockTestListObjId1)")))?.Message!;
		var testObjDbRefStr = createResult.ToPlainText();
		var testObjDbRef = HelperFunctions.ParseDbRef(testObjDbRefStr).AsValue();
		var testObj = (await Database.GetObjectNodeAsync(testObjDbRef)).Known();
		
		// Create an object to hold the dbref list
		var lockHolderResult = (await Parser.FunctionParse(MModule.single("create(LockHolderObjId1)")))?.Message!;
		var lockHolderDbRefStr = lockHolderResult.ToPlainText();
		var lockHolderDbRef = HelperFunctions.ParseDbRef(lockHolderDbRefStr).AsValue();
		var lockHolder = (await Database.GetObjectNodeAsync(lockHolderDbRef)).Known();
		
		// Set an attribute with a full objid
		await Parser.FunctionParse(MModule.single($"attrib_set({lockHolderDbRefStr}/allowedlistobjid,#{testObjDbRef.Number}:{testObjDbRef.CreationMilliseconds})"));
		
		var lockString = "dbreflist^allowedlistobjid";
		
		var bep = BooleanParser;
		
		// Should validate
		await Assert.That(bep.Validate(lockString, lockHolder)).IsTrue();
		
		// Should match the object (both number and creation time match)
		await Assert.That(bep.Compile(lockString)(lockHolder, testObj)).IsTrue();
	}

	[Test]
	public async Task DbRefListLock_ObjId_DoesNotMatchObjectWithDifferentCreationTime()
	{
		// Create a test object
		var createResult = (await Parser.FunctionParse(MModule.single("create(LockTestListObjId2)")))?.Message!;
		var testObjDbRefStr = createResult.ToPlainText();
		var testObjDbRef = HelperFunctions.ParseDbRef(testObjDbRefStr).AsValue();
		var testObj = (await Database.GetObjectNodeAsync(testObjDbRef)).Known();
		
		// Create an object to hold the dbref list
		var lockHolderResult = (await Parser.FunctionParse(MModule.single("create(LockHolderObjId2)")))?.Message!;
		var lockHolderDbRefStr = lockHolderResult.ToPlainText();
		var lockHolderDbRef = HelperFunctions.ParseDbRef(lockHolderDbRefStr).AsValue();
		var lockHolder = (await Database.GetObjectNodeAsync(lockHolderDbRef)).Known();
		
		// Set an attribute with objid but DIFFERENT creation time
		var differentCreationTime = (testObjDbRef.CreationMilliseconds ?? 0) + 1000;
		await Parser.FunctionParse(MModule.single($"attrib_set({lockHolderDbRefStr}/allowedlistdiff,#{testObjDbRef.Number}:{differentCreationTime})"));
		
		var lockString = "dbreflist^allowedlistdiff";
		
		var bep = BooleanParser;
		
		// Should validate
		await Assert.That(bep.Validate(lockString, lockHolder)).IsTrue();
		
		// Should NOT match the object (creation time differs)
		await Assert.That(bep.Compile(lockString)(lockHolder, testObj)).IsFalse();
	}

	[Test]
	public async Task DbRefListLock_MultipleObjIds_MatchesCorrectObject()
	{
		// Create two test objects
		var createResult1 = (await Parser.FunctionParse(MModule.single("create(LockTestMulti1)")))?.Message!;
		var testObjDbRefStr1 = createResult1.ToPlainText();
		var testObjDbRef1 = HelperFunctions.ParseDbRef(testObjDbRefStr1).AsValue();
		var testObj1 = (await Database.GetObjectNodeAsync(testObjDbRef1)).Known();
		
		var createResult2 = (await Parser.FunctionParse(MModule.single("create(LockTestMulti2)")))?.Message!;
		var testObjDbRefStr2 = createResult2.ToPlainText();
		var testObjDbRef2 = HelperFunctions.ParseDbRef(testObjDbRefStr2).AsValue();
		var testObj2 = (await Database.GetObjectNodeAsync(testObjDbRef2)).Known();
		
		// Create an object to hold the dbref list
		var lockHolderResult = (await Parser.FunctionParse(MModule.single("create(LockHolderMulti)")))?.Message!;
		var lockHolderDbRefStr = lockHolderResult.ToPlainText();
		var lockHolderDbRef = HelperFunctions.ParseDbRef(lockHolderDbRefStr).AsValue();
		var lockHolder = (await Database.GetObjectNodeAsync(lockHolderDbRef)).Known();
		
		// Set an attribute with multiple objids
		await Parser.FunctionParse(MModule.single($"attrib_set({lockHolderDbRefStr}/multilist,#{testObjDbRef1.Number}:{testObjDbRef1.CreationMilliseconds} #{testObjDbRef2.Number}:{testObjDbRef2.CreationMilliseconds})"));
		
		var lockString = "dbreflist^multilist";
		
		var bep = BooleanParser;
		
		// Should validate
		await Assert.That(bep.Validate(lockString, lockHolder)).IsTrue();
		
		// Both objects should match
		await Assert.That(bep.Compile(lockString)(lockHolder, testObj1)).IsTrue();
		await Assert.That(bep.Compile(lockString)(lockHolder, testObj2)).IsTrue();
		
		// God (#1) should not match
		var god = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();
		await Assert.That(bep.Compile(lockString)(lockHolder, god)).IsFalse();
	}
}
