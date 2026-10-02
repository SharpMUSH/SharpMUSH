using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// <see cref="BundledPackagePlan"/>: from the packages asked for to what to install and remove, dependencies
/// installed first and removed last, and a dependency of a wanted package never removed.
/// </summary>
public class BundledPackagePlanTests
{
	private static readonly IReadOnlyList<string> Order = ["base", "lib", "app", "other"];

	private static readonly Dictionary<string, IReadOnlyList<string>> Dependencies = new()
	{
		["lib"] = ["base"],
		["app"] = ["lib", "not-bundled"],
	};

	private static BundledPackagePlan Plan(string[] wanted, params string[] installed) =>
		BundledPackagePlan.For(wanted, installed.ToHashSet(), Dependencies, Order);

	[Test]
	public async Task WhatIsAskedFor_BringsItsDependencies_InOrder()
	{
		var plan = Plan(["app"]);

		await Assert.That(plan.Install).IsEquivalentTo(["base", "lib", "app"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(plan.Kept).IsEquivalentTo(["base", "lib"]);
		await Assert.That(plan.Remove).IsEmpty();
	}

	[Test]
	public async Task WhatIsNotAskedFor_IsRemoved_DependentsFirst()
	{
		var plan = Plan(["other"], "base", "lib", "app", "other");

		await Assert.That(plan.Remove).IsEquivalentTo(["app", "lib", "base"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(plan.Install).IsEmpty();
	}

	[Test]
	public async Task ADependencyOfSomethingWanted_IsNeverRemoved()
	{
		var plan = Plan(["lib"], "base", "lib", "app");

		await Assert.That(plan.Remove).IsEquivalentTo(["app"]);
		await Assert.That(plan.Install).IsEmpty();
	}

	[Test]
	public async Task NamesThatAreNotBundled_AreIgnored()
	{
		var plan = Plan(["app", "chargen"]);

		await Assert.That(plan.Install).DoesNotContain("not-bundled");
		await Assert.That(plan.Install).DoesNotContain("chargen");
	}
}
