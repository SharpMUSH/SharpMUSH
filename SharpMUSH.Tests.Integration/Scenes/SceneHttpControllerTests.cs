using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models.Scene;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.Integration.Scenes;

/// <summary>
/// HTTP-level integration tests for <c>SceneController</c> (the read-only Scene REST API the
/// Blazor WASM portal consumes). Requests travel through the full ASP.NET Core pipeline via
/// the in-process <see cref="ServerWebAppFactory"/>; scenes are seeded directly through the DI
/// <see cref="ISceneService"/> (the Scene plugin's per-provider storage, Phase 8), mirroring how the
/// wiki HTTP tests seed pages through <c>IWikiService</c>.
///
/// DebugAuthenticationHandler auto-authenticates every request as the bootstrap admin — player
/// #1 (God), whose <c>character_dbref</c> claim is <c>#1</c>. Visibility is therefore exercised
/// against that caller: a private scene owned by a non-existent object (no #1 owner/membership)
/// is hidden (404), while a private scene owned by #1 is visible to #1.
///
/// NOTE: Do NOT implement IAsyncInitializer here — TUnit's ClassDataSource calls
/// ServerWebAppFactory.InitializeAsync() exactly once for the session.
/// </summary>
[NotInParallel]
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class SceneHttpControllerTests(ServerWebAppFactory factory)
{
	// DTOs mirror SceneController's DTOs — only the fields the tests assert on. Timestamps are
	// long Unix-millis (the portal DTO contract), so they bind straight through.
	private record SceneDto(
		string Id,
		string Status,
		bool IsPublic,
		long StartedAt,
		long LastActivityAt,
		int PoseCount,
		string? OwnerDbref,
		string OwnerName,
		string RoomName,
		IReadOnlyDictionary<string, string> Meta);

	private record ScenePoseDto(
		string Id,
		string SceneId,
		string AuthorName,
		string ShowAsName,
		string Content,
		long CreatedAt,
		bool IsDeleted);

	private record SceneMemberDto(
		string SceneId,
		string? MemberDbref,
		string MemberName,
		string Role,
		bool IsCurrent,
		long GrantedAt);

	private const string God = "#1";

	private ISceneService Scenes => factory.Services.GetRequiredService<ISceneService>();

	/// <summary>
	/// Client pinned to https so the DebugAuth principal survives the http→https redirect
	/// (HttpClient drops Authorization on a 307), matching the wiki admin tests.
	/// </summary>
	private HttpClient CreateClient()
	{
		var http = factory.CreateHttpClient();
		http.BaseAddress = new Uri("https://localhost/");
		return http;
	}

	private async Task<Scene> NewPublicSceneAsync(string title)
	{
		var scene = await Scenes.CreateSceneAsync(roomDbref: "", ownerDbref: God, title: title);
		await Scenes.SetSceneMetaAsync(scene.Id, "status", "active");
		await Scenes.SetSceneMetaAsync(scene.Id, "public", "1");
		return (await Scenes.GetSceneAsync(scene.Id)).AsT0;
	}

	// ── GET /api/scenes/{id} ───────────────────────────────────────────────────

	[Test]
	public async Task GetScene_PublicScene_Returns200WithMatchingId()
	{
		var http = CreateClient();
		var scene = await NewPublicSceneAsync($"Public {Guid.NewGuid():N}");

		var response = await http.GetAsync($"api/scenes/{scene.Id}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var dto = await response.Content.ReadFromJsonAsync<SceneDto>();
		await Assert.That(dto).IsNotNull();
		await Assert.That(dto!.Id).IsEqualTo(scene.Id);
		await Assert.That(dto.IsPublic).IsTrue();
		await Assert.That(dto.StartedAt).IsGreaterThan(0);
	}

	[Test]
	public async Task GetScene_Unknown_Returns404()
	{
		var http = CreateClient();

		var response = await http.GetAsync($"api/scenes/does-not-exist-{Guid.NewGuid():N}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	// ── Visibility ─────────────────────────────────────────────────────────────

	[Test]
	public async Task GetScene_PrivateSceneCallerIsMember_Returns200()
	{
		var http = CreateClient();
		// Private (default IsPublic=false) scene; the DebugAuth caller (#1) is a member.
		var scene = await Scenes.CreateSceneAsync(roomDbref: "", ownerDbref: God, title: $"PrivMember {Guid.NewGuid():N}");
		await Assert.That(scene.IsPublic).IsFalse();
		await Scenes.AddMemberAsync(scene.Id, God, "participant");

		var response = await http.GetAsync($"api/scenes/{scene.Id}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	[Test]
	public async Task GetScene_PrivateSceneNotOwnedByCaller_Returns404()
	{
		var http = CreateClient();
		// Owned by a non-existent object → OwnerDbref resolves to null, and #1 holds no
		// membership, so #1 must not see it. Private + not owner + not member ⇒ 404.
		var scene = await Scenes.CreateSceneAsync(roomDbref: "", ownerDbref: "#99999", title: $"PrivHidden {Guid.NewGuid():N}");
		await Assert.That(scene.IsPublic).IsFalse();

		var response = await http.GetAsync($"api/scenes/{scene.Id}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	// ── GET /api/scenes (list) ─────────────────────────────────────────────────

	[Test]
	public async Task ListScenes_RecentFilter_IncludesPublicScene()
	{
		var http = CreateClient();
		var scene = await NewPublicSceneAsync($"List {Guid.NewGuid():N}");

		var scenes = await http.GetFromJsonAsync<List<SceneDto>>("api/scenes?filter=recent&count=200");

		await Assert.That(scenes).IsNotNull();
		await Assert.That(scenes!.Any(s => s.Id == scene.Id)).IsTrue();
	}

	[Test]
	public async Task ListScenes_DoesNotLeakHiddenPrivateScene()
	{
		var http = CreateClient();
		var hidden = await Scenes.CreateSceneAsync(roomDbref: "", ownerDbref: "#99999", title: $"Leak {Guid.NewGuid():N}");

		var scenes = await http.GetFromJsonAsync<List<SceneDto>>("api/scenes?filter=recent&count=200");

		await Assert.That(scenes).IsNotNull();
		await Assert.That(scenes!.All(s => s.Id != hidden.Id)).IsTrue();
	}

	// ── GET /api/scenes/{id}/poses ─────────────────────────────────────────────

	[Test]
	public async Task GetPoses_ReturnsPosesInChainOrder()
	{
		var http = CreateClient();
		var scene = await NewPublicSceneAsync($"Poses {Guid.NewGuid():N}");
		await Scenes.AddPoseAsync(scene.Id, God, "", God, "pose", [], "First pose.");
		await Scenes.AddPoseAsync(scene.Id, God, "", God, "pose", [], "Second pose.");

		var poses = await http.GetFromJsonAsync<List<ScenePoseDto>>($"api/scenes/{scene.Id}/poses");

		await Assert.That(poses).IsNotNull();
		await Assert.That(poses!.Count).IsEqualTo(2);
		await Assert.That(poses[0].Content).Contains("First");
		await Assert.That(poses[1].Content).Contains("Second");
		await Assert.That(poses[0].CreatedAt).IsGreaterThan(0);
	}

	[Test]
	public async Task GetPoses_HiddenPrivateScene_Returns404()
	{
		var http = CreateClient();
		var hidden = await Scenes.CreateSceneAsync(roomDbref: "", ownerDbref: "#99999", title: $"PosesHidden {Guid.NewGuid():N}");

		var response = await http.GetAsync($"api/scenes/{hidden.Id}/poses");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	// ── GET /api/scenes/{id}/members ───────────────────────────────────────────

	[Test]
	public async Task GetMembers_IncludesAddedMember()
	{
		var http = CreateClient();
		var scene = await NewPublicSceneAsync($"Members {Guid.NewGuid():N}");
		await Scenes.AddMemberAsync(scene.Id, God, "participant");

		var members = await http.GetFromJsonAsync<List<SceneMemberDto>>($"api/scenes/{scene.Id}/members");

		await Assert.That(members).IsNotNull();
		await Assert.That(members!.Any(m => m.Role == "participant")).IsTrue();
		await Assert.That(members.All(m => m.SceneId == scene.Id)).IsTrue();
	}

	// ── GET /api/scenes/{id}/cast and /tags ────────────────────────────────────

	[Test]
	public async Task GetCast_ReturnsDisplayPersonas()
	{
		var http = CreateClient();
		var scene = await NewPublicSceneAsync($"Cast {Guid.NewGuid():N}");
		await Scenes.AddPoseAsync(scene.Id, God, "Guard Captain", God, "pose", [], "stands watch.");

		var cast = await http.GetFromJsonAsync<List<string>>($"api/scenes/{scene.Id}/cast");

		await Assert.That(cast).IsNotNull();
		await Assert.That(cast!.Contains("Guard Captain")).IsTrue();
	}

	[Test]
	public async Task GetTags_ReturnsDistinctPoseTags()
	{
		var http = CreateClient();
		var scene = await NewPublicSceneAsync($"Tags {Guid.NewGuid():N}");
		await Scenes.AddPoseAsync(scene.Id, God, "", God, "pose", ["combat"], "swings a sword.");

		var tags = await http.GetFromJsonAsync<List<string>>($"api/scenes/{scene.Id}/tags");

		await Assert.That(tags).IsNotNull();
		await Assert.That(tags!.Contains("combat")).IsTrue();
	}
}
