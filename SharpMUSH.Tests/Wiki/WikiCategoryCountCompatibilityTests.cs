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
	public async Task LegacyStoreAndServiceImplementersInheritScanFallbackWithoutImplementingTheNewMethod()
	{
		var store = new InMemoryWikiStore();
		var service = new WikiStoreService(store, new WikiMarkdigPipeline());
		var first = (await service.CreateAsync("A", "body", "#1")).Expect<WikiPage>();
		var draft = (await service.CreateAsync("B", "body", "#43")).Expect<WikiPage>();
		var last = (await service.CreateAsync("C", "body", "#1")).Expect<WikiPage>();
		await service.SetMetadataAsync(draft.Id, [], false);
		var legacyStore = LegacyImplementer<IWikiStore>(store);
		var legacyService = LegacyImplementer<IWikiService>(service);
		foreach (var scan in new Func<WikiPageScanPosition, Task<WikiPageScanBatch>>[]
		{
			position => legacyStore.ScanPagesAsync(WikiVisibility.PublishedOnly, position, 1),
			position => legacyService.ScanPagesAsync(WikiVisibility.PublishedOnly, position, 1)
		})
		{
			var batch = await scan(new WikiPageScanStart());
			await Assert.That(batch.Pages.Single().Id).IsEqualTo(first.Id);
			var next = batch.Next is WikiPageScanContinuation continuation ? continuation
				: throw new InvalidOperationException("Legacy scan must continue.");
			var second = await scan(next);
			await Assert.That(second.Pages.Single().Id).IsEqualTo(last.Id);
			var finalPosition = second.Next is WikiPageScanContinuation final ? final
				: throw new InvalidOperationException("Exact-full legacy batch must continue.");
			var end = await scan(finalPosition);
			await Assert.That(end.Pages).IsEmpty();
			await Assert.That(end.Next is WikiPageScanEnd).IsTrue();
		}
		await Assert.That(legacyStore.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
			.Any(method => method.Name == nameof(IWikiStore.ScanPagesAsync))).IsFalse();
		await Assert.That(legacyService.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
			.Any(method => method.Name == nameof(IWikiService.ScanPagesAsync))).IsFalse();
		await Assert.That(typeof(ICacheable).IsAssignableFrom(typeof(ScanWikiPagesQuery))).IsFalse();
	}

	[Test]
	public async Task InMemorySeekSurvivesDeletedBoundaryAndUsesCurrentExplicitVisibility()
	{
		var store = new InMemoryWikiStore();
		var service = new WikiStoreService(store, new WikiMarkdigPipeline());
		var first = (await service.CreateAsync("A", "body", "#1")).Expect<WikiPage>();
		var second = (await service.CreateAsync("B", "body", "#1", categories: ["secret"])).Expect<WikiPage>();
		var last = (await service.CreateAsync("C", "body", "#42")).Expect<WikiPage>();
		await service.SetMetadataAsync(last.Id, [], false);
		var batch = await store.ScanPagesAsync(WikiVisibility.All, new WikiPageScanStart(), 1);
		await Assert.That(batch.Pages.Single().Id).IsEqualTo(first.Id);
		var position = batch.Next is WikiPageScanContinuation next ? next
			: throw new InvalidOperationException("Expected memory continuation.");
		await store.DeletePageAsync(first.Id);
		var resumed = await store.ScanPagesAsync(new WikiVisibility(false, "#42")
		{
			IncludedPageIds = new HashSet<string> { second.Id, last.Id },
			Hidden = new WikiReadRestrictions(false, new HashSet<string>(), new HashSet<string> { "secret" }, new HashSet<string>())
		}, position, 2);
		await Assert.That(resumed.Pages.Single().Id).IsEqualTo(last.Id);
		await Assert.That(resumed.Next is WikiPageScanEnd).IsTrue();
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
