using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Integration.Scenes;

/// <summary>
/// Behaviour tests for the graph-native Scene System against the configured DB backend, driven entirely
/// over the WIRE — the wizard-only <c>scene…()</c> side-effect functions (writes) and the <c>scene…()</c>
/// read functions (reads). The Scene plugin now owns <c>ISceneService</c> inside its own (collectible)
/// AssemblyLoadContext, so the host cannot name it any more; the engine's softcode surface is the
/// host-visible seam. These exercises run identically on all three providers (arangodb / memgraph /
/// surrealdb, selected by <c>SHARPMUSH_DATABASE_PROVIDER</c>). Object references use <c>#1</c> (the seeded
/// God object) so the resolve → edge → name-snapshot mechanism is exercised against a real vertex.
///
/// <para>Each scene is made <c>public</c> immediately after creation so the read functions' visibility
/// check (God owns these scenes anyway) never masks a behaviour assertion.</para>
/// </summary>
[NotInParallel]
public class SceneServiceIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactory { get; init; }

	private IMUSHCodeParser FunctionParser => WebAppFactory.FunctionParser;

	private const string God = "#1"; // seeded object — used for owner/author/origin in these tests

	/// <summary>Evaluates a softcode expression as God and returns its trimmed plain text.</summary>
	private async Task<string> Eval(string expression) =>
		(await FunctionParser.FunctionParse(MModule.single(expression)))!.Message!.ToPlainText().Trim();

	/// <summary>Creates a fresh PUBLIC scene owned by God and returns its id.</summary>
	private async Task<string> NewSceneAsync(string title = "Test Scene")
	{
		var id = await Eval($"scenecreate(,{God},{title} {Guid.NewGuid():N})");
		await Eval($"sceneset({id},public,1)");
		return id;
	}

	// ── Scene CRUD + meta ───────────────────────────────────────────────────────

	[Test]
	public async Task CreateScene_AssignsId_AndSnapshotsOwnerName()
	{
		var id = await NewSceneAsync($"Create");

		await Assert.That(id).IsNotEmpty();
		await Assert.That(id).DoesNotStartWith("#-1");
		await Assert.That(await Eval($"scene({id}, ownername)")).IsNotEmpty(); // resolved + snapshotted from #1
	}

	[Test]
	public async Task GetScene_RoundTrips()
	{
		var id = await NewSceneAsync($"RoundTrip");

		await Assert.That(await Eval($"scene({id}, id)")).IsEqualTo(id);
	}

	[Test]
	public async Task GetScene_Missing_ReturnsNotFound()
	{
		var got = await Eval($"scene(does-not-exist-{Guid.NewGuid():N}, status)");
		await Assert.That(got).StartsWith("#-1");
	}

	[Test]
	public async Task SetSceneMeta_Status_RoutesToFirstClassField()
	{
		var id = await NewSceneAsync();

		await Eval($"sceneset({id},status,active)");

		await Assert.That(await Eval($"scene({id}, status)")).IsEqualTo("active");
	}

	[Test]
	public async Task SetSceneMeta_CustomKey_RoutesToMetaBag()
	{
		var id = await NewSceneAsync();

		await Eval($"sceneset({id},genre,noir)");

		await Assert.That(await Eval($"scene({id}, genre)")).IsEqualTo("noir");
	}

	// ── Poses: order, content, edit/undo ─────────────────────────────────────────

	[Test]
	public async Task AddPoses_AreReturnedInChainOrder()
	{
		var id = await NewSceneAsync();

		var p1 = await Eval($"sceneaddpose({id},{God},,{God},pose,,First pose.)");
		var p2 = await Eval($"sceneaddpose({id},{God},,{God},pose,,Second pose.)");
		await Assert.That(p1).DoesNotStartWith("#-1");
		await Assert.That(p2).DoesNotStartWith("#-1");

		var poses = await Eval($"sceneposes({id})");
		var ids = poses.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		await Assert.That(ids.Length).IsEqualTo(2);
		await Assert.That(await Eval($"scenepose({id}, {ids[0]}, content)")).Contains("First");
		await Assert.That(await Eval($"scenepose({id}, {ids[1]}, content)")).Contains("Second");
	}

	[Test]
	public async Task EditPose_VersionsContent_AndUndoRestores()
	{
		var id = await NewSceneAsync();
		var poseId = await Eval($"sceneaddpose({id},{God},,{God},pose,,Original.)");
		await Assert.That(poseId).DoesNotStartWith("#-1");

		await Eval($"sceneeditpose({poseId},{God},Edited.)");
		await Assert.That(await Eval($"scenepose({id}, {poseId}, content)")).Contains("Edited");
		await Assert.That(int.Parse(await Eval($"scenepose({id}, {poseId}, editcount)"))).IsGreaterThan(1);

		await Eval($"sceneundo({poseId})");
		await Assert.That(await Eval($"scenepose({id}, {poseId}, content)")).Contains("Original");

		await Eval($"sceneredo({poseId})");
		await Assert.That(await Eval($"scenepose({id}, {poseId}, content)")).Contains("Edited");
	}

	[Test]
	public async Task ShowAs_IsSnapshottedOnThePose()
	{
		var id = await NewSceneAsync();

		var poseId = await Eval($"sceneaddpose({id},{God},Guard Captain,{God},pose,,stands watch.)");
		await Assert.That(poseId).DoesNotStartWith("#-1");

		await Assert.That(await Eval($"scenepose({id}, {poseId}, showas)")).IsEqualTo("Guard Captain");
		// author snapshot is the real player, not the persona
		await Assert.That(await Eval($"scenepose({id}, {poseId}, authorname)")).IsNotEqualTo("Guard Captain");
	}

	[Test]
	public async Task DeletePose_SoftDeletes()
	{
		var id = await NewSceneAsync();
		var poseId = await Eval($"sceneaddpose({id},{God},,{God},pose,,Doomed.)");
		await Assert.That(poseId).DoesNotStartWith("#-1");

		await Eval($"scenedelpose({poseId})");
		await Assert.That(await Eval($"scenepose({id}, {poseId}, deleted)")).IsEqualTo("1");
	}

	// ── Membership + focus ───────────────────────────────────────────────────────

	[Test]
	public async Task AddMember_ThenGetMembers_IncludesPlayer()
	{
		var id = await NewSceneAsync();

		await Eval($"sceneaddmember({id},{God},participant)");
		await Assert.That(await Eval($"scenemember({id}, {God}, role)")).IsEqualTo("participant");

		var members = await Eval($"scenemembers({id})");
		await Assert.That(members).Contains(God);
	}

	[Test]
	public async Task SetFocus_ThenGetCurrentScene_ReturnsTheScene()
	{
		var id = await NewSceneAsync();
		await Eval($"sceneaddmember({id},{God},participant)");

		await Eval($"scenesetfocus({God},{id})");

		await Assert.That(await Eval($"scenefocus({God})")).IsEqualTo(id);
	}

	[Test]
	public async Task SetFocus_OnNonMember_AutoJoinsAndFocuses()
	{
		// Focusing a player who is NOT yet a member must auto-create a (role-less) member edge and stick,
		// identically on all three providers. SurrealDB previously only UPDATEd an existing edge, so the
		// focus silently no-opped for a non-member; ArangoDB/Memgraph created the edge. This pins the
		// Arango behavior across the board (no explicit sceneaddmember first).
		var id = await NewSceneAsync("NonMember focus");

		await Eval($"scenesetfocus({God},{id})");

		await Assert.That(await Eval($"scenefocus({God})")).IsEqualTo(id);
		await Assert.That(await Eval($"scenemembers({id})")).Contains(God);
	}
}
