using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// A pattern read walks every matched leaf's root..leaf path, and leaves under one branch share that
/// branch's prefixes. When the pattern matches the leaves but not the branch (<c>FOO`*</c>), each walk
/// has to resolve <c>FOO</c> itself; within one read it is resolved once, not once per leaf (#1466).
/// The flag test still sees the branch node for every leaf, so a restrictive branch still hides them all.
/// </summary>
public class AttributePatternAncestorMemoTests
{
	private const int Leaves = 50;

	private static (AttributeService Service, IMediator Mediator, IPermissionService Permissions, AnySharpObject Target) Build()
	{
		var target = new TestObjectFactory().CreateThing(10, "target");
		var mediator = Substitute.For<IMediator>();
		var permissions = Substitute.For<IPermissionService>();
		var service = new AttributeService(mediator, permissions, Substitute.For<ILocateService>(), Substitute.For<IValidateService>(),
			Substitute.For<INotifyService>(), Substitute.For<IOptionsWrapper<SharpMUSHOptions>>(), Substitute.For<IServiceProvider>(),
			NullLogger<AttributeService>.Instance);
		return (service, mediator, permissions, target);
	}

	private static LazySharpAttribute Lazy(SharpAttribute attr) => new(attr.Id, attr.Key, attr.Name, attr.Flags, null, attr.LongName,
		new(_ => Task.FromResult(AsyncEnumerable.Empty<LazySharpAttribute>())), attr.Owner, attr.SharpAttributeEntry,
		new(_ => Task.FromResult(MarkupString.MarkupText.Empty)));

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ABranchIsResolvedOncePerReadNotOncePerLeaf(bool branchIsDark)
	{
		var (service, mediator, permissions, target) = Build();
		var dbref = target.Object().DBRef;
		var branch = TestAttributeFactory.Named("FOO", branchIsDark ? new[] { "mortal_dark" } : []);
		var leaves = Enumerable.Range(0, Leaves).Select(i => TestAttributeFactory.Named($"FOO`L{i:D2}")).ToArray();

		mediator.CreateStream(Arg.Any<GetLazyAttributesQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => leaves.Select(leaf => new LazyAttributeWithSource(Lazy(leaf), dbref)).ToAsyncEnumerable());
		mediator.CreateStream(Arg.Any<GetLazyAttributeQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => new[] { Lazy(branch) }.ToAsyncEnumerable());
		mediator.CreateStream(Arg.Any<GetAttributesQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => leaves.Select(leaf => new AttributeWithSource(leaf, dbref)).ToAsyncEnumerable());
		mediator.CreateStream(Arg.Any<GetAttributeQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => new[] { branch }.ToAsyncEnumerable());
		permissions.CanViewAttribute(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<LazySharpAttribute[]>())
			.Returns(call => new ValueTask<bool>(!call.Arg<LazySharpAttribute[]>().Any(x => x.Flags.Any(f => f.Name == "mortal_dark"))));
		permissions.CanViewAttribute(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<AttributeViewMemo>(), Arg.Any<LazySharpAttribute[]>())
			.Returns(call => new ValueTask<bool>(!call.Arg<LazySharpAttribute[]>().Any(x => x.Flags.Any(f => f.Name == "mortal_dark"))));
		permissions.CanViewAttribute(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<SharpAttribute[]>())
			.Returns(call => new ValueTask<bool>(!call.Arg<SharpAttribute[]>().Any(x => x.Flags.Any(f => f.Name == "mortal_dark"))));
		permissions.CanViewAttribute(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<AttributeViewMemo>(), Arg.Any<SharpAttribute[]>())
			.Returns(call => new ValueTask<bool>(!call.Arg<SharpAttribute[]>().Any(x => x.Flags.Any(f => f.Name == "mortal_dark"))));

		var lazy = await (await service.LazilyGetAttributePatternAsync(target, target, "FOO`*", false,
			IAttributeService.AttributePatternMode.Wildcard)).Expect<IAsyncEnumerable<LazySharpAttribute>>().ToArrayAsync();
		var eager = (await service.GetAttributePatternAsync(target, target, "FOO`*", false,
			IAttributeService.AttributePatternMode.Wildcard)).Expect<SharpAttribute[]>();

		await Assert.That(lazy.Length).IsEqualTo(branchIsDark ? 0 : Leaves);
		await Assert.That(eager.Length).IsEqualTo(branchIsDark ? 0 : Leaves);
		mediator.Received(1).CreateStream(Arg.Any<GetLazyAttributeQuery>(), Arg.Any<CancellationToken>());
		mediator.Received(1).CreateStream(Arg.Any<GetAttributeQuery>(), Arg.Any<CancellationToken>());
	}
}
