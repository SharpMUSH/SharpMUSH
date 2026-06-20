using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Scene.Functions;

/// <summary>
/// The <c>scene…</c> softcode functions — the Phase-5 plugin counterpart of the engine's former
/// <c>Functions</c> partial. Reads are <c>Regular</c>; writes are <c>WizardOnly|HasSideFX</c> and honour
/// the engine's <c>FunctionSideEffects</c> config. Every function resolves <c>ISceneService</c> (and, where
/// needed, <c>IMediator</c> / the config) from <c>parser.ServiceProvider</c> at call time — the supported,
/// unload-friendly plugin pattern.
/// </summary>
public static class SceneFunctions
{
	// ── Shared helpers ──────────────────────────────────────────────────────────

	private const string SceneNotFound = "#-1 NOT FOUND";
	private const string ScenePermission = "#-1 PERMISSION";

	/// <summary>
	/// Returns true when the viewer may see this scene. Public scenes are visible
	/// to anyone; non-public scenes are visible only to their members. Mirrors the
	/// wiki draft-visibility convention.
	/// </summary>
	private static async ValueTask<bool> SceneVisibleToAsync(ISceneService service, Library.Models.Scene.Scene scene, string viewerDbref)
	{
		if (scene.IsPublic)
		{
			return true;
		}

		if (string.IsNullOrEmpty(viewerDbref))
		{
			return false;
		}

		var member = await service.GetMemberAsync(scene.Id, viewerDbref);
		return member.IsT0;
	}

	private static async ValueTask<string> ViewerDbrefAsync(IMUSHCodeParser parser)
	{
		var mediator = parser.ServiceProvider.GetRequiredService<IMediator>();
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		return executor.Object().DBRef.ToString();
	}

	/// <summary>Guard for side-effect (write) functions: false ⇒ side effects are disabled in config.</summary>
	private static bool SideEffectsEnabled(IMUSHCodeParser parser)
		=> parser.ServiceProvider.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>()
			.CurrentValue.Function.FunctionSideEffects;

	// ── Reads ───────────────────────────────────────────────────────────────────

	/// <summary>
	/// scene(&lt;id&gt;[, &lt;field&gt;])
	/// Returns a field of a scene. Fields: status (default), id, public, istemp,
	/// scheduledfor, startedat, lastactivityat, posecount, owner, ownername,
	/// starter, startername, room, roomname, title, summary, or any meta key.
	/// </summary>
	[SharpFunction(Name = "scene", MinArgs = 1, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["id", "field"])]
	public static async ValueTask<CallState> Scene(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var id = args["0"].Message!.ToPlainText().Trim();
		var field = args.TryGetValue("1", out var fieldArg)
			? fieldArg.Message!.ToPlainText().Trim().ToLowerInvariant()
			: "status";

		var service = parser.ServiceProvider.GetRequiredService<ISceneService>();
		var lookup = await service.GetSceneAsync(id);
		if (lookup.IsT1)
		{
			return new CallState(SceneNotFound);
		}

		var scene = lookup.AsT0;
		if (!await SceneVisibleToAsync(service, scene, await ViewerDbrefAsync(parser)))
		{
			return new CallState(ScenePermission);
		}

		return field switch
		{
			"status" => new CallState(scene.Status),
			"id" => new CallState(scene.Id),
			"public" => new CallState(scene.IsPublic ? "1" : "0"),
			"istemp" => new CallState(scene.IsTempRoom ? "1" : "0"),
			"scheduledfor" => new CallState(scene.ScheduledFor?.ToString() ?? string.Empty),
			"startedat" => new CallState(scene.StartedAt.ToString()),
			"lastactivityat" => new CallState(scene.LastActivityAt.ToString()),
			"posecount" => new CallState(scene.PoseCount.ToString()),
			"owner" => new CallState(scene.OwnerDbref ?? string.Empty),
			"ownername" => new CallState(scene.OwnerName),
			"starter" => new CallState(scene.StarterDbref ?? string.Empty),
			"startername" => new CallState(scene.StarterName),
			"room" => new CallState(scene.RoomDbref ?? string.Empty),
			"roomname" => new CallState(scene.RoomName),
			_ => new CallState(scene.Meta.TryGetValue(field, out var metaVal)
				? metaVal
				: "#-1 UNKNOWN SCENE FIELD"),
		};
	}

	/// <summary>
	/// scenelist([&lt;filter&gt;][, &lt;from&gt;][, &lt;to&gt;])
	/// Returns a space-separated list of scene ids. Filter: active | recent |
	/// scheduled | mine (default recent). The optional UTC-millis from/to bounds
	/// window the scheduled filter.
	/// </summary>
	[SharpFunction(Name = "scenelist", MinArgs = 0, MaxArgs = 3,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["filter", "from", "to"])]
	public static async ValueTask<CallState> SceneList(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var filter = args.TryGetValue("0", out var filterArg)
			? filterArg.Message!.ToPlainText().Trim().ToLowerInvariant()
			: "recent";
		if (filter.Length == 0)
		{
			filter = "recent";
		}

		long? from = null;
		if (args.TryGetValue("1", out var fromArg))
		{
			var fromText = fromArg.Message!.ToPlainText().Trim();
			if (fromText.Length > 0)
			{
				if (!long.TryParse(fromText, out var parsed))
				{
					return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, "SCENELIST"));
				}
				from = parsed;
			}
		}

		long? to = null;
		if (args.TryGetValue("2", out var toArg))
		{
			var toText = toArg.Message!.ToPlainText().Trim();
			if (toText.Length > 0)
			{
				if (!long.TryParse(toText, out var parsed))
				{
					return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, "SCENELIST"));
				}
				to = parsed;
			}
		}

		var service = parser.ServiceProvider.GetRequiredService<ISceneService>();
		var viewer = await ViewerDbrefAsync(parser);
		var scenes = await service.ListScenesAsync(filter, viewer, from, to);

		return new CallState(string.Join(" ", scenes.Select(s => s.Id)));
	}

	/// <summary>
	/// scenewhere(&lt;roomDbref&gt;)
	/// Returns the id of the active scene bound to a room, or #-1 NOT FOUND.
	/// </summary>
	[SharpFunction(Name = "scenewhere", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["roomDbref"])]
	public static async ValueTask<CallState> SceneWhere(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var room = parser.CurrentState.Arguments["0"].Message!.ToPlainText().Trim();
		var service = parser.ServiceProvider.GetRequiredService<ISceneService>();
		var lookup = await service.GetActiveSceneInRoomAsync(room);
		if (lookup.IsT1)
		{
			return new CallState(SceneNotFound);
		}

		var scene = lookup.AsT0;
		if (!await SceneVisibleToAsync(service, scene, await ViewerDbrefAsync(parser)))
		{
			return new CallState(ScenePermission);
		}

		return new CallState(scene.Id);
	}

	/// <summary>
	/// sceneposes(&lt;id&gt;[, &lt;authorDbref&gt;][, &lt;count&gt;])
	/// Returns a space-separated list of pose ids in pose order, optionally
	/// filtered to one author and/or limited to the last &lt;count&gt;.
	/// </summary>
	[SharpFunction(Name = "sceneposes", MinArgs = 1, MaxArgs = 3,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["scene", "authorDbref", "count"])]
	public static async ValueTask<CallState> ScenePoses(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var id = args["0"].Message!.ToPlainText().Trim();

		string? author = null;
		if (args.TryGetValue("1", out var authorArg))
		{
			var authorText = authorArg.Message!.ToPlainText().Trim();
			if (authorText.Length > 0)
			{
				author = authorText;
			}
		}

		int? count = null;
		if (args.TryGetValue("2", out var countArg))
		{
			var countText = countArg.Message!.ToPlainText().Trim();
			if (countText.Length > 0)
			{
				if (!int.TryParse(countText, out var parsedCount) || parsedCount < 1)
				{
					return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, "SCENEPOSES"));
				}
				count = parsedCount;
			}
		}

		var service = parser.ServiceProvider.GetRequiredService<ISceneService>();
		var lookup = await service.GetSceneAsync(id);
		if (lookup.IsT1)
		{
			return new CallState(SceneNotFound);
		}

		if (!await SceneVisibleToAsync(service, lookup.AsT0, await ViewerDbrefAsync(parser)))
		{
			return new CallState(ScenePermission);
		}

		var poses = await service.GetPosesAsync(id, author, count);
		if (poses.IsT1)
		{
			return new CallState(SceneNotFound);
		}

		return new CallState(string.Join(" ", poses.AsT0.Select(p => p.Id)));
	}

	/// <summary>
	/// scenepose(&lt;scene&gt;, &lt;poseId&gt;[, &lt;field&gt;])
	/// Returns a field of a pose. Fields: content (default), markup, id, scene,
	/// author, authorname, showas, origin, originname, source, tags, createdat,
	/// deleted, editcount, lasteditedat, lasteditor, lasteditorname, or any meta key.
	/// </summary>
	[SharpFunction(Name = "scenepose", MinArgs = 2, MaxArgs = 3,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["scene", "poseId", "field"])]
	public static async ValueTask<CallState> ScenePoseFn(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var sceneId = args["0"].Message!.ToPlainText().Trim();
		var poseId = args["1"].Message!.ToPlainText().Trim();
		var field = args.TryGetValue("2", out var fieldArg)
			? fieldArg.Message!.ToPlainText().Trim().ToLowerInvariant()
			: "content";

		var service = parser.ServiceProvider.GetRequiredService<ISceneService>();
		var sceneLookup = await service.GetSceneAsync(sceneId);
		if (sceneLookup.IsT1)
		{
			return new CallState(SceneNotFound);
		}

		if (!await SceneVisibleToAsync(service, sceneLookup.AsT0, await ViewerDbrefAsync(parser)))
		{
			return new CallState(ScenePermission);
		}

		var poseLookup = await service.GetPoseAsync(poseId);
		if (poseLookup.IsT1)
		{
			return new CallState(SceneNotFound);
		}

		var pose = poseLookup.AsT0;
		if (pose.SceneId != sceneId)
		{
			return new CallState(SceneNotFound);
		}

		return field switch
		{
			"content" => new CallState(pose.Content),
			"markup" => new CallState(pose.Markup),
			"id" => new CallState(pose.Id),
			"scene" => new CallState(pose.SceneId),
			"author" => new CallState(pose.AuthorDbref ?? string.Empty),
			"authorname" => new CallState(pose.AuthorName),
			"showas" => new CallState(pose.ShowAsName),
			"origin" => new CallState(pose.OriginDbref ?? string.Empty),
			"originname" => new CallState(pose.OriginName),
			"source" => new CallState(pose.Source),
			"tags" => new CallState(string.Join(" ", pose.Tags)),
			"createdat" => new CallState(pose.CreatedAt.ToString()),
			"deleted" => new CallState(pose.IsDeleted ? "1" : "0"),
			"editcount" => new CallState(pose.EditCount.ToString()),
			"lasteditedat" => new CallState(pose.LastEditedAt?.ToString() ?? string.Empty),
			"lasteditor" => new CallState(pose.LastEditorDbref ?? string.Empty),
			"lasteditorname" => new CallState(pose.LastEditorName ?? string.Empty),
			_ => new CallState(pose.Meta.TryGetValue(field, out var metaVal)
				? metaVal
				: "#-1 UNKNOWN POSE FIELD"),
		};
	}

	/// <summary>
	/// sceneedits(&lt;scene&gt;, &lt;poseId&gt;)
	/// Returns a space-separated list of edit-version ids for a pose, oldest first.
	/// </summary>
	[SharpFunction(Name = "sceneedits", MinArgs = 2, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["scene", "poseId"])]
	public static async ValueTask<CallState> SceneEdits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var sceneId = args["0"].Message!.ToPlainText().Trim();
		var poseId = args["1"].Message!.ToPlainText().Trim();

		var service = parser.ServiceProvider.GetRequiredService<ISceneService>();
		var sceneLookup = await service.GetSceneAsync(sceneId);
		if (sceneLookup.IsT1)
		{
			return new CallState(SceneNotFound);
		}

		if (!await SceneVisibleToAsync(service, sceneLookup.AsT0, await ViewerDbrefAsync(parser)))
		{
			return new CallState(ScenePermission);
		}

		var poseLookup = await service.GetPoseAsync(poseId);
		if (poseLookup.IsT1 || poseLookup.AsT0.SceneId != sceneId)
		{
			return new CallState(SceneNotFound);
		}

		var edits = await service.GetPoseEditsAsync(poseId);
		if (edits.IsT1)
		{
			return new CallState(SceneNotFound);
		}

		return new CallState(string.Join(" ", edits.AsT0.Select(e => e.Id)));
	}

	/// <summary>
	/// scenemembers(&lt;scene&gt;[, &lt;role&gt;])
	/// Returns a space-separated list of member dbrefs, optionally filtered to one role.
	/// </summary>
	[SharpFunction(Name = "scenemembers", MinArgs = 1, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["scene", "role"])]
	public static async ValueTask<CallState> SceneMembers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var id = args["0"].Message!.ToPlainText().Trim();
		string? role = null;
		if (args.TryGetValue("1", out var roleArg))
		{
			var roleText = roleArg.Message!.ToPlainText().Trim();
			if (roleText.Length > 0)
			{
				role = roleText;
			}
		}

		var service = parser.ServiceProvider.GetRequiredService<ISceneService>();
		var sceneLookup = await service.GetSceneAsync(id);
		if (sceneLookup.IsT1)
		{
			return new CallState(SceneNotFound);
		}

		if (!await SceneVisibleToAsync(service, sceneLookup.AsT0, await ViewerDbrefAsync(parser)))
		{
			return new CallState(ScenePermission);
		}

		var members = await service.GetMembersAsync(id, role);
		if (members.IsT1)
		{
			return new CallState(SceneNotFound);
		}

		return new CallState(string.Join(" ", members.AsT0.Select(m => m.MemberDbref ?? m.MemberName)));
	}

	/// <summary>
	/// scenemember(&lt;scene&gt;, &lt;player&gt;[, &lt;field&gt;])
	/// Returns a field of a player's membership. Fields: role (default), showas,
	/// current, grantedat, name.
	/// </summary>
	[SharpFunction(Name = "scenemember", MinArgs = 2, MaxArgs = 3,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["scene", "player", "field"])]
	public static async ValueTask<CallState> SceneMemberFn(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var id = args["0"].Message!.ToPlainText().Trim();
		var player = args["1"].Message!.ToPlainText().Trim();
		var field = args.TryGetValue("2", out var fieldArg)
			? fieldArg.Message!.ToPlainText().Trim().ToLowerInvariant()
			: "role";

		var service = parser.ServiceProvider.GetRequiredService<ISceneService>();
		var sceneLookup = await service.GetSceneAsync(id);
		if (sceneLookup.IsT1)
		{
			return new CallState(SceneNotFound);
		}

		if (!await SceneVisibleToAsync(service, sceneLookup.AsT0, await ViewerDbrefAsync(parser)))
		{
			return new CallState(ScenePermission);
		}

		var lookup = await service.GetMemberAsync(id, player);
		if (lookup.IsT1)
		{
			return new CallState(SceneNotFound);
		}

		var member = lookup.AsT0;
		return field switch
		{
			"role" => new CallState(member.Role),
			"showas" => new CallState(member.ShowAs),
			"current" => new CallState(member.IsCurrent ? "1" : "0"),
			"grantedat" => new CallState(member.GrantedAt.ToString()),
			"name" => new CallState(member.MemberName),
			_ => new CallState("#-1 UNKNOWN MEMBER FIELD"),
		};
	}

	/// <summary>
	/// scenefocus(&lt;player&gt;)
	/// Returns the id of the player's currently-focused scene, or #-1 NOT FOUND.
	/// </summary>
	[SharpFunction(Name = "scenefocus", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["player"])]
	public static async ValueTask<CallState> SceneFocus(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var player = parser.CurrentState.Arguments["0"].Message!.ToPlainText().Trim();
		var service = parser.ServiceProvider.GetRequiredService<ISceneService>();
		var lookup = await service.GetCurrentSceneAsync(player);
		if (lookup.IsT1)
		{
			return new CallState(SceneNotFound);
		}

		var scene = lookup.AsT0;
		if (!await SceneVisibleToAsync(service, scene, await ViewerDbrefAsync(parser)))
		{
			return new CallState(ScenePermission);
		}

		return new CallState(scene.Id);
	}

	/// <summary>
	/// scenetags(&lt;scene&gt;)
	/// Returns a space-separated list of the distinct opaque tags used across a scene's poses.
	/// </summary>
	[SharpFunction(Name = "scenetags", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["scene"])]
	public static async ValueTask<CallState> SceneTags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var id = parser.CurrentState.Arguments["0"].Message!.ToPlainText().Trim();
		var service = parser.ServiceProvider.GetRequiredService<ISceneService>();
		var sceneLookup = await service.GetSceneAsync(id);
		if (sceneLookup.IsT1)
		{
			return new CallState(SceneNotFound);
		}

		if (!await SceneVisibleToAsync(service, sceneLookup.AsT0, await ViewerDbrefAsync(parser)))
		{
			return new CallState(ScenePermission);
		}

		var tags = await service.GetTagsAsync(id);
		if (tags.IsT1)
		{
			return new CallState(SceneNotFound);
		}

		return new CallState(string.Join(" ", tags.AsT0));
	}

	/// <summary>
	/// scenecast(&lt;scene&gt;)
	/// Returns a space-separated list of the distinct display personas (ShowAsNames) used in a scene.
	/// </summary>
	[SharpFunction(Name = "scenecast", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["scene"])]
	public static async ValueTask<CallState> SceneCast(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var id = parser.CurrentState.Arguments["0"].Message!.ToPlainText().Trim();
		var service = parser.ServiceProvider.GetRequiredService<ISceneService>();
		var sceneLookup = await service.GetSceneAsync(id);
		if (sceneLookup.IsT1)
		{
			return new CallState(SceneNotFound);
		}

		if (!await SceneVisibleToAsync(service, sceneLookup.AsT0, await ViewerDbrefAsync(parser)))
		{
			return new CallState(ScenePermission);
		}

		var cast = await service.GetCastAsync(id);
		if (cast.IsT1)
		{
			return new CallState(SceneNotFound);
		}

		return new CallState(string.Join(" ", cast.AsT0));
	}

	// ── Writes (WizardOnly | HasSideFX) ───────────────────────────────────────────

	/// <summary>
	/// scenecreate(&lt;room&gt;, &lt;owner&gt;[, &lt;title&gt;])
	/// Creates a scene; returns the new scene id.
	/// </summary>
	[SharpFunction(Name = "scenecreate", MinArgs = 2, MaxArgs = 3,
		Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly | FunctionFlags.HasSideFX,
		ParameterNames = ["room", "owner", "title"])]
	public static async ValueTask<CallState> SceneCreate(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!SideEffectsEnabled(parser))
		{
			return new CallState(ErrorMessages.Returns.NoSideFx);
		}

		var args = parser.CurrentState.Arguments;
		var room = args["0"].Message!.ToPlainText().Trim();
		var owner = args["1"].Message!.ToPlainText().Trim();
		var title = args.TryGetValue("2", out var titleArg)
			? titleArg.Message!.ToPlainText()
			: string.Empty;

		var service = parser.ServiceProvider.GetRequiredService<ISceneService>();
		var scene = await service.CreateSceneAsync(room, owner, title);
		return new CallState(scene.Id);
	}

	/// <summary>
	/// sceneset(&lt;id&gt;, &lt;key&gt;, &lt;value&gt;)
	/// Sets one scene metadata key; returns the scene id on success.
	/// </summary>
	[SharpFunction(Name = "sceneset", MinArgs = 3, MaxArgs = 3,
		Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly | FunctionFlags.HasSideFX,
		ParameterNames = ["id", "key", "value"])]
	public static async ValueTask<CallState> SceneSet(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!SideEffectsEnabled(parser))
		{
			return new CallState(ErrorMessages.Returns.NoSideFx);
		}

		var args = parser.CurrentState.Arguments;
		var id = args["0"].Message!.ToPlainText().Trim();
		var key = args["1"].Message!.ToPlainText().Trim();
		var value = args["2"].Message!.ToPlainText();

		var service = parser.ServiceProvider.GetRequiredService<ISceneService>();
		var result = await service.SetSceneMetaAsync(id, key, value);
		return result.IsT1
			? new CallState(SceneNotFound)
			: new CallState(result.AsT0.Id);
	}

	/// <summary>
	/// sceneaddpose(&lt;id&gt;, &lt;author&gt;, &lt;showas&gt;, &lt;origin&gt;, &lt;source&gt;, &lt;tags&gt;, &lt;content&gt;)
	/// Appends a pose to the scene; returns the new pose id. Tags is a single
	/// space-separated argument; content is the last argument.
	/// </summary>
	[SharpFunction(Name = "sceneaddpose", MinArgs = 7, MaxArgs = 7,
		Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly | FunctionFlags.HasSideFX,
		ParameterNames = ["id", "author", "showas", "origin", "source", "tags", "content"])]
	public static async ValueTask<CallState> SceneAddPose(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!SideEffectsEnabled(parser))
		{
			return new CallState(ErrorMessages.Returns.NoSideFx);
		}

		var args = parser.CurrentState.Arguments;
		var id = args["0"].Message!.ToPlainText().Trim();
		var author = args["1"].Message!.ToPlainText().Trim();
		var showAs = args["2"].Message!.ToPlainText();
		var origin = args["3"].Message!.ToPlainText().Trim();
		var source = args["4"].Message!.ToPlainText();
		var tagsText = args["5"].Message!.ToPlainText().Trim();
		var content = args["6"].Message!.ToPlainText();

		var tags = tagsText.Length == 0
			? Array.Empty<string>()
			: tagsText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		var service = parser.ServiceProvider.GetRequiredService<ISceneService>();
		var result = await service.AddPoseAsync(id, author, showAs, origin, source, tags, content);

		return result.Match(
			pose => new CallState(pose.Id),
			_ => new CallState(SceneNotFound),
			error => new CallState(error.Value));
	}

	/// <summary>
	/// scenesetpose(&lt;poseId&gt;, &lt;key&gt;, &lt;value&gt;)
	/// Sets one pose metadata key; returns the pose id on success.
	/// </summary>
	[SharpFunction(Name = "scenesetpose", MinArgs = 3, MaxArgs = 3,
		Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly | FunctionFlags.HasSideFX,
		ParameterNames = ["poseId", "key", "value"])]
	public static async ValueTask<CallState> SceneSetPose(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!SideEffectsEnabled(parser))
		{
			return new CallState(ErrorMessages.Returns.NoSideFx);
		}

		var args = parser.CurrentState.Arguments;
		var poseId = args["0"].Message!.ToPlainText().Trim();
		var key = args["1"].Message!.ToPlainText().Trim();
		var value = args["2"].Message!.ToPlainText();

		var service = parser.ServiceProvider.GetRequiredService<ISceneService>();
		var result = await service.SetPoseMetaAsync(poseId, key, value);
		return result.IsT1
			? new CallState(SceneNotFound)
			: new CallState(result.AsT0.Id);
	}

	/// <summary>
	/// sceneeditpose(&lt;poseId&gt;, &lt;editor&gt;, &lt;content&gt;)
	/// Edits a pose's content (appends a new version); returns the pose id on success.
	/// </summary>
	[SharpFunction(Name = "sceneeditpose", MinArgs = 3, MaxArgs = 3,
		Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly | FunctionFlags.HasSideFX,
		ParameterNames = ["poseId", "editor", "content"])]
	public static async ValueTask<CallState> SceneEditPose(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!SideEffectsEnabled(parser))
		{
			return new CallState(ErrorMessages.Returns.NoSideFx);
		}

		var args = parser.CurrentState.Arguments;
		var poseId = args["0"].Message!.ToPlainText().Trim();
		var editor = args["1"].Message!.ToPlainText().Trim();
		var content = args["2"].Message!.ToPlainText();

		var service = parser.ServiceProvider.GetRequiredService<ISceneService>();
		var result = await service.EditPoseAsync(poseId, editor, content);
		return result.IsT1
			? new CallState(SceneNotFound)
			: new CallState(result.AsT0.Id);
	}

	/// <summary>
	/// sceneundo(&lt;poseId&gt;)
	/// Moves a pose's current-edit pointer to the previous version; returns the pose id.
	/// </summary>
	[SharpFunction(Name = "sceneundo", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly | FunctionFlags.HasSideFX,
		ParameterNames = ["poseId"])]
	public static async ValueTask<CallState> SceneUndo(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!SideEffectsEnabled(parser))
		{
			return new CallState(ErrorMessages.Returns.NoSideFx);
		}

		var poseId = parser.CurrentState.Arguments["0"].Message!.ToPlainText().Trim();
		var service = parser.ServiceProvider.GetRequiredService<ISceneService>();
		var result = await service.UndoPoseAsync(poseId);

		return result.Match(
			pose => new CallState(pose.Id),
			_ => new CallState(SceneNotFound),
			error => new CallState(error.Value));
	}

	/// <summary>
	/// sceneredo(&lt;poseId&gt;)
	/// Moves a pose's current-edit pointer to the next version; returns the pose id.
	/// </summary>
	[SharpFunction(Name = "sceneredo", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly | FunctionFlags.HasSideFX,
		ParameterNames = ["poseId"])]
	public static async ValueTask<CallState> SceneRedo(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!SideEffectsEnabled(parser))
		{
			return new CallState(ErrorMessages.Returns.NoSideFx);
		}

		var poseId = parser.CurrentState.Arguments["0"].Message!.ToPlainText().Trim();
		var service = parser.ServiceProvider.GetRequiredService<ISceneService>();
		var result = await service.RedoPoseAsync(poseId);

		return result.Match(
			pose => new CallState(pose.Id),
			_ => new CallState(SceneNotFound),
			error => new CallState(error.Value));
	}

	/// <summary>
	/// scenemovepose(&lt;poseId&gt;, &lt;after&gt;)
	/// Re-links a pose to follow &lt;after&gt; in the pose chain (empty after = front);
	/// returns the pose id on success.
	/// </summary>
	[SharpFunction(Name = "scenemovepose", MinArgs = 1, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly | FunctionFlags.HasSideFX,
		ParameterNames = ["poseId", "after"])]
	public static async ValueTask<CallState> SceneMovePose(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!SideEffectsEnabled(parser))
		{
			return new CallState(ErrorMessages.Returns.NoSideFx);
		}

		var args = parser.CurrentState.Arguments;
		var poseId = args["0"].Message!.ToPlainText().Trim();
		var after = args.TryGetValue("1", out var afterArg)
			? afterArg.Message!.ToPlainText().Trim()
			: string.Empty;

		var service = parser.ServiceProvider.GetRequiredService<ISceneService>();
		var result = await service.MovePoseAsync(poseId, after);

		return result.Match(
			pose => new CallState(pose.Id),
			_ => new CallState(SceneNotFound),
			error => new CallState(error.Value));
	}

	/// <summary>
	/// scenedelpose(&lt;poseId&gt;)
	/// Soft-deletes a pose; returns the pose id on success.
	/// </summary>
	[SharpFunction(Name = "scenedelpose", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly | FunctionFlags.HasSideFX,
		ParameterNames = ["poseId"])]
	public static async ValueTask<CallState> SceneDelPose(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!SideEffectsEnabled(parser))
		{
			return new CallState(ErrorMessages.Returns.NoSideFx);
		}

		var poseId = parser.CurrentState.Arguments["0"].Message!.ToPlainText().Trim();
		var service = parser.ServiceProvider.GetRequiredService<ISceneService>();
		var result = await service.DeletePoseAsync(poseId);
		return result.IsT1
			? new CallState(SceneNotFound)
			: new CallState(result.AsT0.Id);
	}

	/// <summary>
	/// sceneaddmember(&lt;id&gt;, &lt;player&gt;, &lt;role&gt;)
	/// Adds or updates a member; returns the player's member dbref on success.
	/// </summary>
	[SharpFunction(Name = "sceneaddmember", MinArgs = 3, MaxArgs = 3,
		Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly | FunctionFlags.HasSideFX,
		ParameterNames = ["id", "player", "role"])]
	public static async ValueTask<CallState> SceneAddMember(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!SideEffectsEnabled(parser))
		{
			return new CallState(ErrorMessages.Returns.NoSideFx);
		}

		var args = parser.CurrentState.Arguments;
		var id = args["0"].Message!.ToPlainText().Trim();
		var player = args["1"].Message!.ToPlainText().Trim();
		var role = args["2"].Message!.ToPlainText().Trim();

		var service = parser.ServiceProvider.GetRequiredService<ISceneService>();
		var result = await service.AddMemberAsync(id, player, role);
		return result.IsT1
			? new CallState(SceneNotFound)
			: new CallState(result.AsT0.MemberDbref ?? result.AsT0.MemberName);
	}

	/// <summary>
	/// sceneunmember(&lt;id&gt;, &lt;player&gt;)
	/// Removes all of a player's membership to a scene; returns the scene id on success.
	/// </summary>
	[SharpFunction(Name = "sceneunmember", MinArgs = 2, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly | FunctionFlags.HasSideFX,
		ParameterNames = ["id", "player"])]
	public static async ValueTask<CallState> SceneUnMember(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!SideEffectsEnabled(parser))
		{
			return new CallState(ErrorMessages.Returns.NoSideFx);
		}

		var args = parser.CurrentState.Arguments;
		var id = args["0"].Message!.ToPlainText().Trim();
		var player = args["1"].Message!.ToPlainText().Trim();

		var service = parser.ServiceProvider.GetRequiredService<ISceneService>();
		var result = await service.RemoveMemberAsync(id, player);
		return result.IsT1
			? new CallState(SceneNotFound)
			: new CallState(id);
	}

	/// <summary>
	/// scenesetfocus(&lt;player&gt;[, &lt;id&gt;])
	/// Sets the player's focused scene (empty id clears focus); returns the id (or
	/// empty when cleared) on success.
	/// </summary>
	[SharpFunction(Name = "scenesetfocus", MinArgs = 1, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly | FunctionFlags.HasSideFX,
		ParameterNames = ["player", "id"])]
	public static async ValueTask<CallState> SceneSetFocus(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!SideEffectsEnabled(parser))
		{
			return new CallState(ErrorMessages.Returns.NoSideFx);
		}

		var args = parser.CurrentState.Arguments;
		var player = args["0"].Message!.ToPlainText().Trim();
		var id = args.TryGetValue("1", out var idArg)
			? idArg.Message!.ToPlainText().Trim()
			: string.Empty;

		var service = parser.ServiceProvider.GetRequiredService<ISceneService>();
		var result = await service.SetFocusAsync(player, id);
		return result.IsT1
			? new CallState(SceneNotFound)
			: new CallState(id);
	}

	/// <summary>
	/// sceneshowas(&lt;id&gt;, &lt;player&gt;, &lt;name&gt;)
	/// Sets the player's per-scene display persona; returns the new persona on success.
	/// </summary>
	[SharpFunction(Name = "sceneshowas", MinArgs = 3, MaxArgs = 3,
		Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly | FunctionFlags.HasSideFX,
		ParameterNames = ["id", "player", "name"])]
	public static async ValueTask<CallState> SceneShowAs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!SideEffectsEnabled(parser))
		{
			return new CallState(ErrorMessages.Returns.NoSideFx);
		}

		var args = parser.CurrentState.Arguments;
		var id = args["0"].Message!.ToPlainText().Trim();
		var player = args["1"].Message!.ToPlainText().Trim();
		var name = args["2"].Message!.ToPlainText();

		var service = parser.ServiceProvider.GetRequiredService<ISceneService>();
		var result = await service.SetShowAsAsync(id, player, name);
		return result.IsT1
			? new CallState(SceneNotFound)
			: new CallState(result.AsT0.ShowAs);
	}

	/// <summary>
	/// sceneplot(&lt;op&gt;, &lt;plot&gt;[, &lt;id&gt;])
	/// Manages plots. op: create/update (plot = "title|description|owner",
	/// id = existing plot id when updating), link/unlink (plot = plot id, id =
	/// scene id). Returns the plot id.
	/// </summary>
	[SharpFunction(Name = "sceneplot", MinArgs = 2, MaxArgs = 3,
		Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly | FunctionFlags.HasSideFX,
		ParameterNames = ["op", "plot", "id"])]
	public static async ValueTask<CallState> ScenePlotFn(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!SideEffectsEnabled(parser))
		{
			return new CallState(ErrorMessages.Returns.NoSideFx);
		}

		var args = parser.CurrentState.Arguments;
		var op = args["0"].Message!.ToPlainText().Trim().ToLowerInvariant();
		var plot = args["1"].Message!.ToPlainText().Trim();
		var id = args.TryGetValue("2", out var idArg)
			? idArg.Message!.ToPlainText().Trim()
			: string.Empty;

		var service = parser.ServiceProvider.GetRequiredService<ISceneService>();

		switch (op)
		{
			case "create":
			case "update":
			{
				var parts = plot.Split('|');
				var title = parts.Length > 0 ? parts[0] : string.Empty;
				var description = parts.Length > 1 ? parts[1] : string.Empty;
				var owner = parts.Length > 2 ? parts[2] : string.Empty;
				var plotId = string.IsNullOrEmpty(id) ? null : id;
				var result = await service.UpsertPlotAsync(plotId, title, description, owner);
				return new CallState(result.Id);
			}
			case "link":
			{
				var result = await service.LinkSceneToPlotAsync(plot, id);
				return result.IsT1
					? new CallState(SceneNotFound)
					: new CallState(plot);
			}
			case "unlink":
			{
				var result = await service.UnlinkSceneFromPlotAsync(plot, id);
				return result.IsT1
					? new CallState(SceneNotFound)
					: new CallState(plot);
			}
			default:
				return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, "SCENEPLOT"));
		}
	}
}
