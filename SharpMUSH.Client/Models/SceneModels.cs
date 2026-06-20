namespace SharpMUSH.Client.Models;

/// <summary>
/// Client-side projection of <c>SceneController.SceneDto</c>. Timestamps are UTC
/// Unix-millis (<see cref="long"/>) exactly as the server sends them; render with
/// <see cref="System.DateTimeOffset.FromUnixTimeMilliseconds(long)"/>.
/// </summary>
public sealed record SceneSummary(
	string Id,
	string Status,
	bool IsPublic,
	bool IsTempRoom,
	long? ScheduledFor,
	long StartedAt,
	long LastActivityAt,
	int PoseCount,
	string? OwnerDbref,
	string OwnerName,
	string? StarterDbref,
	string StarterName,
	string? RoomDbref,
	string RoomName,
	IReadOnlyDictionary<string, string> Meta)
{
	/// <summary>True when the scene is currently running (open/active).</summary>
	public bool IsLive => string.Equals(Status, "active", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(Status, "open", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(Status, "live", StringComparison.OrdinalIgnoreCase);

	public DateTimeOffset StartedAtUtc => DateTimeOffset.FromUnixTimeMilliseconds(StartedAt);
	public DateTimeOffset LastActivityAtUtc => DateTimeOffset.FromUnixTimeMilliseconds(LastActivityAt);
	public DateTimeOffset? ScheduledForUtc =>
		ScheduledFor is { } v ? DateTimeOffset.FromUnixTimeMilliseconds(v) : null;
}

/// <summary>
/// Client-side projection of <c>SceneController.ScenePoseDto</c>. Carries raw
/// <see cref="Markup"/> (a serialized MString) rendered client-side; never trust
/// server HTML for poses.
/// </summary>
public sealed record ScenePoseView(
	string Id,
	string SceneId,
	string? AuthorDbref,
	string AuthorName,
	string ShowAsName,
	string? OriginDbref,
	string OriginName,
	string Source,
	IReadOnlyList<string> Tags,
	IReadOnlyDictionary<string, string> Meta,
	long CreatedAt,
	bool IsDeleted,
	string Content,
	string Markup,
	int EditCount,
	long? LastEditedAt,
	string? LastEditorDbref,
	string? LastEditorName)
{
	/// <summary>Display persona — <see cref="ShowAsName"/> falling back to <see cref="AuthorName"/>.</summary>
	public string DisplayName =>
		string.IsNullOrWhiteSpace(ShowAsName) ? AuthorName : ShowAsName;

	/// <summary>True when the pose has been edited at least once after its creation.</summary>
	public bool WasEdited => EditCount > 1;

	public DateTimeOffset CreatedAtUtc => DateTimeOffset.FromUnixTimeMilliseconds(CreatedAt);
}

/// <summary>Client-side projection of <c>SceneController.SceneMemberDto</c>.</summary>
public sealed record SceneMemberView(
	string SceneId,
	string? MemberDbref,
	string MemberName,
	string Role,
	string ShowAs,
	bool IsCurrent,
	long GrantedAt)
{
	public DateTimeOffset GrantedAtUtc => DateTimeOffset.FromUnixTimeMilliseconds(GrantedAt);
}
