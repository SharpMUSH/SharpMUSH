using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Server;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// A listing's attribute test asks whether the viewer may examine the object once per object, not once
/// per attribute, and still answers each attribute exactly as the single test does.
/// </summary>
public class AttributeViewMemoTests
{
	private const int Attributes = 100;

	private static (PermissionService Service, ILockService Locks, AnySharpObject Viewer, AnySharpObject Target) Build()
	{
		var factory = new TestObjectFactory();
		var viewer = factory.CreatePlayer(60, "viewer");
		var owner = factory.CreatePlayer(62, "owner");
		var target = factory.CreateThing(61, "target", owner: owner);
		viewer.Object().Id = "viewer"; owner.Object().Id = "owner"; target.Object().Id = "target";
		var viewerPlayer = viewer.Expect<SharpPlayer>();
		var ownerPlayer = owner.Expect<SharpPlayer>();
		viewerPlayer.Id = "player-viewer"; ownerPlayer.Id = "player-owner";
		viewer.Object().Owner = new(_ => Task.FromResult(viewerPlayer));
		target.Object().Owner = new(_ => Task.FromResult(ownerPlayer));
		target.Object().Flags = new(() => new[]
		{
			new SharpObjectFlag { Name = "VISUAL", Symbol = "v", SetPermissions = [], UnsetPermissions = [], TypeRestrictions = [], System = true }
		}.ToAsyncEnumerable());

		var options = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();
		options.CurrentValue.Returns(TestSharpMushOptions.Create());
		var locks = Substitute.For<ILockService>();
		locks.Evaluate(LockType.Examine, Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>()).Returns(false);
		var service = new PermissionService(locks, options, Substitute.For<IRealityPolicy>(),
			Substitute.For<IConnectionService>(), new Lazy<IAttributeService>(Substitute.For<IAttributeService>()));
		return (service, locks, viewer, target);
	}

	private static SharpAttribute[][] Paths()
		=> [.. Enumerable.Range(0, Attributes).Select(i => new[]
		{
			i % 2 == 0 ? TestAttributeFactory.Named($"A{i:D3}", "visual") : TestAttributeFactory.Named($"A{i:D3}")
		})];

	[Test]
	public async Task AListingEvaluatesTheExamineLockOncePerObject()
	{
		var (service, locks, viewer, target) = Build();
		var memo = new AttributeViewMemo();

		var answers = new List<bool>();
		foreach (var path in Paths())
		{
			answers.Add(await service.CanViewAttribute(viewer, target, memo, path));
		}

		await locks.Received(1).Evaluate(LockType.Examine, target, viewer);
		await Assert.That(answers.Count(x => x)).IsEqualTo(Attributes / 2);
	}

	[Test]
	public async Task TheMemoChangesNoAnswer()
	{
		var (service, _, viewer, target) = Build();
		var memo = new AttributeViewMemo();

		foreach (var path in Paths())
		{
			await Assert.That(await service.CanViewAttribute(viewer, target, memo, path))
				.IsEqualTo(await service.CanViewAttribute(viewer, target, path));
		}
	}
}
