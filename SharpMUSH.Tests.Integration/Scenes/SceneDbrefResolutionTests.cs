using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration.Scenes;

/// <summary>
/// Proves (or disproves) the reported bug: the live <c>*Dbref</c> values on scene
/// records — resolved from the object edges — come back empty even though the
/// <c>*Name</c> snapshots are captured. Object references are taken as <c>#1</c>
/// (the seeded God object), so a correct resolution must round-trip back to <c>#1</c>.
/// These assertions are deliberately on the resolved DBREF, not the name snapshot.
/// </summary>
[NotInParallel]
public class SceneDbrefResolutionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactory { get; init; }

	private ISceneService Scenes => WebAppFactory.Services.GetRequiredService<ISharpDatabase>() as ISceneService
		?? throw new InvalidOperationException("ISharpDatabase does not implement ISceneService in this configuration.");

	private const string God = "#1";

	[Test]
	public async Task CreateScene_ResolvesOwnerDbref_BackToGod()
	{
		var created = await Scenes.CreateSceneAsync(roomDbref: "", ownerDbref: God, title: "Dbref owner");

		// Read it back through the resolution path.
		var got = await Scenes.GetSceneAsync(created.Id);
		await Assert.That(got.IsT0).IsTrue();

		// The name snapshot should be captured (this already works)...
		await Assert.That(got.AsT0.OwnerName).IsNotEmpty();
		// ...and the live dbref must resolve back to #1 (this is the bug under test).
		await Assert.That(got.AsT0.OwnerDbref).IsEqualTo(God);
	}

	[Test]
	public async Task AddPose_ResolvesAuthorDbref_BackToGod()
	{
		var scene = await Scenes.CreateSceneAsync(roomDbref: "", ownerDbref: God, title: "Dbref author");
		var added = await Scenes.AddPoseAsync(scene.Id, God, "", God, "pose", [], "dbref author check");
		await Assert.That(added.IsT0).IsTrue();

		var got = await Scenes.GetPoseAsync(added.AsT0.Id);
		await Assert.That(got.IsT0).IsTrue();
		await Assert.That(got.AsT0.AuthorName).IsNotEmpty();
		await Assert.That(got.AsT0.AuthorDbref).IsEqualTo(God);
	}

	[Test]
	public async Task AddMember_ResolvesMemberDbref_BackToGod()
	{
		var scene = await Scenes.CreateSceneAsync(roomDbref: "", ownerDbref: God, title: "Dbref member");
		var member = await Scenes.AddMemberAsync(scene.Id, God, "participant");
		await Assert.That(member.IsT0).IsTrue();
		await Assert.That(member.AsT0.MemberDbref).IsEqualTo(God);

		var members = await Scenes.GetMembersAsync(scene.Id);
		await Assert.That(members.IsT0).IsTrue();
		await Assert.That(members.AsT0.Select(m => m.MemberDbref)).Contains(God);
	}
}
