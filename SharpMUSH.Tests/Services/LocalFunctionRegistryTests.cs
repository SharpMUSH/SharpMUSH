using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class LocalFunctionRegistryTests
{
	private static DBRef Ref(string value) => DBRef.Parse(value);
	private static UserDefinedFunction Entry(string name, DBRef? owner, DBRef target) => new(name, target, "CODE", 0, 32, true, null, Owner: owner);

	[Test]
	public async Task NamesAliasesAndMutationsRemainWithinFullOwnerScope()
	{
		var registry = new UserDefinedFunctionService();
		var first = Ref("#10:100"); var second = Ref("#10:200");
		registry.Define(Entry("same", first, Ref("#20:100")));
		registry.Define(Entry("same", second, Ref("#21:100")));
		registry.Define(Entry("same", null, Ref("#22:100")));
		await Assert.That(registry.Resolve("SAME", first)!.Object).IsEqualTo(Ref("#20:100"));
		await Assert.That(registry.Resolve("same", second)!.Object).IsEqualTo(Ref("#21:100"));
		await Assert.That(registry.Resolve("same")!.Object).IsEqualTo(Ref("#22:100"));
		await Assert.That(registry.Alias("alias", "same", first)).IsTrue();
		await Assert.That(registry.Resolve("alias", second)).IsNull();
		registry.SetEnabled("same", false, first);
		await Assert.That(registry.Resolve("alias", first)).IsNull();
		await Assert.That(registry.Resolve("same", second)).IsNotNull();
		registry.SetEnabled("same", true, first);
		registry.InvalidateLocalDefinitions(Ref("#20:100"));
		await Assert.That(registry.Resolve("same", first)).IsNull();
		await Assert.That(registry.Get("alias", first)).IsNull();
		await Assert.That(registry.Resolve("same")).IsNotNull();
	}

	[Test]
	public async Task PreserveAndResetAffectOnlySelectedOwnerAndDoNotPersistAcrossServices()
	{
		var registry = new UserDefinedFunctionService();
		var owner = Ref("#10:100"); var other = Ref("#11:100");
		registry.Define(Entry("kept", owner, Ref("#20:100")));
		registry.Define(Entry("removed", owner, Ref("#20:100")));
		registry.Define(Entry("removed", other, Ref("#21:100")));
		registry.SetPreserved("kept", true, owner);
		await Assert.That(registry.ResetUnpreserved(owner)).IsEqualTo(1);
		await Assert.That(registry.All(owner).Single().Name).IsEqualTo("kept");
		await Assert.That(registry.Resolve("removed", other)).IsNotNull();
		await Assert.That(new UserDefinedFunctionService().All(owner).Count).IsEqualTo(0);
		registry.InvalidateLocalDefinitions(owner);
		await Assert.That(registry.All(owner).Count).IsEqualTo(0);
	}
	[Test]
	public async Task AliasCleanupFollowsRedefinedTargetRatherThanItsCopiedObject()
	{
		var registry = new UserDefinedFunctionService();
		var owner = Ref("#10:100");
		registry.Define(Entry("source", owner, Ref("#20:100")));
		registry.Alias("alias", "source", owner);
		registry.Define(Entry("source", owner, Ref("#21:100")));
		registry.InvalidateLocalDefinitions(Ref("#20:100"));
		await Assert.That(registry.Resolve("alias", owner)!.Object).IsEqualTo(Ref("#21:100"));
		registry.InvalidateLocalDefinitions(Ref("#21:100"));
		await Assert.That(registry.Get("alias", owner)).IsNull();
	}

	[Test]
	public async Task ExistingGlobalAliasEnableStateRemainsIndependent()
	{
		var registry = new UserDefinedFunctionService();
		registry.Define(Entry("global", null, Ref("#20:100")));
		registry.Alias("alias", "global");
		registry.SetEnabled("global", false);
		await Assert.That(registry.Resolve("alias")).IsNotNull();
		registry.SetEnabled("alias", false);
		await Assert.That(registry.Resolve("alias")).IsNull();
	}

}
