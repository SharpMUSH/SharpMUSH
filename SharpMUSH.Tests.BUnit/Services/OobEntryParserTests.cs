using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// The typed OOB room payload parser. The shapes are the ones the <c>room-contents</c> package actually
/// sends (<c>docs/softcode/room-contents-handler.md</c>) and the ones in the design handoff
/// (<c>docs/design/d1/README.md</c> §7.1); where the two differ, the softcode is the contract.
/// </summary>
public class OobEntryParserTests
{
	// ── v1 (no "v") ─────────────────────────────────────────────────────────

	[Test]
	public async Task V1_who_rows_keep_dbref_name_and_cmd()
	{
		var json = """{ "who": [ { "dbref": "#76", "name": "Marble Bust", "cmd": "look #76" }, { "dbref": "#1", "name": "God", "cmd": "look #1" } ] }""";

		var rows = OobEntryParser.ParseOccupants(json);

		await Assert.That(rows.Count).IsEqualTo(2);
		await Assert.That(rows[0].Dbref).IsEqualTo("#76");
		await Assert.That(rows[0].Name).IsEqualTo("Marble Bust");
		await Assert.That(rows[0].Cmd).IsEqualTo("look #76");
		await Assert.That(rows[1].Name).IsEqualTo("God");
	}

	[Test]
	public async Task V1_rows_have_null_optional_members()
	{
		var rows = OobEntryParser.ParseOccupants("""{ "who": [ { "dbref": "#76", "name": "Marble Bust", "cmd": "look #76" } ] }""");

		var row = rows[0];
		await Assert.That(row.ObjId).IsNull();
		await Assert.That(row.Type).IsNull();
		await Assert.That(row.Color).IsNull();
		await Assert.That(row.Image).IsNull();
		await Assert.That(row.Status).IsNull();
		await Assert.That(row.Idle).IsNull();
		await Assert.That(row.Profile).IsFalse();
		await Assert.That(row.You).IsFalse();
		await Assert.That(row.Actions).IsEmpty();
	}

	[Test]
	public async Task V1_exit_rows_have_only_name_and_cmd()
	{
		var exits = OobEntryParser.ParseExits("""{ "exits": [ { "name": "east", "cmd": "goto #80" }, { "name": "north", "cmd": "goto #74" } ] }""");

		await Assert.That(exits.Count).IsEqualTo(2);
		await Assert.That(exits[0].Name).IsEqualTo("east");
		await Assert.That(exits[0].Cmd).IsEqualTo("goto #80");
		await Assert.That(exits[0].Dbref).IsNull();
		await Assert.That(exits[0].State).IsNull();
		await Assert.That(exits[0].Aliases).IsEmpty();
		await Assert.That(exits[0].Dest).IsNull();
	}

	[Test]
	public async Task V1_bare_string_entries_become_named_rows()
	{
		var exits = OobEntryParser.ParseExits("""{"exits":["north","south"]}""");

		await Assert.That(exits.Count).IsEqualTo(2);
		await Assert.That(exits[1].Name).IsEqualTo("south");
		await Assert.That(exits[1].Cmd).IsNull();
	}

	/// <summary>
	/// A payload without <c>v</c> is v1, and v1 has only <c>dbref</c>, <c>name</c> and <c>cmd</c>. A
	/// handler that has not declared v2 is not read for v2 keys, so a game's own v1 handler that happens
	/// to send an <c>image</c> of some other shape is not misread as a v2 picture.
	/// </summary>
	[Test]
	public async Task Missing_v_reads_only_the_v1_keys()
	{
		var json = """{ "who": [ { "dbref": "#5", "name": "Bob", "cmd": "look #5", "objid": "#5:1", "type": "player", "image": { "url": "/a.jpg" }, "you": true } ] }""";

		var row = OobEntryParser.ParseOccupants(json)[0];

		await Assert.That(row.Name).IsEqualTo("Bob");
		await Assert.That(row.ObjId).IsNull();
		await Assert.That(row.Type).IsNull();
		await Assert.That(row.Image).IsNull();
		await Assert.That(row.You).IsFalse();
	}

	/// <summary><c>json(number,2.0)</c> keeps the <c>.0</c>; a version is a number, not an integer.</summary>
	[Test]
	[Arguments("2")]
	[Arguments("2.0")]
	[Arguments("3")]
	public async Task A_v_of_2_or_more_reads_as_v2(string v)
	{
		var json = $$"""{ "v": {{v}}, "who": [ { "dbref": "#5", "name": "Bob", "objid": "#5:1" } ] }""";

		var row = OobEntryParser.ParseOccupants(json)[0];

		await Assert.That(row.ObjId).IsEqualTo("#5:1");
	}

	[Test]
	[Arguments("\"2\"")]
	[Arguments("null")]
	[Arguments("1")]
	[Arguments("1.5")]
	public async Task A_v_that_is_not_2_or_more_reads_as_v1(string v)
	{
		var json = $$"""{ "v": {{v}}, "who": [ { "dbref": "#5", "name": "Bob", "objid": "#5:1" } ] }""";

		var row = OobEntryParser.ParseOccupants(json)[0];

		await Assert.That(row.Name).IsEqualTo("Bob");
		await Assert.That(row.ObjId).IsNull();
	}

	[Test]
	[Arguments(null)]
	[Arguments("")]
	[Arguments("not json")]
	[Arguments("[]")]
	[Arguments("{\"who\":\"oops\"}")]
	[Arguments("{\"v\":2}")]
	public async Task Malformed_payloads_give_empty_lists(string? json)
	{
		await Assert.That(OobEntryParser.ParseOccupants(json).Count).IsEqualTo(0);
		await Assert.That(OobEntryParser.ParseExits(json).Count).IsEqualTo(0);
	}

	[Test]
	[Arguments(null)]
	[Arguments("")]
	[Arguments("not json")]
	[Arguments("[]")]
	[Arguments("\"a room\"")]
	public async Task Malformed_room_info_gives_null(string? json)
	{
		await Assert.That(OobEntryParser.ParseRoomInfo(json)).IsNull();
	}

	// ── v2: room.contents ───────────────────────────────────────────────────

	/// <summary>The design handoff's example (README §7.1).</summary>
	[Test]
	public async Task V2_readme_contents_parse_every_field()
	{
		var json = """
			{ "v": 2, "who": [
			  { "dbref": "#312", "objid": "#312:1718000000", "type": "player", "name": "Tomas Reyes", "color": "#ffb454",
			    "image": { "url": "/assets/chars/312.jpg", "alt": "Tomas Reyes" },
			    "status": "active", "idle": 60, "profile": true, "cmd": "look #312",
			    "actions": [ { "label": "Page", "cmd": "page #312=" } ] },
			  { "dbref": "#1142", "type": "thing", "name": "Oilcloth bundle", "image": { "url": "/assets/obj/1142.jpg" }, "cmd": "look #1142" } ] }
			""";

		var rows = OobEntryParser.ParseOccupants(json);

		await Assert.That(rows.Count).IsEqualTo(2);
		var tomas = rows[0];
		await Assert.That(tomas.Dbref).IsEqualTo("#312");
		await Assert.That(tomas.ObjId).IsEqualTo("#312:1718000000");
		await Assert.That(tomas.Type).IsEqualTo("player");
		await Assert.That(tomas.Name).IsEqualTo("Tomas Reyes");
		await Assert.That(tomas.Color).IsEqualTo("#ffb454");
		await Assert.That(tomas.Image).IsEqualTo(new ImageRef("/assets/chars/312.jpg", "Tomas Reyes", null, null, null));
		await Assert.That(tomas.Status).IsEqualTo("active");
		await Assert.That(tomas.Idle).IsEqualTo(60);
		await Assert.That(tomas.Profile).IsTrue();
		await Assert.That(tomas.Cmd).IsEqualTo("look #312");
		await Assert.That(tomas.Actions).IsEquivalentTo([new OccupantAction("Page", "page #312=")]);
		await Assert.That(tomas.You).IsFalse();

		var bundle = rows[1];
		await Assert.That(bundle.Type).IsEqualTo("thing");
		await Assert.That(bundle.Image).IsEqualTo(new ImageRef("/assets/obj/1142.jpg", null, null, null, null));
		await Assert.That(bundle.Status).IsNull();
		await Assert.That(bundle.Idle).IsNull();
		await Assert.That(bundle.Profile).IsFalse();
		await Assert.That(bundle.Actions).IsEmpty();
	}

	/// <summary>What <c>room-contents</c> 2.0 sends a wizard (the handler doc's example).</summary>
	[Test]
	public async Task V2_softcode_contents_parse_every_field()
	{
		var json = """
			{"v":2,"who":[
			  {"dbref":"#13","objid":"#13:1790741467794","type":"player","name":"RcWiz","cmd":"look #13",
			   "color":"#ffb454","status":"active","idle":1,"profile":true,"you":true},
			  {"dbref":"#14","objid":"#14:1790741467825","type":"player","name":"RcMortal","cmd":"look #14",
			   "image":{"url":"/assets/chars/f12d1d09.jpg","alt":"RcMortal"},
			   "status":"active","idle":1,"profile":true,"actions":[{"label":"Page","cmd":"page #14="}]},
			  {"dbref":"#20","objid":"#20:1790741467927","type":"thing","name":"Oilcloth bundle","cmd":"look #20",
			   "image":{"url":"/assets/obj/f12d1d09.jpg","alt":"Oilcloth bundle"}},
			  {"dbref":"#21","objid":"#21:1790741467928","type":"thing","name":"Crate","cmd":"look #21"}]}
			""";

		var rows = OobEntryParser.ParseOccupants(json);

		await Assert.That(rows.Count).IsEqualTo(4);
		await Assert.That(rows[0].You).IsTrue();
		await Assert.That(rows[0].Color).IsEqualTo("#ffb454");
		await Assert.That(rows[0].Image).IsNull();
		await Assert.That(rows[0].Actions).IsEmpty();
		await Assert.That(rows[1].You).IsFalse();
		await Assert.That(rows[1].Image!.Alt).IsEqualTo("RcMortal");
		await Assert.That(rows[1].Actions).IsEquivalentTo([new OccupantAction("Page", "page #14=")]);
		await Assert.That(rows[2].ObjId).IsEqualTo("#20:1790741467927");
		await Assert.That(rows[3].Image).IsNull();
		await Assert.That(rows[3].Cmd).IsEqualTo("look #21");
	}

	// ── v2: room.exits ──────────────────────────────────────────────────────

	[Test]
	public async Task V2_readme_exits_parse_every_field()
	{
		var json = """
			{ "v": 2, "exits": [
			  { "dbref": "#1210", "name": "Harbour Row", "aliases": ["n", "north"], "cmd": "goto #1210", "state": "open",
			    "dest": { "name": "Harbour Row", "area": "Harbour Ward", "image": { "url": "/assets/rooms/1210.jpg" }, "desc": "A lamplit street of…", "here": 2 } },
			  { "dbref": "#1211", "name": "Customs House", "aliases": ["w"], "state": "locked", "hint": "Closed after dusk" },
			  { "dbref": "#1212", "name": "Ferry Steps", "aliases": ["e"], "cmd": "goto #1212", "confirm": "This leaves the scene." } ] }
			""";

		var exits = OobEntryParser.ParseExits(json);

		await Assert.That(exits.Count).IsEqualTo(3);
		var row = exits[0];
		await Assert.That(row.Dbref).IsEqualTo("#1210");
		await Assert.That(row.Aliases).IsEquivalentTo(["n", "north"]);
		await Assert.That(row.State).IsEqualTo(ExitState.Open);
		await Assert.That(row.Dest).IsEqualTo(new ExitDestination("Harbour Row", "Harbour Ward",
			new ImageRef("/assets/rooms/1210.jpg", null, null, null, null), "A lamplit street of…", 2));

		var customs = exits[1];
		await Assert.That(customs.State).IsEqualTo(ExitState.Locked);
		await Assert.That(customs.Hint).IsEqualTo("Closed after dusk");
		await Assert.That(customs.Cmd).IsNull();
		await Assert.That(customs.Dest).IsNull();

		var ferry = exits[2];
		await Assert.That(ferry.Confirm).IsEqualTo("This leaves the scene.");
		await Assert.That(ferry.State).IsNull();
	}

	/// <summary>
	/// What <c>room-contents</c> 2.0 sends a mortal: a locked exit keeps its <c>cmd</c> (trying it is how
	/// a player reads the <c>@fail</c>), an unlinked exit is <c>closed</c>, and an exit with no aliases
	/// sends an empty array.
	/// </summary>
	[Test]
	public async Task V2_softcode_exits_parse_every_state()
	{
		var json = """
			{"v":2,"exits":[
			  {"dbref":"#28","objid":"#28:1790741469223","name":"north","aliases":["n"],"cmd":"goto #28",
			   "confirm":"This leaves the scene.","state":"open","dest":{"name":"RcDest","desc":"A lamplit street.","here":0}},
			  {"dbref":"#29","objid":"#29:1790741469224","name":"east","aliases":["e"],"cmd":"goto #29",
			   "state":"locked","hint":"Closed after dusk."},
			  {"dbref":"#31","objid":"#31:1790741469226","name":"west","aliases":[],"cmd":"goto #31","state":"closed"}]}
			""";

		var exits = OobEntryParser.ParseExits(json);

		await Assert.That(exits.Select(e => e.State)).IsEquivalentTo(new ExitState?[] { ExitState.Open, ExitState.Locked, ExitState.Closed });
		await Assert.That(exits[0].ObjId).IsEqualTo("#28:1790741469223");
		await Assert.That(exits[0].Dest).IsEqualTo(new ExitDestination("RcDest", null, null, "A lamplit street.", 0));
		await Assert.That(exits[1].Cmd).IsEqualTo("goto #29");
		await Assert.That(exits[1].Hint).IsEqualTo("Closed after dusk.");
		await Assert.That(exits[2].Aliases).IsEmpty();
		await Assert.That(exits[2].Dest).IsNull();
	}

	// ── v2: room.info ───────────────────────────────────────────────────────

	[Test]
	public async Task V2_readme_room_info_parses_every_field()
	{
		var json = """
			{ "v": 2, "dbref": "#1201", "objid": "#1201:1719500000", "name": "Lower Docks", "area": "Harbour Ward",
			  "image": { "url": "/assets/rooms/1201.jpg", "alt": "The quay at dusk", "width": 1600, "height": 440, "focal": [0.5, 0.6] },
			  "desc": { "format": "markdown", "text": "Tarred pilings and stacked…" },
			  "scene": { "id": "42", "title": "Salt Market at Dusk", "cast": 5 } }
			""";

		var info = OobEntryParser.ParseRoomInfo(json);

		await Assert.That(info).IsNotNull();
		await Assert.That(info!.Dbref).IsEqualTo("#1201");
		await Assert.That(info.ObjId).IsEqualTo("#1201:1719500000");
		await Assert.That(info.Name).IsEqualTo("Lower Docks");
		await Assert.That(info.Area).IsEqualTo("Harbour Ward");
		await Assert.That(info.Image).IsEqualTo(new ImageRef("/assets/rooms/1201.jpg", "The quay at dusk", (0.5, 0.6), 1600, 440));
		await Assert.That(info.Desc).IsEqualTo(new RoomDescription("markdown", "Tarred pilings and stacked…"));
		await Assert.That(info.Scene).IsEqualTo(new RoomScene("42", "Salt Market at Dusk", 5));
	}

	[Test]
	public async Task V2_softcode_room_info_parses_every_field()
	{
		var json = """
			{"v":2,"dbref":"#35","objid":"#35:1790741470197","name":"RcRoom",
			 "desc":{"format":"text","text":"Tarred pilings.\nStacked crates, God looks on."},
			 "area":"RcDest",
			 "image":{"url":"/assets/rooms/12459103.jpg","alt":"The quay at dusk","focal":[0.5,0.6]}}
			""";

		var info = OobEntryParser.ParseRoomInfo(json)!;

		await Assert.That(info.Desc).IsEqualTo(new RoomDescription("text", "Tarred pilings.\nStacked crates, God looks on."));
		await Assert.That(info.Area).IsEqualTo("RcDest");
		await Assert.That(info.Image).IsEqualTo(new ImageRef("/assets/rooms/12459103.jpg", "The quay at dusk", (0.5, 0.6), null, null));
		await Assert.That(info.Scene).IsNull();
	}

	/// <summary>The viewer's place in the scene (room-contents 2.2): their role, or null, and whether they are focused on it.</summary>
	[Test]
	public async Task V2_room_info_scene_carries_the_viewers_role_and_focus()
	{
		var member = OobEntryParser.ParseRoomInfo(
			"""{"v":2,"name":"R","scene":{"id":"42","title":"T","cast":3,"role":"owner","focus":true}}""")!;
		var watcher = OobEntryParser.ParseRoomInfo(
			"""{"v":2,"name":"R","scene":{"id":"42","title":"T","cast":3,"role":null,"focus":false}}""")!;

		await Assert.That(member.Scene).IsEqualTo(new RoomScene("42", "T", 3, "owner", true));
		await Assert.That(member.Scene!.Outside).IsFalse();
		await Assert.That(watcher.Scene).IsEqualTo(new RoomScene("42", "T", 3, null, false));
		await Assert.That(watcher.Scene!.Outside).IsTrue();
	}

	/// <summary>A viewer focused on another scene (room-contents 2.2.2); a handler that does not say means not.</summary>
	[Test]
	public async Task V2_room_info_scene_carries_whether_the_viewer_is_focused_elsewhere()
	{
		var elsewhere = OobEntryParser.ParseRoomInfo(
			"""{"v":2,"name":"R","scene":{"id":"42","cast":3,"role":"participant","focus":false,"elsewhere":true}}""")!;
		var unsaid = OobEntryParser.ParseRoomInfo(
			"""{"v":2,"name":"R","scene":{"id":"42","cast":3,"role":"participant","focus":false}}""")!;

		await Assert.That(elsewhere.Scene!.Elsewhere).IsTrue();
		await Assert.That(unsaid.Scene!.Elsewhere).IsFalse();
	}

	/// <summary>An older handler says nothing about focus; that is not "outside", so the composer stays.</summary>
	[Test]
	public async Task V2_room_info_scene_without_focus_is_not_outside()
	{
		var info = OobEntryParser.ParseRoomInfo("""{"v":2,"name":"R","scene":{"id":"42","cast":3}}""")!;

		await Assert.That(info.Scene!.Focus).IsNull();
		await Assert.That(info.Scene!.Outside).IsFalse();
	}

	[Test]
	public async Task V2_softcode_room_info_with_a_scene()
	{
		var json = """{"v":2,"dbref":"#47","objid":"#47:1790741471143","name":"RcRoom","scene":{"id":"42","title":"Salt Market at Dusk","cast":3}}""";

		var info = OobEntryParser.ParseRoomInfo(json)!;

		await Assert.That(info.Scene).IsEqualTo(new RoomScene("42", "Salt Market at Dusk", 3));
		await Assert.That(info.Image).IsNull();
		await Assert.That(info.Area).IsNull();
		await Assert.That(info.Desc).IsNull();
	}

	[Test]
	public async Task V1_room_info_keeps_dbref_and_name_only()
	{
		var info = OobEntryParser.ParseRoomInfo("""{"dbref":"#35","name":"Docks","area":"Harbour"}""")!;

		await Assert.That(info.Dbref).IsEqualTo("#35");
		await Assert.That(info.Name).IsEqualTo("Docks");
		await Assert.That(info.Area).IsNull();
	}

	// ── Malformed members are dropped one at a time ─────────────────────────

	/// <summary>One bad row must not lose the list: every non-object, non-string row is skipped alone.</summary>
	[Test]
	public async Task A_bad_row_is_dropped_and_the_rest_kept()
	{
		var json = """{"v":2,"who":[42,null,[1],true,{"dbref":"#5","name":"Bob"},{"dbref":"#6","name":"Ann"}]}""";

		var rows = OobEntryParser.ParseOccupants(json);

		await Assert.That(rows.Select(r => r.Name)).IsEquivalentTo(["Bob", "Ann"]);
	}

	[Test]
	[Arguments("\"/a.jpg\"")]
	[Arguments("{}")]
	[Arguments("{\"url\":5}")]
	[Arguments("{\"url\":\"\"}")]
	[Arguments("[\"/a.jpg\"]")]
	[Arguments("null")]
	public async Task A_bad_image_drops_the_image_and_keeps_the_row(string image)
	{
		var json = $$"""{"v":2,"who":[{"dbref":"#5","name":"Bob","cmd":"look #5","image":{{image}}}]}""";

		var rows = OobEntryParser.ParseOccupants(json);

		await Assert.That(rows.Count).IsEqualTo(1);
		await Assert.That(rows[0].Name).IsEqualTo("Bob");
		await Assert.That(rows[0].Cmd).IsEqualTo("look #5");
		await Assert.That(rows[0].Image).IsNull();
	}

	[Test]
	[Arguments("[0.5]")]
	[Arguments("[0.5,0.6,0.7]")]
	[Arguments("[1.5,0.5]")]
	[Arguments("[0.5,-0.1]")]
	[Arguments("[\"0.5\",\"0.6\"]")]
	[Arguments("\"center\"")]
	[Arguments("{\"x\":0.5,\"y\":0.5}")]
	public async Task A_bad_focal_drops_the_focal_and_keeps_the_image(string focal)
	{
		var json = $$$"""{"v":2,"name":"Docks","image":{"url":"/a.jpg","alt":"Quay","focal":{{{focal}}}}}""";

		var info = OobEntryParser.ParseRoomInfo(json)!;

		await Assert.That(info.Image).IsEqualTo(new ImageRef("/a.jpg", "Quay", null, null, null));
	}

	[Test]
	[Arguments("0")]
	[Arguments("-4")]
	[Arguments("1.5")]
	[Arguments("\"1600\"")]
	public async Task A_bad_size_drops_that_size_only(string width)
	{
		var json = $$$"""{"v":2,"name":"Docks","image":{"url":"/a.jpg","width":{{{width}}},"height":440}}""";

		var info = OobEntryParser.ParseRoomInfo(json)!;

		await Assert.That(info.Image).IsEqualTo(new ImageRef("/a.jpg", null, null, null, 440));
	}

	[Test]
	public async Task A_non_string_alt_is_dropped()
	{
		var info = OobEntryParser.ParseRoomInfo("""{"v":2,"name":"Docks","image":{"url":"/a.jpg","alt":7}}""")!;

		await Assert.That(info.Image).IsEqualTo(new ImageRef("/a.jpg", null, null, null, null));
	}

	/// <summary>
	/// URLs stay exactly as sent. The policy — site-relative or <c>https:</c> only — is applied where a
	/// picture is rendered, by the kit components, so it is written once and not twice.
	/// </summary>
	[Test]
	[Arguments("javascript:alert(1)")]
	[Arguments("http://example.com/a.jpg")]
	[Arguments("data:image/png;base64,AAAA")]
	public async Task Image_urls_are_kept_raw(string url)
	{
		var info = OobEntryParser.ParseRoomInfo($$$"""{"v":2,"name":"Docks","image":{"url":"{{{url}}}"}}""")!;

		await Assert.That(info.Image!.Url).IsEqualTo(url);
	}

	/// <summary>
	/// The colour lands in a CSS custom property, so only <c>#rrggbb</c> is kept — the same rule
	/// <c>FN`COLOR</c> applies in the softcode, repeated here because a game can replace that helper.
	/// </summary>
	[Test]
	[Arguments("red")]
	[Arguments("#fff")]
	[Arguments("#ffb45")]
	[Arguments("#ffb4544")]
	[Arguments("#ffb454; background:url(x)")]
	[Arguments("#gggggg")]
	[Arguments("ffb454")]
	public async Task A_colour_that_is_not_rrggbb_is_dropped(string color)
	{
		var json = $$"""{"v":2,"who":[{"dbref":"#5","name":"Bob","color":"{{color}}"}]}""";

		var row = OobEntryParser.ParseOccupants(json)[0];

		await Assert.That(row.Color).IsNull();
		await Assert.That(row.Name).IsEqualTo("Bob");
	}

	[Test]
	public async Task A_colour_in_either_case_is_kept()
	{
		var row = OobEntryParser.ParseOccupants("""{"v":2,"who":[{"name":"Bob","color":"#FfB454"}]}""")[0];

		await Assert.That(row.Color).IsEqualTo("#FfB454");
	}

	[Test]
	public async Task Wrongly_typed_scalars_are_dropped_one_by_one()
	{
		var json = """
			{"v":2,"who":[{"dbref":5,"objid":["#5:1"],"type":7,"name":"Bob","cmd":{"x":1},
			  "status":1,"idle":"60","profile":"yes","you":"true"}]}
			""";

		var row = OobEntryParser.ParseOccupants(json)[0];

		await Assert.That(row.Name).IsEqualTo("Bob");
		await Assert.That(row.Dbref).IsNull();
		await Assert.That(row.ObjId).IsNull();
		await Assert.That(row.Type).IsNull();
		await Assert.That(row.Cmd).IsNull();
		await Assert.That(row.Status).IsNull();
		await Assert.That(row.Idle).IsNull();
		await Assert.That(row.Profile).IsFalse();
		await Assert.That(row.You).IsFalse();
	}

	[Test]
	public async Task A_negative_idle_is_dropped()
	{
		var row = OobEntryParser.ParseOccupants("""{"v":2,"who":[{"name":"Bob","idle":-1}]}""")[0];

		await Assert.That(row.Idle).IsNull();
	}

	[Test]
	public async Task A_missing_or_non_string_name_reads_as_empty()
	{
		var rows = OobEntryParser.ParseOccupants("""{"v":2,"who":[{"dbref":"#5"},{"dbref":"#6","name":6}]}""");

		await Assert.That(rows.Select(r => r.Name)).IsEquivalentTo(["", ""]);
	}

	[Test]
	public async Task Bad_actions_are_dropped_one_by_one()
	{
		var json = """
			{"v":2,"who":[{"name":"Bob","actions":[
			  {"label":"Page","cmd":"page #5="},
			  {"label":"Mail"},
			  {"cmd":"look #5"},
			  {"label":3,"cmd":"x"},
			  "Wave",
			  {"label":"Wave","cmd":"wave #5"}]}]}
			""";

		var row = OobEntryParser.ParseOccupants(json)[0];

		await Assert.That(row.Actions).IsEquivalentTo([new OccupantAction("Page", "page #5="), new OccupantAction("Wave", "wave #5")]);
	}

	[Test]
	public async Task Actions_that_are_not_an_array_read_as_none()
	{
		var row = OobEntryParser.ParseOccupants("""{"v":2,"who":[{"name":"Bob","actions":{"label":"Page","cmd":"page"}}]}""")[0];

		await Assert.That(row.Actions).IsEmpty();
	}

	[Test]
	public async Task Non_string_aliases_are_dropped_one_by_one()
	{
		var exit = OobEntryParser.ParseExits("""{"v":2,"exits":[{"name":"north","aliases":["n",1,null,"no"]}]}""")[0];

		await Assert.That(exit.Aliases).IsEquivalentTo(["n", "no"]);
	}

	[Test]
	public async Task Aliases_that_are_not_an_array_read_as_none()
	{
		var exit = OobEntryParser.ParseExits("""{"v":2,"exits":[{"name":"north","aliases":"n"}]}""")[0];

		await Assert.That(exit.Aliases).IsEmpty();
	}

	[Test]
	[Arguments("\"ajar\"")]
	[Arguments("1")]
	[Arguments("\"\"")]
	public async Task An_unknown_exit_state_is_dropped(string state)
	{
		var exit = OobEntryParser.ParseExits($$"""{"v":2,"exits":[{"name":"north","cmd":"goto #3","state":{{state}}}]}""")[0];

		await Assert.That(exit.State).IsNull();
		await Assert.That(exit.Cmd).IsEqualTo("goto #3");
	}

	[Test]
	public async Task Exit_state_is_read_case_insensitively()
	{
		var exit = OobEntryParser.ParseExits("""{"v":2,"exits":[{"name":"north","state":"Locked"}]}""")[0];

		await Assert.That(exit.State).IsEqualTo(ExitState.Locked);
	}

	[Test]
	public async Task A_bad_destination_is_dropped_and_the_exit_kept()
	{
		var exit = OobEntryParser.ParseExits("""{"v":2,"exits":[{"name":"north","cmd":"goto #3","dest":"Harbour Row"}]}""")[0];

		await Assert.That(exit.Dest).IsNull();
		await Assert.That(exit.Name).IsEqualTo("north");
	}

	[Test]
	public async Task Bad_destination_members_are_dropped_one_by_one()
	{
		var exit = OobEntryParser.ParseExits("""{"v":2,"exits":[{"name":"north","dest":{"name":1,"area":"Ward","image":"x","desc":{},"here":-2}}]}""")[0];

		await Assert.That(exit.Dest).IsEqualTo(new ExitDestination(null, "Ward", null, null, null));
	}

	[Test]
	[Arguments("{\"title\":\"No id\",\"cast\":2}")]
	[Arguments("{\"id\":\"\"}")]
	[Arguments("{\"id\":true}")]
	[Arguments("\"42\"")]
	public async Task A_scene_without_an_id_is_dropped(string scene)
	{
		var info = OobEntryParser.ParseRoomInfo($$"""{"v":2,"name":"Docks","scene":{{scene}}}""")!;

		await Assert.That(info.Scene).IsNull();
		await Assert.That(info.Name).IsEqualTo("Docks");
	}

	/// <summary>Board <c>12</c> drew the id as a number; the contract says string. Either reads as a string.</summary>
	[Test]
	public async Task A_numeric_scene_id_reads_as_its_string()
	{
		var info = OobEntryParser.ParseRoomInfo("""{"v":2,"name":"Docks","scene":{"id":42}}""")!;

		await Assert.That(info.Scene).IsEqualTo(new RoomScene("42", null, null));
	}

	[Test]
	public async Task Bad_scene_members_are_dropped_one_by_one()
	{
		var info = OobEntryParser.ParseRoomInfo("""{"v":2,"name":"Docks","scene":{"id":"42","title":9,"cast":"five"}}""")!;

		await Assert.That(info.Scene).IsEqualTo(new RoomScene("42", null, null));
	}

	[Test]
	[Arguments("\"Tarred pilings.\"")]
	[Arguments("{\"format\":\"text\"}")]
	[Arguments("{\"format\":\"text\",\"text\":5}")]
	[Arguments("[]")]
	public async Task A_bad_description_is_dropped(string desc)
	{
		var info = OobEntryParser.ParseRoomInfo($$"""{"v":2,"name":"Docks","desc":{{desc}}}""")!;

		await Assert.That(info.Desc).IsNull();
		await Assert.That(info.Name).IsEqualTo("Docks");
	}

	[Test]
	public async Task A_description_without_a_format_is_text()
	{
		var info = OobEntryParser.ParseRoomInfo("""{"v":2,"name":"Docks","desc":{"text":"Pilings."}}""")!;

		await Assert.That(info.Desc).IsEqualTo(new RoomDescription("text", "Pilings."));
	}
}
