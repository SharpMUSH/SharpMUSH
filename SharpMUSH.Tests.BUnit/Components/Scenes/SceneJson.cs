using SharpMUSH.Tests.BUnit.Components.Characters;

namespace SharpMUSH.Tests.BUnit.Components.Scenes;

/// <summary>
/// Scene DTOs as <c>/api/scenes</c> serves them (camelCase, Unix-millis), and the list paths the
/// Scenes section asks for, for <see cref="CharactersApiFake.Extra"/>.
/// </summary>
internal static class SceneJson
{
	public const string Recent = "/api/scenes?filter=recent&count=50";
	public const string Active = "/api/scenes?filter=active&count=50";
	public const string Scheduled = "/api/scenes?filter=scheduled&count=50";
	public const string Finished = "/api/scenes?filter=finished&count=50";

	public static string Participant(int dbref) => $"/api/scenes?participant=%23{dbref}&count=50";

	public static string Scene(string id, string title, string status = "active", string room = "The Salt Market",
		int poses = 3, string? image = null, bool isPublic = true, long? scheduledFor = null, string? summary = null)
	{
		var fields = new Dictionary<string, string> { ["title"] = title };
		if (image is not null) fields["image"] = image;
		if (summary is not null) fields["summary"] = summary;
		var meta = System.Text.Json.JsonSerializer.Serialize(fields);
		var scheduled = scheduledFor is { } at ? at.ToString() : "null";
		return $$"""
			{"id":"{{id}}","status":"{{status}}","isPublic":{{(isPublic ? "true" : "false")}},"isTempRoom":false,"scheduledFor":{{scheduled}},
			 "startedAt":{{CharactersApiFake.Now - 3_600_000}},"lastActivityAt":{{CharactersApiFake.Now - 120_000}},"poseCount":{{poses}},
			 "ownerDbref":"#313","ownerName":"Ilsa Varn","starterDbref":"#313","starterName":"Ilsa Varn",
			 "roomDbref":"#40","roomName":"{{room}}","meta":{{meta}}}
			""";
	}

	public static string List(params string[] scenes) => "[" + string.Join(",", scenes) + "]";
}
