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

	[Test]
	public async Task CreateScene_AssignsId_AndSnapshotsOwnerName()
	{
		var id = await NewSceneAsync($"Create");

		await Assert.That(id).IsNotEmpty();
		await Assert.That(id).DoesNotStartWith("#-1");
		await Assert.That(await Eval($"scene({id}, ownername)")).IsNotEmpty();
	}

	/// <summary>
	/// A new scene is public.
	///
	/// <para>Every provider created them member-only. That suits a scene begun at a terminal among
	/// people already in the room, and it is wrong everywhere else: the portal's whole scene surface
	/// is a browser of other people's roleplay, and a starter watched their scene appear in a list
	/// nobody else could see. The command to change it — <c>+scene/private</c>'s opposite — is not
	/// one the portal mentions anywhere, so the state was both wrong and unreachable. Scenes that
	/// should not be watched are now the case that asks for a command.</para>
	/// </summary>
	[Test]
	public async Task CreateScene_IsPublic()
	{
		// Created raw, without NewSceneAsync's explicit sceneset — the point is what the provider
		// chooses when nobody says.
		var id = await Eval($"scenecreate(,{God},Public {Guid.NewGuid():N})");

		await Assert.That(await Eval($"scene({id}, public)")).IsEqualTo("1")
			.Because("a scene nobody can find is not a scene anyone can join");
	}

	/// <summary>
	/// A scene id is a bare key on every provider. It is not an internal detail: players type it
	/// (<c>+scene 1</c>, <c>+scene/join 1</c>), it is a path segment in <c>/scenes/{id}/live</c>, and
	/// it is stored in player attributes. SurrealDB used to hand back its own record id verbatim, so
	/// production (surrealdb) showed <c>scene:1</c> while this suite's default provider (arangodb)
	/// showed <c>1</c> — the tests and the running game disagreed about the shape of the thing a
	/// player is asked to type, and a colon rode into every scene URL.
	/// </summary>
	[Test]
	public async Task CreateScene_AssignsAProviderNeutralId()
	{
		var id = await NewSceneAsync("IdShape");

		await Assert.That(id).DoesNotContain(":");
	}

	/// <summary>
	/// <c>scene(&lt;id&gt;, room)</c> answers with an objid, not a bare dbref, so it can be compared
	/// against <c>loc()</c> — which is what any "is the poser standing in this scene's room?" test has
	/// to do. The two spellings did not match: <c>loc()</c> carries the creation stamp and this did
	/// not, so such a comparison was quietly always false. Objid is also the project's rule for a
	/// stored reference, since a bare dbref number gets recycled.
	///
	/// <para>Note the deliberate asymmetry with <c>owner</c>/<c>starter</c>, which stay bare: the
	/// package's own <c>FUN`OWNS</c> compares <c>scene(&lt;id&gt;,owner)</c> against <c>%#</c>, which
	/// is the short form.</para>
	/// </summary>
	[Test]
	public async Task GetScene_Room_IsAnObjid_ComparableToLoc()
	{
		var id = await Eval($"scenecreate(#0,{God},RoomObjid {Guid.NewGuid():N})");
		await Eval($"sceneset({id},public,1)");

		await Assert.That(await Eval($"scene({id}, room)")).IsEqualTo(await Eval("objid(#0)"));
	}

	/// <summary>
	/// Setting a member's role leaves their focus and their persona alone.
	///
	/// <para>SurrealDB stores both ON the membership edge — focus as <c>isCurrent</c>, the
	/// <c>+scene/as</c> persona as <c>showAs</c> — and its <c>SetMember</c> deleted and recreated that
	/// edge, hard-setting both back to empty. ArangoDB updated in place and kept them, so the two
	/// providers disagreed about what re-roling somebody costs, and production is the one that lost
	/// data. Losing focus is not cosmetic: nearly every owner verb acts on <c>scenefocus(%#)</c> and
	/// does nothing without one, and the capture hooks need it to record a pose at all.</para>
	/// </summary>
	[Test]
	public async Task SetMember_KeepsFocusAndPersona()
	{
		var id = await NewSceneAsync("MemberRole");
		await Eval($"sceneaddmember({id},{God},owner)");
		await Eval($"scenesetfocus({God},{id})");
		await Eval($"sceneshowas({id},{God},The Stranger)");

		await Assert.That(await Eval($"scenefocus({God})")).IsEqualTo(id)
			.Because("the focus set above is the precondition this test exists to protect");

		await Eval($"sceneaddmember({id},{God},participant)");

		await Assert.That(await Eval($"scenefocus({God})")).IsEqualTo(id);
		await Assert.That(await Eval($"scenemember({id},{God},showas)")).IsEqualTo("The Stranger");
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
