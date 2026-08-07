using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Plugins.Scene.Web;
using System.Reflection;
using System.Security.Claims;

namespace SharpMUSH.Tests.ScenePlugin;

/// <summary>
/// The scene realtime hub's permission boundary, exercised directly against <see cref="SceneHub"/> with the
/// shared <see cref="FixedSceneService"/> — no SignalR pipeline, so every caller shape is reachable.
///
/// <para>REGRESSION: <c>JoinScene</c> was a one-liner that added <em>any</em> caller — including an
/// anonymous one — to the <c>scene:{id}</c> broadcast group for any id it named, after which every pose
/// broadcast to that scene reached them. A private scene leaked its live contents to anyone who asked,
/// while the REST route for the same scene answered 404. The gate is three layers: the hub's
/// <see cref="AuthorizeAttribute"/>, then a character on the connection, then the shared visibility
/// predicate the REST controller uses.</para>
/// </summary>
public class SceneHubAuthorizationTests
{
	/// <summary>
	/// Builds the hub for a caller: <paramref name="claimValue"/> is the acting character, or null for a
	/// caller with no character. <paramref name="authenticated"/> picks which of the two null shapes
	/// <see cref="SceneFixture.PrincipalFor"/> builds. Both must be refused.
	/// </summary>
	private static (SceneHub hub, RecordingGroupManager groups) HubFor(
		FixedSceneService service, string? claimValue, bool authenticated = true)
	{
		var groups = new RecordingGroupManager();
		var hub = new SceneHub(service, NullLogger<SceneHub>.Instance)
		{
			Groups = groups,
			Context = new FakeHubCallerContext(SceneFixture.PrincipalFor(claimValue, authenticated)),
		};

		return (hub, groups);
	}

	/// <summary>
	/// The outermost layer: an anonymous connection must never reach a hub method at all. <c>[Authorize]</c>
	/// with no scheme resolves against the host's DEFAULT scheme — <c>AccountSession</c> in production,
	/// <c>DebugAuth</c> in development — exactly as the host's <c>GameHub</c> is secured.
	/// </summary>
	[Test]
	public async Task SceneHub_CarriesTheAuthorizeAttribute_WithNoSchemeOverride()
	{
		var authorize = typeof(SceneHub).GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();

		await Assert.That(authorize).IsNotEmpty()
			.Because("an unauthenticated connection must be refused at the endpoint, before any hub method runs");
		await Assert.That(authorize[0].AuthenticationSchemes).IsNull()
			.Because("the hub must authorize on the host's default scheme, like GameHub does");
	}

	/// <summary>
	/// The principal an anonymous connection would carry — no authentication type, no claims — is refused by
	/// the method body too. That is defence in depth, not the anonymous gate: these tests call the hub
	/// directly, so no SignalR pipeline and therefore no <c>[Authorize]</c> ever runs, and
	/// <c>SceneVisibility.ActingCharacter</c> reads only the <c>character_dbref</c> claim — it never consults
	/// <c>Identity.IsAuthenticated</c>. The refusal below is the missing-character one, and the name says so.
	///
	/// <para>Rejecting an anonymous <em>connection</em> is unreachable from here and is asserted where it
	/// lives: <see cref="SceneHub_CarriesTheAuthorizeAttribute_WithNoSchemeOverride"/> for the attribute and
	/// its scheme, and the integration suite's <c>SceneHub_IsMappedAt_HubsScene</c> for the endpoint the
	/// attribute is applied to.</para>
	/// </summary>
	[Test]
	public async Task JoinScene_UnauthenticatedIdentityWithNoCharacter_IsRefusedAndNeverJoinsTheGroup()
	{
		var service = new FixedSceneService(SceneFixture.SceneOwnedBy("#1", isPublic: true));
		var (hub, groups) = HubFor(service, claimValue: null, authenticated: false);

		await Assert.That(async () => await hub.JoinScene("scene-1")).Throws<HubException>();
		await Assert.That(groups.Added).IsEmpty();
	}

	/// <summary>
	/// "Joining a Scene requires a Character. Guests cannot join scenes." — an authenticated account acting
	/// as no character is refused even for a PUBLIC scene, which the REST route would happily serve it.
	/// </summary>
	[Test]
	public async Task JoinScene_AuthenticatedAccountWithNoCharacter_IsRefusedEvenForAPublicScene()
	{
		var service = new FixedSceneService(SceneFixture.SceneOwnedBy("#1", isPublic: true));
		var (hub, groups) = HubFor(service, claimValue: null);

		await Assert.That(async () => await hub.JoinScene("scene-1")).Throws<HubException>();
		await Assert.That(groups.Added).IsEmpty();
		// The character check short-circuits: no scene is even looked up for a caller who cannot join any.
		await Assert.That(service.SceneLookups).IsEmpty();
	}

	[Test]
	public async Task JoinScene_PublicScene_AdmitsAnyCharacter()
	{
		var service = new FixedSceneService(SceneFixture.SceneOwnedBy("#1", isPublic: true));
		var (hub, groups) = HubFor(service, SceneFixture.StrangerObjid);

		await hub.JoinScene("scene-1");

		await Assert.That(groups.Added).IsEquivalentTo(new[] { ("conn-001", "scene:scene-1") });
	}

	[Test]
	public async Task JoinScene_PrivateSceneOwnedByCaller_IsAdmitted()
	{
		var service = new FixedSceneService(SceneFixture.SceneOwnedBy("#1", isPublic: false));
		var (hub, groups) = HubFor(service, SceneFixture.GodObjid);

		await hub.JoinScene("scene-1");

		await Assert.That(groups.Added).IsEquivalentTo(new[] { ("conn-001", "scene:scene-1") });
		// The owner check must answer on its own — a membership lookup would mask a broken owner comparison.
		await Assert.That(service.MemberLookups).IsEmpty();
	}

	[Test]
	public async Task JoinScene_PrivateSceneCallerIsMember_IsAdmitted()
	{
		var service = new FixedSceneService(SceneFixture.SceneOwnedBy("#7", isPublic: false)) { MemberDbref = "#1" };
		var (hub, groups) = HubFor(service, SceneFixture.GodObjid);

		await hub.JoinScene("scene-1");

		await Assert.That(groups.Added).IsEquivalentTo(new[] { ("conn-001", "scene:scene-1") });
		// The storage resolves the reference against the live object, so it must be handed a parseable
		// dbref — the canonical objid spelling, not a hash-stripped fragment.
		await Assert.That(service.MemberLookups).Contains(SceneFixture.GodObjid);
	}

	[Test]
	public async Task JoinScene_PrivateSceneCallerIsNeitherOwnerNorMember_IsRefused()
	{
		var service = new FixedSceneService(SceneFixture.SceneOwnedBy("#1", isPublic: false));
		var (hub, groups) = HubFor(service, SceneFixture.StrangerObjid);

		await Assert.That(async () => await hub.JoinScene("scene-1")).Throws<HubException>();
		await Assert.That(groups.Added).IsEmpty();
	}

	/// <summary>
	/// A missing scene and an invisible one must be indistinguishable, or the refusal itself enumerates
	/// private scene ids. The REST side answers 404 to both for the same reason.
	/// </summary>
	[Test]
	public async Task JoinScene_UnknownScene_IsRefusedIndistinguishablyFromAnInvisibleOne()
	{
		var service = new FixedSceneService(SceneFixture.SceneOwnedBy("#1", isPublic: false));
		var (hub, _) = HubFor(service, SceneFixture.StrangerObjid);

		var missing = await Assert.That(async () => await hub.JoinScene("no-such-scene")).Throws<HubException>();
		var invisible = await Assert.That(async () => await hub.JoinScene("scene-1")).Throws<HubException>();

		await Assert.That(missing!.Message).IsEqualTo(invisible!.Message);
	}

	[Test]
	public async Task JoinScene_WithoutASceneId_IsRefused()
	{
		var service = new FixedSceneService(SceneFixture.SceneOwnedBy("#1", isPublic: true));
		var (hub, groups) = HubFor(service, SceneFixture.GodObjid);

		await Assert.That(async () => await hub.JoinScene(null)).Throws<HubException>();
		await Assert.That(groups.Added).IsEmpty();
	}

	[Test]
	public async Task LeaveScene_WithoutACharacter_IsRefused()
	{
		var service = new FixedSceneService(SceneFixture.SceneOwnedBy("#1", isPublic: true));
		var (hub, groups) = HubFor(service, claimValue: null);

		await Assert.That(async () => await hub.LeaveScene("scene-1")).Throws<HubException>();
		await Assert.That(groups.Removed).IsEmpty();
	}

	/// <summary>
	/// Leaving is gated on the character requirement only — deliberately NOT on visibility. A player whose
	/// membership is revoked mid-scene must still be able to drop the subscription; requiring visibility
	/// would leave them receiving poses with no way to unsubscribe short of disconnecting.
	/// </summary>
	[Test]
	public async Task LeaveScene_WithACharacter_LeavesTheGroupWithoutConsultingVisibility()
	{
		var service = new FixedSceneService(SceneFixture.SceneOwnedBy("#1", isPublic: false));
		var (hub, groups) = HubFor(service, SceneFixture.StrangerObjid);

		await hub.LeaveScene("scene-1");

		await Assert.That(groups.Removed).IsEquivalentTo(new[] { ("conn-001", "scene:scene-1") });
		await Assert.That(service.SceneLookups).IsEmpty();
	}

	/// <summary>Records group membership calls instead of performing them.</summary>
	private sealed class RecordingGroupManager : IGroupManager
	{
		public List<(string ConnectionId, string Group)> Added { get; } = [];
		public List<(string ConnectionId, string Group)> Removed { get; } = [];

		public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
		{
			Added.Add((connectionId, groupName));
			return Task.CompletedTask;
		}

		public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
		{
			Removed.Add((connectionId, groupName));
			return Task.CompletedTask;
		}
	}

	/// <summary>The caller context a hub method reads: a connection id and the connection's principal.</summary>
	private sealed class FakeHubCallerContext(ClaimsPrincipal user) : HubCallerContext
	{
		public override string ConnectionId => "conn-001";
		public override string? UserIdentifier => null;
		public override ClaimsPrincipal? User => user;
		public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
		public override IFeatureCollection Features { get; } = new FeatureCollection();
		public override CancellationToken ConnectionAborted => CancellationToken.None;
		public override void Abort() { }
	}
}
