using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Components.Kit;
using SharpMUSH.Client.Components.Layout;
using SharpMUSH.Client.Components.Play;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Models.Widgets;
using SharpMUSH.Client.Services;
using SharpMUSH.Client.Widgets;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Models.Portal.Setup;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Pages;

public partial class Play
{
	private GlobalTerminal? _playTerm;
	private PlayComposer? _composer;
	private RoomState Room = RoomState.Empty;

	[CascadingParameter] private Task<AuthenticationState>? AuthState { get; set; }

	/// <summary>MainLayout's drawer; null outside the shell (tests).</summary>
	[CascadingParameter] private ShellNavigation? Shell { get; set; }

	// Whether the account-vs-roster question has been answered yet, and its answer. All three start
	// "unknown"/false so the terminal is not mounted — and no dead Connect button rendered — until
	// we know which state this visitor is in.
	private bool _rosterResolved;
	private bool _needsCharacter;
	private bool _rosterFailed;

	/// <summary>The gate shown instead of the terminal, or null once the visitor may play.</summary>
	private PlayGateState? Gate =>
		!_rosterResolved ? PlayGateState.Resolving
		: _rosterFailed ? PlayGateState.RosterFailed
		: _needsCharacter ? PlayGateState.NeedsCharacter
		: null;

	/// <summary>The player's choice of view in a scene; null until they choose (the terminal, by default).</summary>
	private PlayView? _chosenView;

	/// <summary>The scene <see cref="_chosenView"/> was chosen in: a scene that starts later opens in the terminal again.</summary>
	private string? _chosenFor;

	private bool _focus;

	/// <summary>A phone, either way up (layout.js compactScreenQuery).</summary>
	private bool _compactScreen;

	/// <summary>
	/// A phone held sideways (layout.js shortScreenQuery): wider than the narrow tier, so the tablet layout,
	/// but short of height for it. Play.razor.css tightens that layout under play--short (#1506).
	/// </summary>
	private bool _shortScreen;

	/// <summary>The shell is in touch chrome (layout.js isTouchChrome): its header is merged into the card header.</summary>
	private bool _touchChrome;

	/// <summary>The player opened the full banner on a larger screen; remembered (play.banner = open).</summary>
	private bool _bannerOpen;

	private DotNetObjectReference<Play>? _self;
	private string? _sheetName;
	private RoomOccupant? _sheetOccupant;
	private PlaySheet? _mobileSheet;

	/// <summary>The play layout's aside, then the scope panels it does not place, as the Room sheet's tabs.</summary>
	private IReadOnlyList<PlayAsideEntry> _asideEntries = [];

	/// <summary>The minimum-role gate for the aside and the Room sheet, the one every zone applies.</summary>
	private ApplicationWidgetGate? _asideGate;

	private ApplicationWidgetGate AsideGate => _asideGate ??= new ApplicationWidgetGate(Widgets, ServiceProvider);

	/// <summary>The Room sheet's current tab: Exits when there is one (board 11), else the first.</summary>
	private string? _sheetEntry;

	/// <summary>The sheet's variant of the page context: sheet mode, and a send that closes the sheet.</summary>
	private PlayPageContext _sheetContext = null!;

	/// <summary>What the play-scope widgets may do on this page: open the sheet, send a command.</summary>
	private PlayPageContext _pageContext = null!;

	/// <summary>Widget apps registered for the Play scope that the play layout does not place itself.</summary>
	private IReadOnlyList<PortalApplication> _scopePanels = [];

	private bool _scenesOn = true;

	/// <summary>The scene whose live subscription the hub refused, as SceneStory reported it.</summary>
	private string? _liveRefusedFor;

	/// <summary>Live poses are not arriving because the scene connection is down; the story retries and catches up.</summary>
	private bool _storyLiveDown;

	/// <summary>The scene whose marker the player hid; a scene that starts later is marked again.</summary>
	private string? _hintHiddenFor;

	/// <summary>The channel or conversation main is showing (board 06), or null for the scene card.</summary>
	private string? _commKey;

	// A join the game refused, kept for the scene it was asked of: the refusal goes to the terminal, which
	// the Story view does not show, so the join bar repeats it.
	private (string SceneId, string Text)? _joinRefusal;
	private bool _joining;

	private PlaySettingsMenu? _settingsOpen;

	// The player's preferred terminal width in columns (min 78). The terminal autoscales its
	// font to fill this many columns; the value is also what the client reports over NAWS.
	// Persisted per-browser in localStorage, with whether the room banner is open (larger screens).
	private const int MinCols = 78;
	private const int MaxCols = 240;
	private const string BannerKey = "play.banner";
	private int _targetCols = MinCols;

	/// <summary>
	/// The room banner is folded into the card header on every screen: one bar over the game. The header carries
	/// the room's picture and name, and its name and sub-line open the full banner, whose minimise folds it again.
	/// On a larger screen it opens above the card and the choice is remembered. On a phone it drops over the page
	/// as a sheet (PlaySheet.Details) and is never pinned open, so the header stays the only bar.
	/// </summary>
	private bool BannerOpen => !_compactScreen && _bannerOpen;

	private bool BannerFolded => !BannerOpen && !_focus;

	private bool BannerShown => BannerOpen && !_focus;

	/// <summary>
	/// The room's scene, while the game has the Scene System: a game that turned it off has no story view and
	/// no In scene row, whatever the room's last push said.
	/// </summary>
	private RoomScene? SceneHere => _scenesOn ? Room.Info?.Scene : null;

	/// <summary>The room as every sidebar shows it: without its scene when the game has no scene system.</summary>
	private RoomInfo? SidebarRoom => Room.Info is { } info ? info with { Scene = SceneHere } : null;

	private bool InScene => SceneHere is not null;

	/// <summary>The composer's draft, kept per character so an alt in another tab keeps its own.</summary>
	private string? DraftKey =>
		(PlayTerminal.ConnectedPlayerName ?? SelfRow?.Name ?? AccountAuth.ActiveCharacter?.Name) is { Length: > 0 } name
			? $"play.draft.{name}"
			: null;

	/// <summary>
	/// This client cannot receive the room scene's poses (a guest, or a character that cannot see it):
	/// the Story says so and its composer does not send, as on the live scene page — a pose sent from
	/// here would be posted and never appear. The terminal view still takes commands.
	/// </summary>
	private bool StoryLiveUnavailable => SceneHere?.Id is { } id && id == _liveRefusedFor;

	/// <summary>
	/// The terminal unless the player opened the room's scene's Story: walking into a room with a scene only
	/// marks it (PlaySceneHint). Outside a scene there is only the terminal.
	/// </summary>
	private PlayView View => SceneHere is { } scene && _chosenFor == scene.Id
		? _chosenView ?? PlayView.Terminal
		: PlayView.Terminal;

	/// <summary>The sidebar's current row: the In scene row only while the Story is shown.</summary>
	private string? SidebarCurrent => _commKey ?? (View == PlayView.Story ? null : string.Empty);

	/// <summary>
	/// The card is headed by who the player is playing, the way the terminal's connection row named them:
	/// the connected character, else the account's active one. The room's name is the banner's job.
	/// </summary>
	private string ViewerName =>
		PlayTerminal.ConnectedPlayerName is { Length: > 0 } connected ? connected
		: SelfRow?.Name is { Length: > 0 } self ? self
		: AccountAuth.ActiveCharacter?.Name is { Length: > 0 } active ? active
		: Loc["TermNotLoggedIn"];

	/// <summary>
	/// The viewer's row in the room's contents while connected. It names a guest, whose character the
	/// server chooses, so nothing on the client knows it otherwise.
	/// </summary>
	private RoomOccupant? SelfRow => PlayTerminal.IsConnected ? Room.Occupants.FirstOrDefault(o => o.You) : null;

	/// <summary>The scene's title in a scene; outside one, the room's name only while no banner shows it.</summary>
	private string? CardSubtitle =>
		SceneHere?.Title is { Length: > 0 } scene ? scene
		: BannerShown ? null
		: Room.Info?.Name;

	/// <summary>The room's picture behind the card header, while no banner shows it.</summary>
	private string? CardImage =>
		!BannerShown && Room.Info?.Image?.Url is { } url && ImageUrlPolicy.IsRenderable(url) ? url : null;

	/// <summary>The viewer's own picture from the room's contents (the row marked as them).</summary>
	private ImageRef? ViewerImage => SelfRow?.Image;

	/// <summary>Who the viewer plays, to find their picture while the room has not listed them yet.</summary>
	private string? ViewerCharacter =>
		SelfRow?.ObjId is { Length: > 0 } objid ? objid
		: PlayTerminal.ConnectedPlayerName is { Length: > 0 } connected ? connected
		: AccountAuth.ActiveCharacter is { } active ? $"#{active.DbrefNumber}:{active.CreationTime}"
		: null;

	/// <summary>Unread messages in joined channels and in conversations: the menu button's dot.</summary>
	private int UnreadComms => Comms.Channels.Where(c => c.Joined).Sum(c => c.Unread) + Comms.Conversations.Sum(c => c.Unread);

	private string SheetLabel(PlaySheet sheet) => sheet switch
	{
		PlaySheet.Room => Loc["NavPlayTabRoom"],
		PlaySheet.Details => Room.Info?.Name ?? Loc["NavPlayTabRoom"],
		_ => Loc["NavPlayTabs"],
	};

	protected override void OnInitialized()
	{
		PlayTerminal.ConnectionStateChanged += OnConnectionChanged;
		PlayTerminal.OobChannels.RoomChanged += OnRoomChanged;
		Comms.Changed += OnCommsChanged;
		Room = PlayTerminal.OobChannels.Room;
		_pageContext = new PlayPageContext(
			occupant => InvokeAsync(() => { OpenSheet(occupant); StateHasChanged(); }),
			SendAsync);
		_sheetContext = new PlayPageContext(
			occupant => InvokeAsync(() => { OpenSheetFromRoom(occupant); StateHasChanged(); }),
			command => InvokeAsync(async () => { await SendFromSheetAsync(command); StateHasChanged(); }),
			InSheet: true);
		Layouts.OnLayoutChanged += OnLayoutChanged;
	}

	protected override async Task OnInitializedAsync()
	{
		await ScreenReader.LoadAsync();
		_scenesOn = await ServerInfo.HasFeatureAsync(GameFeatures.Scenes);
		await ResolveRosterAsync();
		await LoadAsideAsync();
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (!firstRender) return;
		var changed = await LoadStoredChoicesAsync();
		_self = DotNetObjectReference.Create(this);
		changed |= await WatchScreenAsync();
		var touchChrome = await IsTouchChromeAsync();
		changed |= touchChrome != _touchChrome;
		_touchChrome = touchChrome;
		if (changed) StateHasChanged();
	}

	/// <summary>The width and the banner's state as this browser kept them; true when either changed.</summary>
	private async Task<bool> LoadStoredChoicesAsync()
	{
		var changed = false;
		var stored = await JS.GetItemAsync(BrowserStore.Local, "play.cols");
		if (int.TryParse(stored, out var n))
		{
			var clamped = Math.Clamp(n, MinCols, MaxCols);
			changed |= clamped != _targetCols;
			_targetCols = clamped;
		}
		// Folded unless the player opened it; the old "min" (the strip) folds too.
		if (await JS.GetItemAsync(BrowserStore.Local, BannerKey) == "open")
		{
			changed |= !_bannerOpen;
			_bannerOpen = true;
		}
		return changed;
	}

	/// <summary>Starts watching the compact and short screen tiers (layout.js); true when either differs from the default.</summary>
	private async Task<bool> WatchScreenAsync()
	{
		var changed = false;
		try
		{
			var compactScreen = await JS.InvokeAsync<bool>("sharpmushLayout.watchCompactScreen", _self);
			changed |= compactScreen != _compactScreen;
			_compactScreen = compactScreen;
		}
		catch (JSException)
		{
			// No layout script (prerender, tests): the banner follows the stored choice.
		}
		try
		{
			var shortScreen = await JS.InvokeAsync<bool>("sharpmushLayout.watchShortScreen", _self);
			changed |= shortScreen != _shortScreen;
			_shortScreen = shortScreen;
		}
		catch (JSException)
		{
			// No layout script: the tablet layout as it is.
		}
		return changed;
	}

	/// <summary>The screen turned into or out of the short tier (layout.js watchCompactScreen).</summary>
	[JSInvokable]
	public Task OnCompactScreenChanged(bool compactScreen) => InvokeAsync(async () =>
	{
		_compactScreen = compactScreen;
		// The sheets are a phone's: a larger screen has the banner above the card and the aside beside it.
		if (_mobileSheet is not null) await CloseMobileSheet();
		// A window that narrows to a phone's width changes chrome as well.
		_touchChrome = await IsTouchChromeAsync();
		StateHasChanged();
	});

	/// <summary>The screen turned into or out of the short tier (layout.js watchShortScreen).</summary>
	[JSInvokable]
	public Task OnShortScreenChanged(bool shortScreen) => InvokeAsync(() =>
	{
		_shortScreen = shortScreen;
		StateHasChanged();
	});

	private async Task<bool> IsTouchChromeAsync()
	{
		try
		{
			return await JS.InvokeAsync<bool>("sharpmushLayout.isTouchChrome");
		}
		catch (JSException)
		{
			return false;
		}
	}

	private Task OpenFoldedBanner() => _compactScreen ? OpenMobileSheetAsync(PlaySheet.Details) : SetBannerOpenAsync(true);

	private async Task SetBannerOpenAsync(bool open)
	{
		_bannerOpen = open;
		await JS.SetItemAsync(BrowserStore.Local, BannerKey, open ? "open" : "folded");
	}

	/// <summary>The open banner's minimise folds it back into the card header.</summary>
	private Task OnBannerMinimisedAsync(bool minimised) => SetBannerOpenAsync(!minimised);

	/// <summary>Focus from the banner's row: the details sheet it sits in closes first, so focus mode is not under it.</summary>
	private async Task EnterFocusAsync()
	{
		if (_mobileSheet == PlaySheet.Details) await CloseMobileSheet();
		OnFocusChanged(true);
	}

	private void OnFocusChanged(bool focus) => _focus = focus;

	private void OnViewChanged(PlayView view) => ChooseView(view);

	/// <summary>Choosing a view in a scene: the player knows of the scene now, so its marker stays hidden.</summary>
	private void ChooseView(PlayView view)
	{
		_chosenView = view;
		_chosenFor = SceneHere?.Id;
		if (view == PlayView.Terminal && _chosenFor is { } scene) _hintHiddenFor = scene;
	}

	private void OnSettingsOpenChanged(PlaySettingsMenu menu, bool open)
	{
		if (open) _settingsOpen = menu;
		else if (_settingsOpen == menu) _settingsOpen = null;
	}

	private void CloseSettings() => _settingsOpen = null;

	private void ClearTerminal() => _playTerm?.ClearOutput();

	private async Task OnColsChanged(string? value)
	{
		_targetCols = int.TryParse(value, out var n)
			? Math.Clamp(n, MinCols, MaxCols)
			: MinCols;
		await JS.SetItemAsync(BrowserStore.Local, "play.cols", _targetCols.ToString(CultureInfo.InvariantCulture));
	}

	private Task ConnectAsync() => _playTerm?.ConnectAsync() ?? Task.CompletedTask;

	private Task DisconnectAsync() => _playTerm?.DisconnectAsync() ?? Task.CompletedTask;

	private void OnLiveUnavailable(string sceneId, bool unavailable)
	{
		if (unavailable) _liveRefusedFor = sceneId;
		else if (_liveRefusedFor == sceneId) _liveRefusedFor = null;
	}

	/// <summary>An edit to the viewer's own pose: the scene package's whole-text edit. The text is softcode (decompose()'s, as the Edit box seeds it), which the package evaluates once.</summary>
	private Task EditPoseAsync((string PoseId, string Text) edit) =>
		SendAsync($"+scene/rewrite {edit.PoseId}={MushComposeEncoder.Encode(edit.Text)}");

	/// <summary>
	/// Joins the room's scene, then asks the same connection what the character is focused on. The query
	/// runs after the join, so a focus that is not this scene means the join was refused, and what the game
	/// printed in between is why. A join that took needs nothing here: the room.info that follows brings
	/// the composer back.
	/// </summary>
	private async Task JoinSceneAsync(RoomScene scene)
	{
		_joining = true;
		_joinRefusal = null;
		var said = new List<string>();
		void Heard(TerminalLine line)
		{
			if (line.Source == TerminalLineSource.Server && !string.IsNullOrWhiteSpace(line.Text))
				lock (said) said.Add(line.Text.Trim());
		}

		PlayTerminal.LineReceived += Heard;
		try
		{
			await PlayTerminal.SendAsync($"+scene/join {scene.Id}");
			var focus = await PlayTerminal.SendCommandAsync("scenefocus(me)");
			if (focus.FirstOrDefault()?.Trim() != scene.Id)
			{
				string text;
				lock (said) text = said.Count > 0 ? string.Join(" ", said) : Loc["NavPlaySceneJoinFailed"];
				_joinRefusal = (scene.Id, text);
			}
		}
		finally
		{
			PlayTerminal.LineReceived -= Heard;
			_joining = false;
		}
	}

	private Task LeaveSceneAsync() => SendAsync("+scene/leave");

	/// <summary>
	/// The aside's entries from the play layout, then the widget apps registered for the Play scope that the
	/// layout does not place, that allow the right sidebar, and that the viewer's role allows. An application
	/// the layout places keeps its minimum role too: placing it is not granting it. The placed entries go
	/// through the same <see cref="ApplicationWidgetGate"/> as every zone, so one the catalog has not delivered
	/// yet joins only once its application is found and admits this viewer. Re-read when an admin changes the
	/// layout.
	/// </summary>
	private async Task LoadAsideAsync()
	{
		var layout = await Layouts.GetLayoutAsync(LayoutScopes.Play);
		var role = AuthState is null ? PortalRole.Guest : PortalRoleHelper.CurrentRole((await AuthState).User);
		var placements = (layout.Zones.TryGetValue(WidgetZone.RightSidebar, out var list) ? list : [])
			.OrderBy(p => p.Order)
			.ToList();

		BuildAside(placements, role);
		if (await AsideGate.ResolveAsync(placements.Select(p => p.WidgetName)))
		{
			BuildAside(placements, role);
			StateHasChanged();
		}
	}

	private void BuildAside(List<WidgetPlacement> placements, PortalRole role)
	{
		var placed = placements.Select(p => p.WidgetName).ToHashSet(StringComparer.OrdinalIgnoreCase);
		_scopePanels = Catalog.ForScope(LayoutScopes.Play)
			.Where(a => role >= a.MinimumRoleEnum)
			.Where(a => a.KindEnum == ApplicationKind.Widget && !placed.Contains(a.Slug))
			.Where(a => a.ZoneEnums.Contains(WidgetZone.RightSidebar))
			.ToList();

		var entries = new List<PlayAsideEntry>();
		foreach (var placement in placements.Where(p => AsideGate.Admits(p.WidgetName, role)))
		{
			if (Widgets.GetWidget(placement.WidgetName) is { } widget) entries.Add(new PlayAsideEntry(placement.WidgetName, widget, placement.Config, Placed: true));
		}
		foreach (var app in _scopePanels)
		{
			if (Widgets.GetWidget(app.Slug) is { } widget) entries.Add(new PlayAsideEntry(app.Slug, widget, null, Placed: false));
		}
		_asideEntries = entries;
		if (_sheetEntry is null || entries.All(e => e.Name != _sheetEntry))
		{
			_sheetEntry = entries.FirstOrDefault(e => e.Name == "Exits")?.Name ?? entries.FirstOrDefault()?.Name;
		}
	}

	private void OnLayoutChanged(string scope)
	{
		if (!string.Equals(scope, LayoutScopes.Play, StringComparison.OrdinalIgnoreCase)) return;
		_ = InvokeAsync(async () =>
		{
			await LoadAsideAsync();
			StateHasChanged();
		});
	}

	/// <summary>The parameters ZoneRenderer passes a widget, built by the same gate.</summary>
	private Dictionary<string, object> EntryParameters(PlayAsideEntry entry) =>
		AsideGate.Parameters(entry.Name, entry.Widget, entry.Config, nameof(WidgetZone.RightSidebar));

	private async Task ResolveRosterAsync()
	{
		_needsCharacter = false;
		_rosterFailed = false;

		// InitAsync is cached per tab, so this costs a task await once the session has hydrated.
		// It restores the session token but NOT the roster (see AccountAuthService.InitCoreAsync),
		// so a reloaded tab always has to ask the server who this account owns — exactly what
		// GlobalTerminal does before deciding whether it can connect.
		await AccountAuth.InitAsync();

		if (AccountAuth.IsLoggedIn && AccountAuth.Characters.Count == 0)
		{
			if (await AccountAuth.GetCharactersAsync() is IReadOnlyList<AccountAuthService.CharacterSummary> characters)
				_needsCharacter = characters.Count == 0;
			else
				_rosterFailed = true;
		}

		_rosterResolved = true;
	}

	/// <summary>Re-asks the server for the roster, back through the loading state so the failed
	/// card cannot sit there looking answered while the retry is in flight.</summary>
	private async Task RetryRosterAsync()
	{
		_rosterResolved = false;
		await ResolveRosterAsync();
	}

	private void OnConnectionChanged(bool _) => InvokeAsync(StateHasChanged);

	private void OnCommsChanged() => InvokeAsync(StateHasChanged);

	// The store keeps the room typed and replaces each list whole on every push, so this reads one
	// snapshot rather than parsing the payloads again.
	private void OnRoomChanged()
	{
		Room = PlayTerminal.OobChannels.Room;
		InvokeAsync(StateHasChanged);
	}

	private async Task SendAsync(string command)
	{
		if (!string.IsNullOrWhiteSpace(command))
			await PlayTerminal.SendAsync(command);
	}

	private async Task SendFromSheetAsync(string command)
	{
		_mobileSheet = null;
		await SendAsync(command);
	}

	private void OpenSheet(RoomOccupant occupant)
	{
		_sheetOccupant = occupant;
		_sheetName = occupant.Name;
	}

	private void OpenSheetFromRoom(RoomOccupant occupant)
	{
		_mobileSheet = null;
		OpenSheet(occupant);
	}

	/// <summary>A name from the story: their room row when they are here, else just the name.</summary>
	private void OpenSheetByName(string name)
	{
		_sheetOccupant = Room.Occupants.FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));
		_sheetName = name;
	}

	private void CloseSheet()
	{
		_sheetName = null;
		_sheetOccupant = null;
	}

	/// <summary>
	/// The sheet's Page: the composer in Story, the terminal's own line otherwise. The room row's Page action
	/// (<c>page #312=</c>) is used when it has one: a dbref survives a rename and a name with spaces.
	/// </summary>
	private async Task StartPageAsync(string name)
	{
		var action = _sheetOccupant?.Actions.FirstOrDefault(a => string.Equals(a.Label, "Page", StringComparison.OrdinalIgnoreCase))?.Cmd;
		CloseSheet();
		if (View == PlayView.Story && _composer is not null)
		{
			await _composer.StartPageAsync(name, action);
		}
		else if (_playTerm is not null)
		{
			await _playTerm.SetInputAsync(action ?? $"page {name}=");
		}
	}

	private void OpenScene()
	{
		_commKey = null;
		ChooseView(PlayView.Story);
	}

	/// <summary>A channel or conversation opens the channel view in main (README §5.1, board 06).</summary>
	private void OpenComm(string key) => _commKey = key;

	private void CloseComm() => _commKey = null;

	private void OpenSceneFromSheet()
	{
		_mobileSheet = null;
		OpenScene();
	}

	private void OpenCommFromSheet(string key)
	{
		_mobileSheet = null;
		OpenComm(key);
	}

	private Task OpenSideSheetAsync() => OpenMobileSheetAsync(PlaySheet.Side);

	/// <summary>Opens a phone sheet, remembering what opened it so closing can hand focus back.</summary>
	private async Task OpenMobileSheetAsync(PlaySheet sheet)
	{
		await RunFocusAsync("sharpmushLayout.rememberFocus");
		_mobileSheet = sheet;
	}

	private async Task CloseMobileSheet()
	{
		_mobileSheet = null;
		await RunFocusAsync("sharpmushLayout.restoreFocus");
	}

	private async Task OpenSiteMenu()
	{
		await CloseMobileSheet();
		Shell?.Open();
	}

	private async Task RunFocusAsync(string identifier)
	{
		try { await JS.InvokeVoidAsync(identifier); }
		catch (JSException) { }
		catch (InvalidOperationException) { }
	}

	public async ValueTask DisposeAsync()
	{
		Layouts.OnLayoutChanged -= OnLayoutChanged;
		PlayTerminal.ConnectionStateChanged -= OnConnectionChanged;
		PlayTerminal.OobChannels.RoomChanged -= OnRoomChanged;
		Comms.Changed -= OnCommsChanged;
		if (_self is null) return;
		try
		{
			await JS.InvokeVoidAsync("sharpmushLayout.unwatchCompactScreen");
			await JS.InvokeVoidAsync("sharpmushLayout.unwatchShortScreen");
		}
		catch (Exception e) when (e is JSException or JSDisconnectedException)
		{
			// The page is going away; so is the listener's reason to call it.
		}
		_self.Dispose();
	}
}
