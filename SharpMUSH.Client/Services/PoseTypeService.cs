using SharpMUSH.Client.Models;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Models.Portal.Setup;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The pose types (<c>GET api/scenes/types</c>), read once per acting character and kept: how each type's poses
/// draw, and which types that character hides while reading a scene and while playing in one, each its own choice.
/// Hiding and showing a type runs <c>+scene/hide &lt;type&gt;=reading|playing</c> / <c>+scene/show</c> as the
/// character, so the portal and telnet's log and recall agree; with nobody acting, the choice is kept here for the page. Staff change the catalogue through <c>+scene/type/add|set|remove</c>.
/// </summary>
/// <remarks>
/// A game without the scene package answers empty lists, and every pose then draws as in character: a key the
/// catalogue does not list is <see cref="PoseTypeInfo.Unlisted"/>.
/// </remarks>
public class PoseTypeService
{
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly GameCommandService _commands;
	private readonly IAccountAuthState _accountAuth;
	private readonly ServerInfoService? _serverInfo;

	public PoseTypeService(IHttpClientFactory httpClientFactory, GameCommandService commands, IAccountAuthState accountAuth,
		ServerInfoService? serverInfo = null)
	{
		_httpClientFactory = httpClientFactory;
		_serverInfo = serverInfo;
		_commands = commands;
		_accountAuth = accountAuth;
		// The hidden set is the character's: another character's is read afresh.
		accountAuth.ActiveCharacterChanged += OnActiveCharacterChanged;
	}

	private HttpClient Client => _httpClientFactory.CreateClient("api");

	/// <summary>Raised when the catalogue or the hidden set changed, so every story draws and folds again.</summary>
	public event Action? Changed;

	private readonly SingleFlight<string, ApiResult<PoseTypeCatalogue>> _flight = new(StringComparer.Ordinal);
	private PoseTypeCatalogue _catalogue = PoseTypeCatalogue.Empty;
	private HashSet<string> _hiddenReading = new(StringComparer.OrdinalIgnoreCase);
	private HashSet<string> _hiddenPlaying = new(StringComparer.OrdinalIgnoreCase);
	private (int, long)? _loadedFor;
	private bool _loaded;

	/// <summary>The acting character the server answers the hidden set for.</summary>
	private (int, long)? Acting => _accountAuth.ActiveCharacter is { } acting ? (acting.DbrefNumber, acting.CreationTime) : null;

	/// <summary>The types in display order; empty until <see cref="LoadAsync"/> has answered.</summary>
	public IReadOnlyList<PoseTypeInfo> Types => _catalogue.Types;

	/// <summary>The <c>TYPE`</c> attributes the plugin could not read as a type, and why.</summary>
	public IReadOnlyList<PoseTypeProblem> Problems => _catalogue.Problems;

	/// <summary>Whether the catalogue has been read for the acting character.</summary>
	public bool IsLoaded => _loaded && _loadedFor == Acting;

	/// <summary>Whether the reader hides poses of type <paramref name="key"/> in <paramref name="context"/>.</summary>
	public bool IsHidden(string key, StoryContext context) => Hidden(context).Contains(Normal(key));

	private HashSet<string> Hidden(StoryContext context) => context == StoryContext.Playing ? _hiddenPlaying : _hiddenReading;

	/// <summary>The word <c>+scene/hide</c> and <c>+scene/show</c> take for <paramref name="context"/>.</summary>
	public static string Word(StoryContext context) => context == StoryContext.Playing ? "playing" : "reading";

	/// <summary>The type <paramref name="key"/> names; one the catalogue does not list draws as in character.</summary>
	public PoseTypeInfo For(string? key)
	{
		var normal = Normal(key);
		return _catalogue.Types.FirstOrDefault(t => string.Equals(t.Key, normal, StringComparison.OrdinalIgnoreCase))
			?? PoseTypeInfo.Unlisted(normal);
	}

	private static string Normal(string? key) =>
		string.IsNullOrWhiteSpace(key) ? PoseTypeInfo.InCharacter : key.Trim().ToLowerInvariant();

	/// <summary>
	/// The catalogue, read from the server the first time for each acting character and kept after;
	/// <paramref name="refresh"/> reads it again. A failed read keeps what was there.
	/// </summary>
	public async Task<ApiResult<PoseTypeCatalogue>> LoadAsync(bool refresh = false)
	{
		var acting = Acting;
		if (!refresh && _loaded && _loadedFor == acting) return _catalogue;

		var result = await _flight.RunAsync(acting is { } who ? $"{who.Item1}:{who.Item2}" : "", FetchAsync);
		if (result is PoseTypeCatalogue catalogue && Acting == acting)
		{
			_catalogue = catalogue;
			_hiddenReading = new HashSet<string>(catalogue.HiddenReading.Select(Normal), StringComparer.OrdinalIgnoreCase);
			_hiddenPlaying = new HashSet<string>(catalogue.HiddenPlaying.Select(Normal), StringComparer.OrdinalIgnoreCase);
			_loadedFor = acting;
			_loaded = true;
			Changed?.Invoke();
		}

		return result;
	}

	private async Task<ApiResult<PoseTypeCatalogue>> FetchAsync()
	{
		var result = await Client.GetFeatureApiAsync<PoseTypeCatalogue>(_serverInfo, GameFeatures.Scenes, "api/scenes/types", "The server returned no pose types.");
		return result switch
		{
			PoseTypeCatalogue catalogue => new PoseTypeCatalogue(catalogue.Types ?? [], catalogue.Problems ?? [], catalogue.HiddenReading ?? [],
				catalogue.HiddenPlaying ?? []),
			ApiFailure failure => failure,
		};
	}

	/// <summary>
	/// Hides poses of type <paramref name="key"/> from the reader in <paramref name="context"/>: the character's choice,
	/// or the page's with nobody acting. The other context keeps its own.
	/// </summary>
	public Task<ApiResult<Success>> HideAsync(string key, StoryContext context) => SetHiddenAsync(key, context, hide: true);

	/// <summary>Shows poses of type <paramref name="key"/> to the reader in <paramref name="context"/> again.</summary>
	public Task<ApiResult<Success>> ShowAsync(string key, StoryContext context) => SetHiddenAsync(key, context, hide: false);

	/// <summary>
	/// Runs <c>+scene/hide</c> or <c>+scene/show</c> for one context as the character and reads the hidden sets back:
	/// the command refuses in its output, so only the set read back says whether it took. Refused, the failure carries
	/// what the command said (empty when it said nothing).
	/// </summary>
	private async Task<ApiResult<Success>> SetHiddenAsync(string key, StoryContext context, bool hide)
	{
		var normal = Normal(key);
		if (_accountAuth.ActiveCharacter is null)
		{
			if (hide) Hidden(context).Add(normal);
			else Hidden(context).Remove(normal);
			Changed?.Invoke();
			return new Success();
		}

		return await _commands.RunAsync($"+scene/{(hide ? "hide" : "show")} {normal}={Word(context)}") switch
		{
			PortalCommandResponse response => await LoadAsync(refresh: true) switch
			{
				PoseTypeCatalogue when IsHidden(normal, context) == hide => new Success(),
				PoseTypeCatalogue => Refused(response),
				ApiFailure failure => failure,
			},
			ApiFailure failure => failure,
		};
	}

	/// <summary>Adds the type <paramref name="key"/>, named <paramref name="label"/> (staff with <c>layout.admin</c>).</summary>
	public Task<ApiResult<Success>> AddAsync(string key, string label) =>
		ChangeAsync($"+scene/type/add {Normal(key)}={MushComposeEncoder.Encode(label.Trim())}",
			$"scenetype({Normal(key)},label)", label.Trim());

	/// <summary>
	/// Sets one field of type <paramref name="key"/>: label, presentation, tone, icon, hidden (yes, no, reading or
	/// playing: where it starts hidden) or order.
	/// </summary>
	public Task<ApiResult<Success>> SetAsync(string key, string field, string value)
	{
		var normal = Normal(key);
		var trimmed = value.Trim();
		var expected = field switch
		{
			"hidden" => trimmed switch
			{
				"yes" => "reading playing",
				"reading" or "playing" => trimmed,
				_ => "",
			},
			"label" => trimmed,
			_ => trimmed.ToLowerInvariant(),
		};
		var sent = field == "label" ? MushComposeEncoder.Encode(trimmed) : trimmed;
		return ChangeAsync($"+scene/type/set {normal}/{field}={sent}", $"scenetype({normal},{field})", expected);
	}

	/// <summary>The answer <c>scenetype()</c> gives for a type that is not there.</summary>
	public const string NoSuchType = "#-1 NO SUCH POSE TYPE";

	/// <summary>Removes the type <paramref name="key"/>; its poses then draw as in character.</summary>
	public Task<ApiResult<Success>> RemoveAsync(string key) =>
		ChangeAsync($"+scene/type/remove {Normal(key)}", $"scenetype({Normal(key)})", NoSuchType);

	/// <summary>
	/// Runs one catalogue command and reads the changed field back in the same queue entry; a value read back that
	/// is not the one sent means the command refused, and the failure carries what it said. The catalogue is read
	/// again either way, so the page shows what is there now.
	/// </summary>
	private async Task<ApiResult<Success>> ChangeAsync(string command, string readBack, string expected)
	{
		ApiResult<Success> outcome = await _commands.RunAsync(command, readBack) switch
		{
			PortalCommandResponse { Result: { } value } response =>
				string.Equals(value.Trim(), expected, StringComparison.Ordinal) ? new Success() : Refused(response),
			PortalCommandResponse response => Refused(response),
			ApiFailure failure => failure,
		};
		await LoadAsync(refresh: true);
		return outcome;
	}

	private static ApiFailure Refused(PortalCommandResponse response) =>
		new(ApiFailureKind.Forbidden, string.Join(" ", response.Output));

	private void OnActiveCharacterChanged()
	{
		_loaded = false;
		_hiddenReading = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		_hiddenPlaying = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		Changed?.Invoke();
		// A story stays mounted across the switch, so the new character's hidden set is read here, not on mount.
		_ = LoadAsync();
	}
}
