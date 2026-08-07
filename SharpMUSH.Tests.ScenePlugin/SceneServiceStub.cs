using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models;
using SharpMUSH.Plugins.Scene.Models;
using SharpMUSH.Plugins.Scene.Storage;
using System.Security.Claims;
using Scene = SharpMUSH.Plugins.Scene.Models.Scene;

namespace SharpMUSH.Tests.ScenePlugin;

/// <summary>
/// Throwing <see cref="ISceneService"/> base so a test fake only implements the handful of methods it
/// actually exercises — and so a method it did NOT expect to be called fails loudly rather than returning
/// a default that quietly satisfies an assertion. Every member is virtual for that reason.
/// </summary>
internal abstract class SceneServiceStub : ISceneService
{
	public virtual Task<Scene> CreateSceneAsync(string roomDbref, string ownerDbref, string title = "") => throw new NotSupportedException();
	public virtual Task<OneOf<Scene, NotFound>> GetSceneAsync(string sceneId) => throw new NotSupportedException();
	public virtual Task<OneOf<Scene, NotFound>> SetSceneMetaAsync(string sceneId, string key, string value) => throw new NotSupportedException();
	public virtual Task<IReadOnlyList<Scene>> ListScenesAsync(string filter, string? viewerDbref = null, long? fromUtcMillis = null, long? toUtcMillis = null, int count = 50) => throw new NotSupportedException();
	public virtual Task<OneOf<Scene, NotFound>> GetActiveSceneInRoomAsync(string roomDbref) => throw new NotSupportedException();
	public virtual Task<OneOf<ScenePose, NotFound, Error<string>>> AddPoseAsync(string sceneId, string authorDbref, string showAs, string originDbref, string source, IReadOnlyList<string> tags, string content) => throw new NotSupportedException();
	public virtual Task<OneOf<ScenePose, NotFound>> GetPoseAsync(string poseId) => throw new NotSupportedException();
	public virtual Task<OneOf<IReadOnlyList<ScenePose>, NotFound>> GetPosesAsync(string sceneId, string? authorDbref = null, int? count = null) => throw new NotSupportedException();
	public virtual Task<OneOf<ScenePose, NotFound>> SetPoseMetaAsync(string poseId, string key, string value) => throw new NotSupportedException();
	public virtual Task<OneOf<ScenePose, NotFound>> EditPoseAsync(string poseId, string editorDbref, string content) => throw new NotSupportedException();
	public virtual Task<OneOf<ScenePose, NotFound, Error<string>>> UndoPoseAsync(string poseId) => throw new NotSupportedException();
	public virtual Task<OneOf<ScenePose, NotFound, Error<string>>> RedoPoseAsync(string poseId) => throw new NotSupportedException();
	public virtual Task<OneOf<ScenePose, NotFound, Error<string>>> MovePoseAsync(string poseId, string afterPoseId) => throw new NotSupportedException();
	public virtual Task<OneOf<ScenePose, NotFound>> DeletePoseAsync(string poseId) => throw new NotSupportedException();
	public virtual Task<OneOf<IReadOnlyList<ScenePoseEdit>, NotFound>> GetPoseEditsAsync(string poseId) => throw new NotSupportedException();
	public virtual Task<OneOf<SceneMember, NotFound>> AddMemberAsync(string sceneId, string playerDbref, string role) => throw new NotSupportedException();
	public virtual Task<OneOf<None, NotFound>> RemoveMemberAsync(string sceneId, string playerDbref) => throw new NotSupportedException();
	public virtual Task<OneOf<IReadOnlyList<SceneMember>, NotFound>> GetMembersAsync(string sceneId, string? role = null) => throw new NotSupportedException();
	public virtual Task<OneOf<SceneMember, NotFound>> GetMemberAsync(string sceneId, string playerDbref) => throw new NotSupportedException();
	public virtual Task<OneOf<None, NotFound>> SetFocusAsync(string playerDbref, string? sceneId = null) => throw new NotSupportedException();
	public virtual Task<OneOf<Scene, NotFound>> GetCurrentSceneAsync(string playerDbref) => throw new NotSupportedException();
	public virtual Task<OneOf<SceneMember, NotFound>> SetShowAsAsync(string sceneId, string playerDbref, string showAs) => throw new NotSupportedException();
	public virtual Task<ScenePlot> UpsertPlotAsync(string? plotId, string title, string description, string ownerDbref) => throw new NotSupportedException();
	public virtual Task<OneOf<ScenePlot, NotFound>> GetPlotAsync(string plotId) => throw new NotSupportedException();
	public virtual Task<OneOf<None, NotFound>> LinkSceneToPlotAsync(string plotId, string sceneId) => throw new NotSupportedException();
	public virtual Task<OneOf<None, NotFound>> UnlinkSceneFromPlotAsync(string plotId, string sceneId) => throw new NotSupportedException();
	public virtual Task<OneOf<IReadOnlyList<string>, NotFound>> GetTagsAsync(string sceneId) => throw new NotSupportedException();
	public virtual Task<OneOf<IReadOnlyList<string>, NotFound>> GetCastAsync(string sceneId) => throw new NotSupportedException();
}

/// <summary>
/// Scene service over a fixed set of scenes, recording the dbrefs it was asked to look memberships up by
/// (the second half of the dbref-spelling bug — a mangled reference resolves to no object at all).
///
/// <para>Shared by both authorization suites (<see cref="SceneControllerVisibilityTests"/> for REST,
/// <see cref="SceneHubAuthorizationTests"/> for the realtime hub) on purpose: the two surfaces answer
/// visibility through the one shared predicate, so they must be provable against the one fake.</para>
/// </summary>
internal sealed class FixedSceneService(params Scene[] scenes) : SceneServiceStub
{
	/// <summary>The member dbref that holds a membership edge on every scene, or null for none.</summary>
	public string? MemberDbref { get; init; }

	public List<string> MemberLookups { get; } = [];

	public List<string> SceneLookups { get; } = [];

	public override Task<OneOf<Scene, NotFound>> GetSceneAsync(string sceneId)
	{
		SceneLookups.Add(sceneId);

		return Task.FromResult(scenes.FirstOrDefault(s => s.Id == sceneId) is { } scene
			? OneOf<Scene, NotFound>.FromT0(scene)
			: new NotFound());
	}

	public override Task<IReadOnlyList<Scene>> ListScenesAsync(string filter, string? viewerDbref = null,
		long? fromUtcMillis = null, long? toUtcMillis = null, int count = 50) =>
		Task.FromResult<IReadOnlyList<Scene>>(scenes);

	public override Task<OneOf<SceneMember, NotFound>> GetMemberAsync(string sceneId, string playerDbref)
	{
		MemberLookups.Add(playerDbref);

		// Match the storages, which resolve the reference through the live object rather than comparing
		// strings: a bare dbref and its objid name the same player.
		var matches = MemberDbref is { } member
			&& DBRef.TryParse(member, out var expected)
			&& DBRef.TryParse(playerDbref, out var actual)
			&& expected!.Value.SameObjectAs(actual!.Value);

		return Task.FromResult(matches
			? OneOf<SceneMember, NotFound>.FromT0(
				new SceneMember(sceneId, MemberDbref, "God", "participant", string.Empty, true, 3))
			: new NotFound());
	}
}

/// <summary>
/// The caller and scene shapes both authorization suites are written against. Shared for the same reason
/// <see cref="FixedSceneService"/> is: the REST controller and the realtime hub answer visibility through
/// one predicate, so a scene that is "public" or a caller that is "God" must mean the same thing on both
/// sides. A second copy of either is how the two suites drift into proving different things.
/// </summary>
internal static class SceneFixture
{
	/// <summary>
	/// Claim name carrying the acting character's dbref, spelled as the production auth handlers emit it.
	/// A literal rather than a reference to <c>SceneVisibility.CharacterDbrefClaim</c>: the tests assert the
	/// wire name, so reading it from the type under test would make a rename invisible here.
	/// </summary>
	public const string CharacterDbrefClaim = "character_dbref";

	/// <summary>God's claim as the auth handlers actually mint it — objid, not bare dbref.</summary>
	public const string GodObjid = "#1:1785989066109";

	/// <summary>A different, unrelated character's claim.</summary>
	public const string StrangerObjid = "#7:1700000000000";

	/// <summary>A scene with the fixed id <c>scene-1</c>, owned by <paramref name="ownerDbref"/>.</summary>
	public static Scene SceneOwnedBy(string? ownerDbref, bool isPublic) => new(
		Id: "scene-1",
		Status: "active",
		IsPublic: isPublic,
		IsTempRoom: false,
		ScheduledFor: null,
		StartedAt: 1,
		LastActivityAt: 2,
		PoseCount: 0,
		OwnerDbref: ownerDbref,
		OwnerName: "God",
		StarterDbref: ownerDbref,
		StarterName: "God",
		RoomDbref: null,
		RoomName: string.Empty,
		Meta: new Dictionary<string, string>());

	/// <summary>
	/// The caller principal: <paramref name="claimValue"/> is the acting character, or null for a caller
	/// with no character at all. <paramref name="authenticated"/> distinguishes the two null cases — an
	/// identity with no authentication type (what an anonymous request carries) from a signed-in account
	/// acting as no character (a guest, or an account whose only characters were unlinked). Both must be
	/// refused, and neither surface is allowed to read anything but the claim to decide that.
	/// </summary>
	public static ClaimsPrincipal PrincipalFor(string? claimValue, bool authenticated = false) =>
		new(claimValue is null
			? authenticated ? new ClaimsIdentity(authenticationType: "TestScheme") : new ClaimsIdentity()
			: new ClaimsIdentity([new Claim(CharacterDbrefClaim, claimValue)], "TestScheme"));
}
