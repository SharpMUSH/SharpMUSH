using System.Reflection;
using System.Reflection.Emit;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Queries.Database.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki;

public class WikiCategoryCountCompatibilityTests
{
	// An implementer with only the old abstract surface. CLR default-interface dispatch, not
	// DispatchProxy/NSubstitute, must supply the omitted scalar method. Other calls reach a real store.
	private static T LegacyImplementer<T>(T inner) where T : class
	{
		var contract = typeof(T);
		var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("LegacyWiki" + Guid.NewGuid().ToString("N")),
			AssemblyBuilderAccess.RunAndCollect);
		var type = assembly.DefineDynamicModule("Legacy").DefineType("LegacyImplementer", TypeAttributes.Public | TypeAttributes.Sealed);
		type.AddInterfaceImplementation(contract);
		var field = type.DefineField("inner", contract, FieldAttributes.Private | FieldAttributes.InitOnly);
		var constructor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [contract]);
		var il = constructor.GetILGenerator();
		il.Emit(OpCodes.Ldarg_0);
		il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)
			?? throw new InvalidOperationException("Object constructor missing."));
		il.Emit(OpCodes.Ldarg_0);
		il.Emit(OpCodes.Ldarg_1);
		il.Emit(OpCodes.Stfld, field);
		il.Emit(OpCodes.Ret);
		var index = 0;
		foreach (var method in contract.GetMethods().Where(method => method.IsAbstract))
		{
			var parameters = method.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
			var forward = type.DefineMethod("Forward" + index++, MethodAttributes.Public | MethodAttributes.Virtual
				| MethodAttributes.Final | MethodAttributes.NewSlot, method.ReturnType, parameters);
			var body = forward.GetILGenerator();
			body.Emit(OpCodes.Ldarg_0);
			body.Emit(OpCodes.Ldfld, field);
			for (var argument = 0; argument < parameters.Length; argument++) body.Emit(OpCodes.Ldarg, (short)(argument + 1));
			body.Emit(OpCodes.Callvirt, method);
			body.Emit(OpCodes.Ret);
			type.DefineMethodOverride(forward, method);
		}
		var concrete = type.CreateType() ?? throw new InvalidOperationException("Legacy implementation type missing.");
		return (T)(Activator.CreateInstance(concrete, inner) ?? throw new InvalidOperationException("Legacy implementation missing."));
	}

	[Test]
	public async Task LegacyStoreAndServiceImplementersInheritAnExactVisibilityPreservingFallback()
	{
		var store = new InMemoryWikiStore();
		var service = new WikiStoreService(store, new WikiMarkdigPipeline());
		var first = (await service.CreateAsync("Public", "body", "#1", categories: ["Rules"])).Expect<WikiPage>();
		var draft = (await service.CreateAsync("Draft", "body", "#42", categories: ["Rules"])).Expect<WikiPage>();
		await service.SetMetadataAsync(draft.Id, ["rules"], false);
		IWikiStore legacyStore = LegacyImplementer<IWikiStore>(store);
		IWikiService legacyService = LegacyImplementer<IWikiService>(service);
		foreach (var visibility in new[]
		{
			WikiVisibility.All, WikiVisibility.PublishedOnly, new WikiVisibility(false, "#42"),
			WikiVisibility.All with { IncludedPageIds = new HashSet<string> { draft.Id } },
			WikiVisibility.All with { ExcludedPageIds = new HashSet<string> { first.Id } },
			WikiVisibility.All with { Hidden = WikiReadRestrictions.All }
		})
		{
			var expected = (await store.CountPagesByCategoryAsync(visibility)).GetValueOrDefault("rules");
			await Assert.That(await legacyStore.CountPagesInCategoryAsync(" RULES ", visibility)).IsEqualTo(expected);
			await Assert.That(await legacyService.CountPagesInCategoryAsync(" RULES ", visibility)).IsEqualTo(expected);
			await Assert.That(await legacyStore.CountPagesInCategoryAsync("absent", visibility)).IsEqualTo(0);
			await Assert.That(await legacyService.CountPagesInCategoryAsync("absent", visibility)).IsEqualTo(0);
		}
		foreach (var contract in new[] { typeof(IWikiStore), typeof(IWikiService) })
		{
			var method = contract.GetMethod(nameof(IWikiStore.CountPagesInCategoryAsync))
				?? throw new InvalidOperationException("Scalar contract missing.");
			await Assert.That(method.IsAbstract).IsFalse();
			await Assert.That(method.GetParameters().Any(parameter => parameter.HasDefaultValue)).IsFalse();
			var implementer = contract == typeof(IWikiStore) ? legacyStore.GetType() : legacyService.GetType();
			await Assert.That(implementer.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
				.Any(candidate => candidate.Name == method.Name)).IsFalse();
		}
		// ISharpDatabase currently composes other stores; wiki is separately implemented by Lightning.
		await Assert.That(typeof(ISharpDatabase).GetInterfaces().Contains(typeof(IWikiStore))).IsFalse();
		await Assert.That(typeof(ICacheable).IsAssignableFrom(typeof(CountWikiPagesInCategoryQuery))).IsFalse();
	}

	[Test]
	public async Task InMemoryScalarCountsDuplicateMembershipOnceAndPreservesAdmission()
	{
		var store = new InMemoryWikiStore();
		var at = DateTimeOffset.UnixEpoch;
		var first = (await store.CreatePageAsync(new WikiPage("", "public", "Public", "main", "body", "body", "body",
			"#1", "#1", at, at, 1)
		{ Categories = ["rules", "RULES", "rules"] })).Expect<WikiPage>();
		var second = (await store.CreatePageAsync(first with
		{ Id = "", Slug = "private", Title = "Private", Published = false, AuthorDbref = "#42" })).Expect<WikiPage>();
		(await store.CreatePageAsync(first with { Id = "", Slug = "neighbor", Categories = ["rules-extra"] })).Expect<WikiPage>();
		await Assert.That(await store.CountPagesInCategoryAsync("RULES", WikiVisibility.All)).IsEqualTo(2);
		await Assert.That(await store.CountPagesInCategoryAsync("rules", WikiVisibility.PublishedOnly)).IsEqualTo(1);
		await Assert.That(await store.CountPagesInCategoryAsync("rules", new WikiVisibility(false, "#42"))).IsEqualTo(2);
		await Assert.That(await store.CountPagesInCategoryAsync("rules", WikiVisibility.All with
		{
			IncludedPageIds = new HashSet<string> { first.Id, second.Id },
			ExcludedPageIds = new HashSet<string> { first.Id },
			Hidden = new WikiReadRestrictions(false, new HashSet<string>(), new HashSet<string>(), new HashSet<string> { second.Id })
		})).IsEqualTo(0);
		await Assert.That(await store.CountPagesInCategoryAsync("missing", WikiVisibility.All)).IsEqualTo(0);
	}
}
