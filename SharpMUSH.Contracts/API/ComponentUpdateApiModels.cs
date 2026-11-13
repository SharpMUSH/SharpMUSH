namespace SharpMUSH.Library.API;

/// <summary>The parts of a game the update agent can replace, in the order an update replaces them.</summary>
public static class ServerComponents
{
	/// <summary>The rendering worker. Replacing it keeps every connection.</summary>
	public const string Renderer = "renderer";

	/// <summary>The engine, which also serves the portal. Replacing it keeps every connection.</summary>
	public const string Server = "server";

	/// <summary>The socket server. Replacing it closes every telnet and web terminal connection.</summary>
	public const string SocketServer = "socketserver";

	/// <summary>Every component, in update order: the socket server comes last.</summary>
	public static readonly IReadOnlyList<string> InUpdateOrder = [Renderer, Server, SocketServer];
}

/// <summary>One component as the update agent sees it running.</summary>
/// <param name="Component">One of <see cref="ServerComponents"/>.</param>
/// <param name="Service">Its Compose service name.</param>
/// <param name="Image">The image it was started from, such as <c>sharpmush/sharpmush-server</c>.</param>
/// <param name="Tag">The tag that image follows, such as <c>latest</c> or <c>1.0.0</c>.</param>
/// <param name="Version">The image's <c>org.opencontainers.image.version</c> label, or null.</param>
/// <param name="Revision">The image's <c>org.opencontainers.image.revision</c> label (a commit), or null.</param>
/// <param name="RunningDigest">The registry digest of the image the container runs, or null for a local build.</param>
/// <param name="LatestDigest">The registry digest the tag names now, or null when the registry was not read.</param>
/// <param name="State">The container's state, such as <c>running</c>.</param>
/// <param name="StartedAt">When the container last started.</param>
public sealed record UpdateComponentStatus(
	string Component,
	string Service,
	string Image,
	string Tag,
	string? Version,
	string? Revision,
	string? RunningDigest,
	string? LatestDigest,
	string State,
	DateTimeOffset? StartedAt)
{
	/// <summary>Whether the tag it follows names a newer image than the one it runs.</summary>
	public bool UpdateAvailable => LatestDigest is not null && RunningDigest is not null && LatestDigest != RunningDigest;
}

/// <summary>An update asked for in the portal, and how far the agent has got with it.</summary>
/// <param name="Id">The update's id.</param>
/// <param name="Components">What it replaces, in the order it replaces them.</param>
/// <param name="Tag">The tag it moves the game to, or null to fetch a newer image of the tag already followed.</param>
/// <param name="RequestedAt">When it was asked for.</param>
/// <param name="RequestedBy">The account that asked for it.</param>
/// <param name="SocketNotBefore">The earliest the socket server is replaced, after the players' warning; null when it is not replaced.</param>
/// <param name="State"><c>queued</c>, <c>running</c>, <c>succeeded</c> or <c>failed</c>.</param>
/// <param name="Step">The component being replaced now, or null.</param>
/// <param name="Message">What went wrong, or the agent's last word on it.</param>
/// <param name="FinishedAt">When it finished.</param>
public sealed record UpdateJobStatus(
	Guid Id,
	IReadOnlyList<string> Components,
	string? Tag,
	DateTimeOffset RequestedAt,
	string RequestedBy,
	DateTimeOffset? SocketNotBefore,
	string State,
	string? Step,
	string? Message,
	DateTimeOffset? FinishedAt)
{
	/// <summary>Whether the update is still to finish.</summary>
	public bool Active => State is UpdateJobStates.Queued or UpdateJobStates.Running;
}

/// <summary>The states of an <see cref="UpdateJobStatus"/>.</summary>
public static class UpdateJobStates
{
	public const string Queued = "queued";
	public const string Running = "running";
	public const string Succeeded = "succeeded";
	public const string Failed = "failed";
}

/// <summary>The updates panel (<c>GET api/admin/updates</c>).</summary>
/// <param name="AgentConfigured">Whether this server was given an update agent token at all.</param>
/// <param name="AgentConnected">Whether the agent has reported in the last minute.</param>
/// <param name="AgentSeenAt">When it last reported.</param>
/// <param name="Project">The Compose project the agent manages.</param>
/// <param name="Tag">The image tag the game follows (<c>SHARPMUSH_IMAGE_TAG</c>), or null when the Compose file fixes it.</param>
/// <param name="Components">Each component the agent found.</param>
/// <param name="AvailableTags">Release tags on the registry, newest first.</param>
/// <param name="RegistryReadAt">When the agent last read the registry.</param>
/// <param name="AgentError">The agent's last error, or null.</param>
/// <param name="Job">The current or last update, or null.</param>
/// <param name="Connections">Open connections, which a socket server update closes.</param>
/// <param name="Players">Logged-in players among them.</param>
public sealed record AdminUpdatesStatus(
	bool AgentConfigured,
	bool AgentConnected,
	DateTimeOffset? AgentSeenAt,
	string? Project,
	string? Tag,
	IReadOnlyList<UpdateComponentStatus> Components,
	IReadOnlyList<string> AvailableTags,
	DateTimeOffset? RegistryReadAt,
	string? AgentError,
	UpdateJobStatus? Job,
	int Connections,
	int Players);

/// <summary>An update asked for in the portal (<c>POST api/admin/updates</c>).</summary>
/// <param name="Components">What to replace; any of <see cref="ServerComponents"/>.</param>
/// <param name="Tag">A tag to move every component to, or null to fetch a newer image of the tag already followed.</param>
/// <param name="SocketDelaySeconds">How long players are warned before the socket server is replaced (0 to 1800).</param>
public sealed record AdminUpdateRequest(IReadOnlyList<string> Components, string? Tag, int SocketDelaySeconds);
