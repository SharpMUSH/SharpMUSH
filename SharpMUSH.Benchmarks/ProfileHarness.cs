using Microsoft.Extensions.DependencyInjection;
using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library;
using SharpMUSH.Library.Services.Interfaces;
using System.Diagnostics;
using System.Text;

namespace SharpMUSH.Benchmarks;

/// <summary>
/// Profiling harness: <c>dotnet run -c Release -- profile [scenario,...] [--seconds N] [--wait N]
/// [--paren-groups on|off]</c>. Boots the Lightning-backed benchmark host and reports operations per
/// second, managed bytes allocated per operation, and generation-zero collections per thousand
/// operations. <c>--paren-groups</c> sets that compatibility option for the run; the <c>paren-*</c>
/// scenarios are <see cref="ParenGroupBenchmarks"/>' workloads.
/// </summary>
public sealed class ProfileHarness : LightningBaseBenchmark
{
	private sealed record Scenario(string Name, string Input, bool IsCommand, bool IsCommandList = false, bool AsMortal = false);

	private static readonly string Shuffled100 = string.Join(' ', Enumerable.Range(0, 100).Select(i => i * 37 % 100));

	private static readonly Scenario[] Scenarios =
	[
		new("think", "think Hello World", true),
		new("b-fn", "add(1,2)", false),
		new("b-fn-br", "[add(1,2)]", false),
		new("b-nest", "add(mul(2,3),sub(5,1))", false),
		new("b-nest-br", "[add([mul(2,3)],[sub(5,1)])]", false),
		new("b-iter", "iter(lnum(20),add(%i0,1))", false),
		new("b-iter-br", "[iter(lnum(20),[add(%i0,1)])]", false),
		new("b-sub", "%#", false),
		new("b-sub-br", "[%#]", false),
		new("b-think", "think add(1,2)", true),
		new("b-think-br", "think [add(1,2)]", true),
		new("think-subst", "think %#", true),
		new("pemit-fn", "@pemit me=[add(1,2)]", true),
		new("fn1", "[add(1,2)]", false),
		new("fn10", BuildNested(10), false),
		new("cat3", "[cat(%#,%#,%#)]", false),
		new("iter50", "iter(lnum(50),%i0)", false),
		new("mixed", "[switch(%#,#1,[iter(lnum(1,10),[add(##,1)])],other)]", false),
		new("ansi", "[ansi(hr,Hello World)]", false),
		new("ansi-nest", "[ansi(h,[ansi(r,Hello)] [ansi(g,World)])]", false),
		new("ansi-hex", "[ansi(#ff8800/<0 0 64>,Hello World)]", false),
		new("ansi-name", "[ansi(+orange,Hello World)]", false),
		new("ansi-iter", "[iter(lnum(20),ansi(hr,##))]", false),
		new("strlen", "[strlen(Hello World)]", false),
		new("strcat", "[strcat(hr,Hello World)]", false),
		new("strcat-iter", "[iter(lnum(20),strcat(hr,##))]", false),
		new("strlen-iter", "[iter(lnum(20),strlen(##))]", false),
		new("text", "[cat(Hello there, this is a fairly ordinary line of prose that a player might see, with %# in it)]", false),
		new("ufun", "[u(me/PROFILE_FN,5)]", false),
		new("get", "[get(me/PROFILE_FN)]", false),
		new("haspower", "[haspower(me,see_all)]", false),
		new("loc", "[loc(me)]", false),
		new("lcon", "[lcon(me)]", false),
		new("lcon-names", "[iter(lcon(me),name(##))]", false),
		new("hastype", "[hastype(me,player)]", false),
		new("hasflag", "[hasflag(me,CONNECTED)]", false),
		new("orflags", "[orflags(me,Wr)]", false),
		new("num", "[num(me)]", false),
		new("objid", "[objid(me)]", false),
		new("name", "[name(me)]", false),
		new("type", "[type(me)]", false),
		new("idle", "[idle(me)]", false),
		new("not", "[not(0)]", false),
		new("strmatch", "[strmatch(#1,#1)]", false),
		new("json", "[json(string,Hello)]", false),
		new("json-doc", "[json(object,kind,json(string,form),schema_version,json(number,1),title,json(string,New request),pages,json(array,json(object,key,json(string,main),title,json(string,New request),order,json(number,1),sections,json_array(json(object,name,json(string,S),order,json(number,1))))),actions,[chr(123)][chr(125)])]", false),
		new("json-fill", "[json_fill(v(PROFILE_DOC),/kind,form,/title,New request,/pages/0/title,New request,/pages/0/sections,json_array(json(object,name,json(string,S),order,json(number,1))),/actions,[chr(123)][chr(125)])]", false),
		new("json-obj", "[json(object,a,json(string,x),b,json(number,1))]", false),
		new("json-mod", "[json_mod(json(object,a,json(number,1)),patch,json(object,b,json(number,2)))]", false),
		new("setq", "[setq(w1,x)]", false),
		new("rc-whovis", "[filter(me/PROFILE_WHOVIS,lcon(me),,,me)]", false),
		new("rc-whobase", "[iter(lcon(me),u(me/PROFILE_WHOBASE,##))]", false),
		// 0-99 in a fixed shuffled order: a sorted list is a merge sort's best case.
		new("sort", $"[sort({Shuffled100})]", false),
		new("sortby", $"[sortby(me/PROFILE_CMP,{Shuffled100})]", false),
		new("set", "&PROFILE_X me=x", true),
		new("set-cmd", "@set me=PROFILE_X:x", true),
		new("set-fn", "[attrib_set(me/PROFILE_X,x)]", false),
		new("set+lattr", "&PROFILE_X me=x;think [lattr(me)]", true, IsCommandList: true),
		new("set+get", "&PROFILE_X me=x;think [get(me/PROFILE_FN)]", true, IsCommandList: true),
		new("actions", "@pemit me=Hi [add(1,2)];@switch 1=1,think yes;@trigger me/PROFILE_NOOP=a,b", true, IsCommandList: true),
		new("include", "@include me/PROFILE_ACTIONS=5", true, IsCommandList: true),
		// {BIG}: a mortal's object holding BigFlat flat attributes and BigBranches trees of BigLeaves leaves.
		// {KID}: a child of {BIG} with KidAttributes of its own. "-m" runs as that mortal, not as God.
		new("lattr-big", "[lattr({BIG})]", false),
		new("lattr-big-m", "[lattr({BIG})]", false, AsMortal: true),
		new("lattr-pat-m", "[lattr({BIG}/FLAT1*)]", false, AsMortal: true),
		new("lattr-tree-m", "[lattr({BIG}/BR05`*)]", false, AsMortal: true),
		new("nattr-big-m", "[nattr({BIG})]", false, AsMortal: true),
		new("xattr-big-m", "[xattr({BIG},1,10)]", false, AsMortal: true),
		new("lattrp-kid-m", "[lattrp({KID})]", false, AsMortal: true),
		new("reglattr-m", "[reglattr({BIG}/^FLAT00)]", false, AsMortal: true),
		new("hasattr-m", "[hasattr({BIG},FLAT3999)]", false, AsMortal: true),
		new("grep-m", "[grep({BIG},FLAT1*,value)]", false, AsMortal: true),
		new("examine-m", "examine {BIG}", true, AsMortal: true),
		// With --connections: a player's own theme, and the player ancestor's, which every player inherits from.
		new("theme-me", "@theme me=fantasy", true),
		new("theme-anc", "@theme #4=fantasy", true),
		// Feeds (--feed-members N, --feed-lines N): bench/crowd has N members and the default 500-line limit,
		// full; bench/long keeps every line and holds --feed-lines of them. {MEMBER} is crowd's last member.
		new("feed-send", "@feed/send bench/crowd=Coming in.", true),
		new("feed-long", "@feed/send bench/long=Coming in.", true),
		new("feed-quiet", "@feed/send bench/quiet=Coming in.", true),
		// radio/1: bench/quiet's members, each line drawn by the radio package's FORMAT.
		new("feed-format", "@feed/send radio/1=Coming in.", true),
		new("radio-line0", "[uargs(me/FUN`LINE,frequency,1,speaker,%#,name,God,style,pose,text,waves.,reader,%#)]", false),
		new("radio-line", "[uargs(me/FUN`LINE,id,1,frequency,1,speaker,%#,name,God,style,say,text,Coming in.,reader,%#)]", false),
		new("feed-member", "[feedmember(bench/crowd,{MEMBER})]", false),
		new("feed-gag", "[feedmember(bench/crowd,{MEMBER},gag)]", false),
		new("feed-unread", "[feedunread(bench/long,{MEMBER})]", false),
		new("feed-recall", "[iter(feedrecall(bench/long,20),feedmsg(##,text))]", false),
		new("feed-info", "[feedinfo(bench/crowd,members)]", false),
		new("feed-who", "[words(feedwho(bench/crowd,active))]", false),
		new("feedsof", "[feedsof({MEMBER},bench)]", false),
	];

	private const int BigFlat = 4000;
	private const int BigBranches = 40;
	private const int BigLeaves = 50;
	private const int KidAttributes = 100;
	private const string FeedKind = "bench";

	/// <summary>
	/// The bundled radio package's line (examples/packages/radio, <c>{{radio}}</c> as <c>me</c>), on God, for
	/// <c>feed-format</c> and <c>radio-line</c>. Copy it again when the package changes.
	/// </summary>
	private static readonly Dictionary<string, string> RadioLine = new()
	{
		["FUN`KEY"] = "[edit(objid(%0),#,,:,_)]",
		["FUN`NAME"] = "[get(me/F`%0`NAME)]",
		["FUN`LINE"] = "if(cor(not(strlen(%<id>)),comp(%q<line_id>,%<frequency>/%<id>)),setqm(line_id,%<frequency>/%<id>,line_title,get(me/P`[u(me/FUN`KEY,%<speaker>)]`TITLE`%<frequency>),line_speaker,if(strlen(%q<line_title>),%q<line_title>%b)%<name>,line_frequency,get(me/F`%<frequency>`NAME),line_painted_colour,#-1))[setq(line_colour,firstof(get(me/P`[edit(objid(%<reader>),#,,:,_)]`COLOR`%<frequency>),get(me/DATA`COLOR)))][if(comp(%q<line_painted_colour>,%q<line_colour>),setqm(line_painted_colour,%q<line_colour>,line_painted,u(me/FUN`PAINT,%q<line_colour>,%q<line_frequency>)))]<%q<line_painted>> [switch(%<style>,pose,%q<line_speaker> %<text>,semipose,%q<line_speaker>%<text>,emit,%<text>,announce,%<text>,%q<line_speaker> says\\, \"%<text>\")]",
		["FUN`PAINT"] = "[if(strmatch(setr(_p,tone(%0,%1)),#-*),ansi(%0,%1),%q<_p>)]",
		["FEED`RADIO`FORMAT"] = "uargs(me/FUN`LINE,id,%<id>,frequency,%<key>,speaker,%<speaker>,name,firstof(%<name>,name(%<speaker>)),style,%<style>,text,%<text>,reader,%<recipient>)",
		["DATA`COLOR"] = "info",
		["F`1`NAME"] = "Ops",
	};

	/// <summary>
	/// <see cref="ParenGroupBenchmarks"/>' workloads, each in the form its setting reads: unescaped with
	/// <c>paren_groups</c> on, escaped with it off (the default).
	/// </summary>
	private static IEnumerable<Scenario> ParenScenarios(bool parenGroups)
		=> ParenGroupBenchmarks.Workloads.Select(workload => new Scenario(workload.Name, workload.For(parenGroups), false));

	private static string BuildNested(int depth)
	{
		var sb = new StringBuilder();
		for (var i = 0; i < depth; i++) sb.Append("[add(1,");
		sb.Append('1');
		for (var i = 0; i < depth; i++) sb.Append(")]");
		return sb.ToString();
	}

	public static async Task RunAsync(string[] args)
	{
		var seconds = 10;
		var wait = 0;
		var connections = 0;
		var feedMembers = 200;
		var feedLines = 5000;
		bool? parenGroups = null;
		var selected = new List<string>();
		for (var i = 0; i < args.Length; i++)
		{
			switch (args[i])
			{
				case "--seconds":
					if (++i >= args.Length || !int.TryParse(args[i], out seconds) || seconds <= 0)
						throw new ArgumentException("--seconds requires a positive integer.");
					break;
				case "--wait":
					if (++i >= args.Length || !int.TryParse(args[i], out wait) || wait < 0)
						throw new ArgumentException("--wait requires a non-negative integer.");
					break;
				case "--paren-groups":
					parenGroups = ++i < args.Length ? args[i] switch
					{
						"on" => true,
						"off" => false,
						_ => throw new ArgumentException("--paren-groups takes on or off.")
					} : throw new ArgumentException("--paren-groups takes on or off.");
					break;
				case "--connections":
					if (++i >= args.Length || !int.TryParse(args[i], out connections) || connections < 0)
						throw new ArgumentException("--connections requires a non-negative integer.");
					break;
				case "--feed-members":
					if (++i >= args.Length || !int.TryParse(args[i], out feedMembers) || feedMembers <= 0)
						throw new ArgumentException("--feed-members requires a positive integer.");
					break;
				case "--feed-lines":
					if (++i >= args.Length || !int.TryParse(args[i], out feedLines) || feedLines < 0)
						throw new ArgumentException("--feed-lines requires a non-negative integer.");
					break;
				default: selected.AddRange(args[i].Split(',', StringSplitOptions.RemoveEmptyEntries)); break;
			}
		}

		var all = Scenarios.Concat(ParenScenarios(parenGroups ?? false)).ToArray();
		var scenarios = selected.Count == 0
			? all
			: all.Where(s => selected.Contains(s.Name, StringComparer.OrdinalIgnoreCase)).ToArray();

		var harness = new ProfileHarness();
		await harness.Setup();
		try
		{
			await harness.RunScenariosAsync(scenarios, seconds, wait, parenGroups, connections, feedMembers, feedLines);
		}
		finally
		{
			await harness.Cleanup();
		}
	}

	private async Task RunScenariosAsync(Scenario[] scenarios, int seconds, int wait, bool? parenGroups, int connections,
		int feedMembers, int feedLines)
	{
		if (await _database!.GetObjectNodeAsync(new DBRef(1)) is not (AnySharpObject and SharpPlayer god))
			throw new InvalidOperationException("God (#1) is not seeded as a player.");
		var one = god.Object.DBRef;
		var baseParser = _server!.Services.GetRequiredService<IMUSHCodeParser>();
		if (scenarios.Any(scenario => scenario.Name.StartsWith("paren-", StringComparison.Ordinal)))
			await ParenGroupBenchmarks.VerifyWorkloadsAsync(baseParser, one);
		if (parenGroups is { } on) baseParser = ParenGroupBenchmarks.WithParenGroups(baseParser, on);
		await _database.SetAttributeAsync(new DBRef(1), ["PROFILE_FN"], MarkupText.Plain("[mul(%0,2)]"), god);
		await _database.SetAttributeAsync(new DBRef(1), ["PROFILE_WHOVIS"], MarkupText.Plain("cand(not(hastype(%0,exit)),cor(not(hastype(%0,player)),hasflag(%0,CONNECTED)),cor(not(hasflag(%0,DARK)),strmatch(num(%0),num(%1)),orflags(%1,Wr),haspower(%1,See_All)))"), god);
		await _database.SetAttributeAsync(new DBRef(1), ["PROFILE_WHOBASE"], MarkupText.Plain("json_mod(json(object,dbref,json(string,num(%0)),objid,json(string,objid(%0)),type,json(string,lcstr(type(%0))),name,json(string,name(%0)),cmd,json(string,look [num(%0)])),patch,json(object,status,if(hastype(%0,player),json(string,idle(%0)),null),profile,if(hastype(%0,player),true,null)))"), god);
		await _database.SetAttributeAsync(new DBRef(1), ["PROFILE_DOC"], MarkupText.Plain("{\"kind\":\"\",\"schema_version\":1,\"title\":\"\",\"pages\":[{\"key\":\"main\",\"title\":\"\",\"order\":1,\"sections\":[]}],\"actions\":{}}"), god);
		await _database.SetAttributeAsync(new DBRef(1), ["PROFILE_NOOP"], MarkupText.Plain("think"), god);
		await _database.SetAttributeAsync(new DBRef(1), ["PROFILE_CMP"], MarkupText.Plain("sub(%0,%1)"), god);
		await _database.SetAttributeAsync(new DBRef(1), ["PROFILE_ACTIONS"], MarkupText.Plain("@pemit me=Got %0 [add(%0,1)];think [mul(%0,2)];@switch %0=5,{@pemit me=five},{@pemit me=other}"), god);
		for (var i = 0; i < 50; i++)
			await baseParser.FromState(BenchmarkHelpers.FreshState(one)).CommandParse(MarkupText.Plain($"@create Profile Thing {i}"));

		await ConnectPlayersAsync(connections);

		var (mortal, big, kid) = scenarios.Any(scenario => scenario.Input.Contains("{BIG}") || scenario.Input.Contains("{KID}"))
			? await SeedBigObjectsAsync()
			: (one, one, one);
		var member = scenarios.Any(scenario => scenario.Name.StartsWith("feed", StringComparison.Ordinal)
				|| scenario.Name.StartsWith("radio", StringComparison.Ordinal))
			? await SeedFeedsAsync(baseParser, one, feedMembers, feedLines)
			: one;
		var inputs = scenarios.ToDictionary(scenario => scenario, scenario => MarkupText.Plain(scenario.Input
			.Replace("{BIG}", $"#{big.Number}").Replace("{KID}", $"#{kid.Number}").Replace("{MEMBER}", member.ToString())));
		DBRef Executor(Scenario scenario) => scenario.AsMortal ? mortal : one;

		foreach (var scenario in scenarios)
		{
			for (var i = 0; i < 200; i++) await RunOnce(baseParser, Executor(scenario), scenario, inputs[scenario]);
		}

		if (wait > 0)
		{
			Console.WriteLine($"READY pid={Environment.ProcessId} - attach now; measured loop starts in {wait}s");
			await Task.Delay(TimeSpan.FromSeconds(wait));
		}

		Console.WriteLine();
		Console.WriteLine($"{"scenario",-12} {"ops/s",10} {"us/op",10} {"KB/op",9} {"gen0/kop",9}");
		foreach (var scenario in scenarios)
		{
			var input = inputs[scenario];
			var executor = Executor(scenario);
			var allocBefore = GC.GetTotalAllocatedBytes(precise: true);
			var gen0Before = GC.CollectionCount(0);
			var sw = Stopwatch.StartNew();
			long ops = 0;
			while (sw.Elapsed.TotalSeconds < seconds)
			{
				await RunOnce(baseParser, executor, scenario, input);
				ops++;
			}

			sw.Stop();
			var alloc = GC.GetTotalAllocatedBytes(precise: true) - allocBefore;
			var gen0 = GC.CollectionCount(0) - gen0Before;
			Console.WriteLine(
				$"{scenario.Name,-12} {ops / sw.Elapsed.TotalSeconds,10:N0} {sw.Elapsed.TotalMilliseconds * 1000 / ops,10:N1} {alloc / 1024.0 / ops,9:N1} {gen0 * 1000.0 / ops,9:N2}");
		}
	}

	/// <summary>
	/// A mortal player, an object of theirs carrying thousands of attributes, and a child of it, for the
	/// attribute-listing scenarios. The mortal owns both, so their reads take the non-privileged
	/// permission path.
	/// </summary>
	private async Task<(DBRef Mortal, DBRef Big, DBRef Kid)> SeedBigObjectsAsync()
	{
		var room = await _database!.GetObjectNodeAsync(new DBRef(0)) is AnySharpObject { AsOptionalContainer: AnySharpContainer start }
			? start
			: throw new InvalidOperationException("Room #0 is not seeded.");
		var mortalRef = await _database.CreatePlayerAsync("ProfileMortal", "unused", new DBRef(0), new DBRef(0), 100);
		if (await _database.GetObjectNodeAsync(mortalRef) is not (AnySharpObject and SharpPlayer mortal))
			throw new InvalidOperationException("The profile mortal was not created.");

		var value = MarkupText.Plain("some value");
		var big = await _database.CreateThingAsync("ProfileBig", room, mortal, room);
		var writes = new List<AttributeWrite>();
		for (var i = 0; i < BigFlat; i++) writes.Add(new AttributeWrite([$"FLAT{i:D4}"], value, mortal, []));
		for (var b = 0; b < BigBranches; b++)
		{
			writes.Add(new AttributeWrite([$"BR{b:D2}"], value, mortal, []));
			for (var l = 0; l < BigLeaves; l++) writes.Add(new AttributeWrite([$"BR{b:D2}", $"L{l:D2}"], value, mortal, []));
		}

		foreach (var batch in writes.Chunk(2048)) await _database.SetAttributesAsync(big, batch);

		var kid = await _database.CreateThingAsync("ProfileKid", room, mortal, room);
		await _database.SetAttributesAsync(kid,
			Enumerable.Range(0, KidAttributes).Select(i => new AttributeWrite([$"KID{i:D3}"], value, mortal, [])).ToList());
		await _database.SetObjectParent(await Node(kid), await Node(big));
		return (mortal.Object.DBRef, (await Node(big)).Object().DBRef, (await Node(kid)).Object().DBRef);

		async ValueTask<AnySharpObject> Node(DBRef dbref)
			=> await _database!.GetObjectNodeAsync(dbref) is AnySharpObject node
				? node
				: throw new InvalidOperationException($"#{dbref.Number} is missing.");
	}

	/// <summary>
	/// Feed kind <c>bench</c>, owned by God: <c>bench/crowd</c> with <paramref name="members"/> players (none
	/// connected) and its default 500 lines stored, <c>bench/long</c> keeping every line with
	/// <paramref name="lines"/> stored and crowd's last member as its one member, and <c>bench/quiet</c> and
	/// <c>radio/1</c>, unlogged, with the same members as crowd. God holds the radio package's line. Returns crowd's last member, who has read none of long's lines.
	/// </summary>
	private async Task<DBRef> SeedFeedsAsync(IMUSHCodeParser baseParser, DBRef god, int members, int lines)
	{
		var mediator = _server!.Services.GetRequiredService<IMediator>();
		var ids = _server.Services.GetRequiredService<IChannelMessageIdSource>();
		foreach (var command in new[]
		{
			$"@feed/define {FeedKind}=me", $"@feed/set {FeedKind}/long/max_messages=0", $"@feed/set {FeedKind}/quiet/logged=no",
			"@feed/define radio=me", "@feed/set radio/1/logged=no"
		})
			await baseParser.FromState(BenchmarkHelpers.FreshState(god)).CommandParse(MarkupText.Plain(command));

		var last = god;
		for (var i = 0; i < members; i++)
		{
			var created = await _database!.CreatePlayerAsync($"ProfileListener{i}", "unused", new DBRef(0), new DBRef(0), 100);
			last = await _database.GetObjectNodeAsync(created) is AnySharpObject player
				? player.Object().DBRef
				: throw new InvalidOperationException($"#{created.Number} is missing.");
			foreach (var (kind, key) in new[] { (FeedKind, "crowd"), (FeedKind, "quiet"), ("radio", "1") })
				await mediator.Send(new SetFeedMemberCommand(kind, key, new SharpFeedMember(last, 0, false, 0)));
		}

		await mediator.Send(new SetFeedMemberCommand(FeedKind, "long", new SharpFeedMember(last, 0, false, 0)));
		if (await _database!.GetObjectNodeAsync(god) is not (AnySharpObject found and SharpPlayer godPlayer))
			throw new InvalidOperationException("God is missing.");
		var godObject = found.Object();
		foreach (var (name, value) in RadioLine)
			await _database.SetAttributeAsync(god, name.Split('`'), MarkupText.Plain(value), godPlayer);
		var crowdLimits = FeedSettings.Defaults;
		var longLimits = FeedSettings.Defaults with { MaxMessages = 0 };
		foreach (var (key, count, limits) in new[] { ("crowd", 500, crowdLimits), ("long", lines, longLimits) })
		{
			for (var i = 0; i < count; i++)
			{
				var message = new SharpFeedMessage(await ids.NextAsync(), FeedKind, key, DateTimeOffset.UtcNow,
					godObject.DBRef, godObject.Name, godObject.DBRef, godObject.Name, null, "", FeedStyles.Say,
					MarkupText.Plain($"Line {i} of the seeded history."), "");
				await mediator.Send(new AppendFeedMessageCommand(message, limits));
			}
		}

		return last;
	}

	/// <summary>
	/// <paramref name="count"/> players logged in on handles of their own, output discarded, each with a THEME
	/// that is code, as a game's would be.
	/// </summary>
	private async Task ConnectPlayersAsync(int count)
	{
		var connectionService = _server!.Services.GetRequiredService<IConnectionService>();
		if (await _database!.GetObjectNodeAsync(new DBRef(1)) is not (AnySharpObject and SharpPlayer god))
			throw new InvalidOperationException("God (#1) is not seeded as a player.");
		for (var i = 0; i < count; i++)
		{
			var player = await _database.CreatePlayerAsync($"ProfileOnline{i}", "unused", new DBRef(0), new DBRef(0), 100);
			await _database.SetAttributeAsync(player, ["THEME"], MarkupText.Plain("if(1,nord)"), god);
			var handle = 900_000L + i;
			await connectionService.Register(handle, "localhost", "localhost", "profile",
				_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
			await connectionService.Bind(handle, player);
		}
	}

	private static async ValueTask RunOnce(IMUSHCodeParser baseParser, DBRef one, Scenario scenario, MString input)
	{
		var parser = baseParser.FromState(BenchmarkHelpers.FreshState(one));
		if (scenario.IsCommandList)
			await parser.CommandListParse(input);
		else if (scenario.IsCommand)
			await parser.CommandParse(input);
		else
			await parser.FunctionParse(input);
	}
}
