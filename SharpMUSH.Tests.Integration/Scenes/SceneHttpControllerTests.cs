using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SharpMUSH.Tests.Integration.Scenes;

/// <summary>
/// HTTP-level integration tests for the Scene plugin's read-only <c>SceneController</c> (the Scene REST API
/// the Blazor WASM portal consumes). Requests travel through the full ASP.NET Core pipeline via the
/// in-process <see cref="ServerWebAppFactory"/>.
///
/// <para>The host no longer references <c>ISceneService</c> (it lives inside the Scene plugin's collectible
/// ALC), so scenes are seeded over the WIRE — through the wizard-only <c>scene…()</c> side-effect functions
/// evaluated as God (#1) — rather than via DI. The controller surface itself is exercised exactly as the
/// portal sees it.</para>
///
/// DebugAuthenticationHandler auto-authenticates every request as the bootstrap admin — player #1 (God),
/// whose <c>character_dbref</c> claim is <c>#1</c>. Visibility is therefore exercised against that caller:
/// a private scene owned by a non-existent object (no #1 owner/membership) is hidden (404), while a private
/// scene owned by #1 is visible to #1.
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

	private IMUSHCodeParser FunctionParser => factory.FunctionParser;

	/// <summary>Evaluates a softcode expression as God and returns its trimmed plain text.</summary>
	private async Task<string> Eval(string expression) =>
		(await FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText().Trim();

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

	/// <summary>Creates an active, public scene owned by God (over the wire) and returns its id.</summary>
	private async Task<string> NewPublicSceneAsync(string title)
	{
		var id = await Eval($"scenecreate(,{God},{title} {Guid.NewGuid():N})");
		await Eval($"sceneset({id},status,active)");
		await Eval($"sceneset({id},public,1)");
		return id;
	}

	[Test]
	public async Task GetScene_PublicScene_Returns200WithMatchingId()
	{
		var http = CreateClient();
		var sceneId = await NewPublicSceneAsync("Public");

		var response = await http.GetAsync($"api/scenes/{sceneId}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var dto = await response.Content.ReadFromJsonAsync<SceneDto>();
		await Assert.That(dto).IsNotNull();
		await Assert.That(dto!.Id).IsEqualTo(sceneId);
		await Assert.That(dto.IsPublic).IsTrue();
		await Assert.That(dto.StartedAt).IsGreaterThan(0);
	}

	private record PoseTypeDto(string Key, string Label, string Presentation, string Tone, string Icon, bool Hidden, int Order);
	private record PoseTypesDto(List<PoseTypeDto> Types, List<JsonElement> Problems, List<string> Hidden);

	[Test]
	public async Task GetTypes_ReturnsTheSceneLoggersPoseTypesInOrder()
	{
		var response = await CreateClient().GetAsync("api/scenes/types");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var dto = (await response.Content.ReadFromJsonAsync<PoseTypesDto>())!;
		await Assert.That(dto.Types.Take(3).Select(t => t.Key)).IsEquivalentTo(new[] { "ic", "ooc", "narration" });
		await Assert.That(dto.Types.Single(t => t.Key == "ooc"))
			.IsEqualTo(new PoseTypeDto("ooc", "Out of character", "band", "secondary", "", false, 20));
		await Assert.That(dto.Hidden.All(key => dto.Types.Any(t => t.Key == key))).IsTrue();
	}

	private record SceneDetailDto(string Key, string Label, string Hint, List<string> Choices, bool Many, bool Listed, string Tone, int Order);
	private record SceneDetailsDto(List<SceneDetailDto> Details, List<JsonElement> Problems);
	private record SceneDetailValueDto(string Value, int Scenes);
	private record SceneDetailValuesDto(string Key, List<SceneDetailValueDto> Values);

	/// <summary>Not in parallel: it lists a detail on the game's own catalogue for as long as it runs.</summary>
	[Test]
	[NotInParallel]
	public async Task GetDetails_ReturnsTheSceneLoggersDetailsInOrder()
	{
		var registry = (IPackageRegistryService)factory.Services.GetRequiredService<ISharpDatabase>();
		var logger = DBRef.Parse((await registry.GetPackageObjectsAsync("scene")).Single(o => o.Ref == "logger").Objid);
		var key = $"d{Guid.NewGuid():N}"[..12];
		await Eval($$"""attrib_set(#{{logger.Number}}/DETAIL`{{key}},lit({"label":"Era","hint":"When it is set.","many":true,"tone":"Warning","order":-1000}))""");
		try
		{
			var response = await CreateClient().GetAsync("api/scenes/details");

			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
			var dto = (await response.Content.ReadFromJsonAsync<SceneDetailsDto>())!;
			var first = dto.Details.First();
			await Assert.That(first.Key).IsEqualTo(key).Because("the lowest order comes first");
			await Assert.That(first.Label).IsEqualTo("Era");
			await Assert.That(first.Hint).IsEqualTo("When it is set.");
			await Assert.That(first.Choices).IsEmpty();
			await Assert.That(first.Many).IsTrue();
			await Assert.That(first.Listed).IsFalse();
			await Assert.That(first.Tone).IsEqualTo("warning").Because("a tone is kept lower case");
		}
		finally
		{
			await Eval($"attrib_set(#{logger.Number}/DETAIL`{key})");
		}
	}

	/// <summary>Lists detail <paramref name="key"/> on the game's catalogue, taking several values, for as long as <paramref name="body"/> runs.</summary>
	private async Task WithDetailAsync(string key, string json, Func<Task> body)
	{
		var registry = (IPackageRegistryService)factory.Services.GetRequiredService<ISharpDatabase>();
		var logger = DBRef.Parse((await registry.GetPackageObjectsAsync("scene")).Single(o => o.Ref == "logger").Objid);
		await Eval($"attrib_set(#{logger.Number}/DETAIL`{key},lit({json}))");
		try
		{
			await body();
		}
		finally
		{
			await Eval($"attrib_set(#{logger.Number}/DETAIL`{key})");
		}
	}

	private async Task<List<string>> SceneIdsAsync(string query)
	{
		var response = await CreateClient().GetAsync($"api/scenes?filter=recent&{query}");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var found = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
		return [.. found!.Select(s => s.GetProperty("id").GetString() ?? "")];
	}

	[Test]
	public async Task ListScenes_Detail_KeepsTheScenesGivingThatValue()
	{
		var key = $"d{Guid.NewGuid():N}"[..12];
		var word = $"W{Guid.NewGuid():N}"[..10];
		await WithDetailAsync(key, """{"label":"Canon","many":true}""", async () =>
		{
			var both = await NewPublicSceneAsync("DetailBoth");
			await Eval($"sceneset({both},{key},Other|{word})");
			var longer = await NewPublicSceneAsync("DetailLonger");
			await Eval($"sceneset({longer},{key},{word}s)");

			await Assert.That(await SceneIdsAsync($"detail={key}&value={word.ToLowerInvariant()}")).IsEquivalentTo(new[] { both })
				.Because("one of a detail's values, ignoring case, and never a value it is only part of");
			await Assert.That(await SceneIdsAsync($"detail={key.ToUpperInvariant()}&value={word}s")).IsEquivalentTo(new[] { longer });
		});

		var unpaired = await CreateClient().GetAsync($"api/scenes?filter=recent&detail={key}");
		await Assert.That(unpaired.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task GetDetailValues_CountsTheScenesGivingEachValue_OfTheListedDetails()
	{
		var key = $"d{Guid.NewGuid():N}"[..12];
		var unlisted = $"d{Guid.NewGuid():N}"[..12];
		var (common, rare, hidden) = ($"C{Guid.NewGuid():N}"[..10], $"R{Guid.NewGuid():N}"[..10], $"H{Guid.NewGuid():N}"[..10]);
		await WithDetailAsync(unlisted, """{"label":"Mood"}""", () => WithDetailAsync(key, """{"label":"Canon","many":true,"listed":true}""", async () =>
		{
			var first = await NewPublicSceneAsync("ValuesFirst");
			await Eval($"sceneset({first},{key},{common}|{rare}|{common})");
			await Eval($"sceneset({first},{unlisted},Calm)");
			var second = await NewPublicSceneAsync("ValuesSecond");
			await Eval($"sceneset({second},{key},{common.ToLowerInvariant()})");
			var secret = await Eval($"scenecreate(,#99999,ValuesHidden {Guid.NewGuid():N})");
			await Eval($"sceneset({secret},public,0)");
			await Eval($"sceneset({secret},{key},{hidden})");

			var response = await CreateClient().GetAsync("api/scenes/details/values");

			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
			var details = (await response.Content.ReadFromJsonAsync<List<SceneDetailValuesDto>>())!;
			await Assert.That(details.Any(d => d.Key == unlisted)).IsFalse().Because("only the details cards show are browsed");
			var values = details.Single(d => d.Key == key).Values;
			await Assert.That(values.Select(v => (v.Value.ToLowerInvariant(), v.Scenes)))
				.IsEquivalentTo(new[] { (common.ToLowerInvariant(), 2), (rare.ToLowerInvariant(), 1) }, TUnit.Assertions.Enums.CollectionOrdering.Matching)
				.Because("each scene counts once per value, most scenes first, and a scene the caller can't see not at all");
		}));
	}

	[Test]
	public async Task ListScenes_Search_FindsASceneByItsDetail()
	{
		var verse = $"Verse{Guid.NewGuid():N}";
		var sceneId = await Eval($"scenecreate(,{God},DetailSearch {Guid.NewGuid():N})");
		await Eval($"sceneset({sceneId},verse,{verse})");

		var response = await CreateClient().GetAsync($"api/scenes?filter=recent&search={verse}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var found = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
		await Assert.That(found!.Select(s => s.GetProperty("id").GetString() ?? "")).IsEquivalentTo(new[] { sceneId });
	}

	[Test]
	public async Task GetScene_Unknown_Returns404()
	{
		var http = CreateClient();

		var response = await http.GetAsync($"api/scenes/does-not-exist-{Guid.NewGuid():N}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task GetScene_PrivateSceneCallerIsMember_Returns200()
	{
		var http = CreateClient();
		// Private scene; the DebugAuth caller (#1) is a member. Made private explicitly — scenes are
		// created watchable now, so privacy is the case a test has to ask for.
		var sceneId = await Eval($"scenecreate(,{God},PrivMember {Guid.NewGuid():N})");
		await Eval($"sceneset({sceneId},public,0)");
		await Assert.That(await Eval($"scene({sceneId}, public)")).IsEqualTo("0");
		await Eval($"sceneaddmember({sceneId},{God},participant)");

		var response = await http.GetAsync($"api/scenes/{sceneId}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	[Test]
	public async Task GetScene_PrivateSceneOwnedByCaller_Returns200()
	{
		var http = CreateClient();
		// Private (default IsPublic=false) scene owned by the caller (#1), with NO membership edge — so the
		// only thing that can make it visible is the owner check. Creating a scene does not add its owner as
		// a member, which is what isolates that path here.
		var sceneId = await Eval($"scenecreate(,{God},PrivOwner {Guid.NewGuid():N})");
		await Eval($"sceneset({sceneId},public,0)");
		await Assert.That(await Eval($"scene({sceneId}, public)")).IsEqualTo("0");

		var response = await http.GetAsync($"api/scenes/{sceneId}");

		// Regression: the claim carries the objid (#1:creation) while the owner edge resolves to a bare #1,
		// so comparing the two as strings hid EVERY non-public scene from its own owner.
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var dto = await response.Content.ReadFromJsonAsync<SceneDto>();
		await Assert.That(dto!.OwnerDbref).IsEqualTo(God);
	}

	[Test]
	public async Task ListScenes_IncludesPrivateSceneOwnedByCaller()
	{
		var http = CreateClient();
		var sceneId = await Eval($"scenecreate(,{God},PrivOwnerList {Guid.NewGuid():N})");
		await Eval($"sceneset({sceneId},public,0)");
		await Assert.That(await Eval($"scene({sceneId}, public)")).IsEqualTo("0");

		var scenes = await http.GetFromJsonAsync<List<SceneDto>>("api/scenes?filter=recent&count=200");

		await Assert.That(scenes).IsNotNull();
		await Assert.That(scenes!.Any(s => s.Id == sceneId)).IsTrue();
	}

	[Test]
	public async Task GetScene_PrivateSceneNotOwnedByCaller_Returns404()
	{
		var http = CreateClient();
		// Owned by a non-existent object → OwnerDbref resolves to null, and #1 holds no
		// membership, so #1 must not see it. Private + not owner + not member ⇒ 404.
		var sceneId = await Eval($"scenecreate(,#99999,PrivHidden {Guid.NewGuid():N})");
		await Eval($"sceneset({sceneId},public,0)");

		var response = await http.GetAsync($"api/scenes/{sceneId}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task ListScenes_RecentFilter_IncludesPublicScene()
	{
		var http = CreateClient();
		var sceneId = await NewPublicSceneAsync("List");

		var scenes = await http.GetFromJsonAsync<List<SceneDto>>("api/scenes?filter=recent&count=200");

		await Assert.That(scenes).IsNotNull();
		await Assert.That(scenes!.Any(s => s.Id == sceneId)).IsTrue();
	}

	[Test]
	public async Task ListScenes_DoesNotLeakHiddenPrivateScene()
	{
		var http = CreateClient();
		var hiddenId = await Eval($"scenecreate(,#99999,Leak {Guid.NewGuid():N})");
		await Eval($"sceneset({hiddenId},public,0)");

		var scenes = await http.GetFromJsonAsync<List<SceneDto>>("api/scenes?filter=recent&count=200");

		await Assert.That(scenes).IsNotNull();
		await Assert.That(scenes!.All(s => s.Id != hiddenId)).IsTrue();
	}

	[Test]
	public async Task GetPoses_ReturnsPosesInChainOrder()
	{
		var http = CreateClient();
		var sceneId = await NewPublicSceneAsync("Poses");
		await Eval($"sceneaddpose({sceneId},{God},,{God},ic,pose,,First pose.)");
		await Eval($"sceneaddpose({sceneId},{God},,{God},ic,pose,,Second pose.)");

		var poses = await http.GetFromJsonAsync<List<ScenePoseDto>>($"api/scenes/{sceneId}/poses");

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
		var hiddenId = await Eval($"scenecreate(,#99999,PosesHidden {Guid.NewGuid():N})");
		await Eval($"sceneset({hiddenId},public,0)");

		var response = await http.GetAsync($"api/scenes/{hiddenId}/poses");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task GetMembers_IncludesAddedMember()
	{
		var http = CreateClient();
		var sceneId = await NewPublicSceneAsync("Members");
		await Eval($"sceneaddmember({sceneId},{God},participant)");

		var members = await http.GetFromJsonAsync<List<SceneMemberDto>>($"api/scenes/{sceneId}/members");

		await Assert.That(members).IsNotNull();
		await Assert.That(members!.Any(m => m.Role == "participant")).IsTrue();
		await Assert.That(members.All(m => m.SceneId == sceneId)).IsTrue();
	}

	[Test]
	public async Task GetCast_ReturnsDisplayPersonas()
	{
		var http = CreateClient();
		var sceneId = await NewPublicSceneAsync("Cast");
		await Eval($"sceneaddpose({sceneId},{God},Guard Captain,{God},ic,pose,,stands watch.)");

		var cast = await http.GetFromJsonAsync<List<string>>($"api/scenes/{sceneId}/cast");

		await Assert.That(cast).IsNotNull();
		await Assert.That(cast!.Contains("Guard Captain")).IsTrue();
	}

	[Test]
	public async Task GetTags_ReturnsDistinctPoseTags()
	{
		var http = CreateClient();
		var sceneId = await NewPublicSceneAsync("Tags");
		await Eval($"sceneaddpose({sceneId},{God},,{God},ic,pose,combat,swings a sword.)");

		var tags = await http.GetFromJsonAsync<List<string>>($"api/scenes/{sceneId}/tags");

		await Assert.That(tags).IsNotNull();
		await Assert.That(tags!.Contains("combat")).IsTrue();
	}
}
