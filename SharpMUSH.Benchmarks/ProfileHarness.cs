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
/// [--things N] [--connections N] [--paren-groups on|off] [--index-world N] [--warmup N]</c>. Boots the Lightning-backed benchmark host and reports operations per
/// second, managed bytes allocated per operation, and generation-zero collections per thousand
/// operations. <c>--things</c> is how many things God carries (default 50), for the scenarios that
/// grow with a container. <c>--paren-groups</c> sets that compatibility option for the run; the <c>paren-*</c>
/// scenarios are <see cref="ParenGroupBenchmarks"/>' workloads. <c>--index-world N</c> seeds N things carrying indexed
/// STATUS and LEVEL attributes and adds the attribute index scenarios (<see cref="IndexScenarios"/>), which run only
/// then. <c>--warmup</c> sets the unmeasured runs per scenario (default 200), for scenarios too slow to repeat that often.
/// </summary>
public sealed class ProfileHarness : LightningBaseBenchmark
{
	private sealed record Scenario(string Name, string Input, bool IsCommand, bool IsCommandList = false, bool AsMortal = false,
		bool AsStranger = false);

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
		// God's inventory holds --things "Profile Thing N" objects; these grow with it.
		new("look-me", "look me", true),
		new("locate", "[locate(me,Profile Thing 0,*)]", false),
		new("tel", "[tel(Profile Thing 0,me)]", false),
		// Command piping (help piping): a list that pipes against the same list without, typed and from an attribute.
		new("list2", "think hello;think %>", true, IsCommandList: true),
		new("pipe", "think hello ;| think %|", true, IsCommandList: true),
		new("list2-inc", "@include me/PROFILE_LIST", true, IsCommandList: true),
		new("pipe-inc", "@include me/PROFILE_PIPE", true, IsCommandList: true),
		new("list-50", "@dolist/inline lnum(50)=think ##;think words(%>)", true, IsCommandList: true),
		new("pipe-50", "@dolist/inline lnum(50)=think ## ;| think words(%|)", true, IsCommandList: true),
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
		// {OTHER}: another mortal player, connected, in a room of their own: what +who and +where ask of each
		// player from an unprivileged package object, so every answer goes through the permission checks.
		new("name-o", "[name({OTHER})]", false, AsMortal: true),
		new("loc-o", "[loc({OTHER})]", false, AsMortal: true),
		new("room-o", "[room({OTHER})]", false, AsMortal: true),
		new("get-o", "[get({OTHER}/ALIAS)]", false, AsMortal: true),
		new("idle-o", "[idle({OTHER})]", false, AsMortal: true),
		new("conn-o", "[conn({OTHER})]", false, AsMortal: true),
		// {ONLINE}: every player --connections logged in, each in a room of their own (the +where repro).
		new("name-all", "[strlen(iter({ONLINE},name(%i0)))]", false, AsMortal: true),
		new("loc-all", "[strlen(iter({ONLINE},loc(%i0)))]", false, AsMortal: true),
		new("room-all", "[strlen(iter({ONLINE},room(%i0)))]", false, AsMortal: true),
		new("get-all", "[strlen(iter({ONLINE},get(%i0/ALIAS)))]", false, AsMortal: true),
		new("idle-all", "[strlen(iter({ONLINE},idle(%i0)))]", false, AsMortal: true),
		// u() call overhead: the same loop with a trivial attribute (PROFILE_U is %0) and with no call at all.
		new("id-all", "[strlen(iter({ONLINE},%i0))]", false, AsMortal: true),
		new("u-all", "[strlen(iter({ONLINE},u(me/PROFILE_U,%i0)))]", false, AsMortal: true),
		new("u-obj-all", "[strlen(iter({ONLINE},u({BIG}/PROFILE_U,%i0)))]", false, AsMortal: true),
		new("u-1", "[u(me/PROFILE_U,5)]", false, AsMortal: true),
		new("u-obj-1", "[u({BIG}/PROFILE_U,5)]", false, AsMortal: true),
		new("get-u-1", "[get(me/PROFILE_U)]", false, AsMortal: true),
		new("lambda-1", "[ulambda(#lambda/%0,5)]", false, AsMortal: true),
		new("strlen-m", "[strlen(5)]", false, AsMortal: true),
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

	/// <summary>
	/// Attribute index workloads over the <c>--index-world</c> things (owned by a mortal, STATUS one of ten words
	/// with one "rare" in 10,000, LEVEL 000-999): <c>indexed()</c> against the <c>lsearch()</c> EVAL scan it replaces,
	/// as God, as the owner (<c>-m</c>) and as a stranger who may read none of the copies (<c>-s</c>), where every
	/// hit is hidden. The writes compare a set of an indexed attribute with one of a plain one.
	/// </summary>
	private static readonly Scenario[] IndexScenarios =
	[
		new("idx-eq20", "[indexed(STATUS,=,approved,1,20)]", false),
		new("idx-eq-all", "[words(indexed(STATUS,=,approved))]", false),
		new("idx-rare", "[indexed(STATUS,=,rare)]", false),
		new("idx-count", "[indexed(STATUS,count,approved)]", false),
		new("idx-count-m", "[indexed(STATUS,count,approved)]", false, AsMortal: true),
		new("idx-top10", "[indexed(LEVEL,last,,1,10)]", false),
		new("idx-range20", "[indexed(LEVEL,between,100 200,1,20)]", false),
		new("idx-prefix", "[indexed(STATUS,prefix,appr,1,20)]", false),
		new("idx-eq20-m", "[indexed(STATUS,=,approved,1,20)]", false, AsMortal: true),
		new("idx-eq20-s", "[indexed(STATUS,=,approved,1,20)]", false, AsStranger: true),
		new("ls-index20", "[lsearch(all,INDEX,STATUS:approved,COUNT,20)]", false),
		new("ls-index20-m", "[lsearch(all,INDEX,STATUS:approved,COUNT,20)]", false, AsMortal: true),
		new("ls-eval20", "[lsearch(all,ETHING,\\[strmatch\\(get\\(##/STATUS\\)\\,approved\\)\\],COUNT,20)]", false),
		new("ls-eval20-m", "[lsearch(all,ETHING,\\[strmatch\\(get\\(##/STATUS\\)\\,approved\\)\\],COUNT,20)]", false, AsMortal: true),
		new("ls-eval20-s", "[lsearch(all,ETHING,\\[strmatch\\(get\\(##/STATUS\\)\\,approved\\)\\],COUNT,20)]", false, AsStranger: true),
		new("ls-rare", "[lsearch(all,ETHING,\\[strmatch\\(get\\(##/STATUS\\)\\,rare\\)\\])]", false),
		new("set-idx", "&STATUS {IDXW}=[rand(100000)]", true),
		new("set-plain", "&NOTE {IDXW}=[rand(100000)]", true),
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
		var things = 50;
		var connections = 0;
		var feedMembers = 200;
		var feedLines = 5000;
		bool? parenGroups = null;
		var indexWorld = 0;
		var warmup = 200;
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
				case "--things":
					if (++i >= args.Length || !int.TryParse(args[i], out things) || things < 0)
						throw new ArgumentException("--things requires a non-negative integer.");
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
				case "--index-world":
					if (++i >= args.Length || !int.TryParse(args[i], out indexWorld) || indexWorld < 0)
						throw new ArgumentException("--index-world requires a non-negative integer.");
					break;
				case "--warmup":
					if (++i >= args.Length || !int.TryParse(args[i], out warmup) || warmup < 0)
						throw new ArgumentException("--warmup requires a non-negative integer.");
					break;
				default: selected.AddRange(args[i].Split(',', StringSplitOptions.RemoveEmptyEntries)); break;
			}
		}

		var all = Scenarios.Concat(ParenScenarios(parenGroups ?? false)).Concat(indexWorld > 0 ? IndexScenarios : []).ToArray();
		var scenarios = selected.Count == 0
			? all
			: all.Where(s => selected.Contains(s.Name, StringComparer.OrdinalIgnoreCase)).ToArray();

		// locate and tel name the first thing God carries.
		if (things == 0 && scenarios.Any(scenario => scenario.Input.Contains("Profile Thing 0")))
			throw new ArgumentException("locate and tel need --things of at least 1.");

		var harness = new ProfileHarness();
		await harness.Setup();
		try
		{
			await harness.RunScenariosAsync(scenarios, seconds, wait, things, parenGroups, connections, feedMembers, feedLines, indexWorld,
				warmup);
		}
		finally
		{
			await harness.Cleanup();
		}
	}

	private async Task RunScenariosAsync(Scenario[] scenarios, int seconds, int wait, int things, bool? parenGroups, int connections,
		int feedMembers, int feedLines, int indexWorld, int warmup)
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
		await _database.SetAttributeAsync(new DBRef(1), ["PROFILE_LIST"], MarkupText.Plain("think hello;think %>"), god);
		await _database.SetAttributeAsync(new DBRef(1), ["PROFILE_PIPE"], MarkupText.Plain("think hello ;| think %|"), god);
		await _database.SetAttributeAsync(new DBRef(1), ["PROFILE_ACTIONS"], MarkupText.Plain("@pemit me=Got %0 [add(%0,1)];think [mul(%0,2)];@switch %0=5,{@pemit me=five},{@pemit me=other}"), god);
		for (var i = 0; i < things; i++)
			await baseParser.FromState(BenchmarkHelpers.FreshState(one)).CommandParse(MarkupText.Plain($"@create Profile Thing {i}"));

		var online = await ConnectPlayersAsync(connections,
			ownRooms: scenarios.Any(scenario => scenario.Input.Contains("{ONLINE}")));

		var (mortal, big, kid) = scenarios.Any(scenario => scenario.AsMortal)
			? await SeedBigObjectsAsync()
			: (one, one, one);
		var other = scenarios.Any(scenario => scenario.Input.Contains("{OTHER}"))
			? await SeedOtherPlayerAsync()
			: one;
		var member = scenarios.Any(scenario => scenario.Name.StartsWith("feed", StringComparison.Ordinal)
				|| scenario.Name.StartsWith("radio", StringComparison.Ordinal))
			? await SeedFeedsAsync(baseParser, one, feedMembers, feedLines)
			: one;
		var (indexOwner, indexWrite, stranger) = indexWorld > 0 ? await SeedIndexWorldAsync(indexWorld) : (one, one, one);
		var inputs = scenarios.ToDictionary(scenario => scenario, scenario => MarkupText.Plain(scenario.Input
			.Replace("{IDXW}", $"#{indexWrite.Number}")
			.Replace("{BIG}", $"#{big.Number}").Replace("{KID}", $"#{kid.Number}").Replace("{MEMBER}", member.ToString()).Replace("{OTHER}", $"#{other.Number}")
			.Replace("{ONLINE}", string.Join(' ', online.Select(player => $"#{player.Number}")))));
		DBRef Executor(Scenario scenario) => scenario.AsStranger
			? stranger
			: scenario.AsMortal
				? IndexScenarios.Contains(scenario) ? indexOwner : mortal
				: one;

		foreach (var scenario in scenarios)
		{
			for (var i = 0; i < warmup; i++) await RunOnce(baseParser, Executor(scenario), scenario, inputs[scenario]);
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
			while (ops == 0 || sw.Elapsed.TotalSeconds < seconds)
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

		await _database.SetAttributeAsync(mortalRef, ["PROFILE_U"], MarkupText.Plain("%0"), mortal);
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
		await _database.SetAttributeAsync((await Node(big)).Object().DBRef, ["PROFILE_U"], MarkupText.Plain("%0"), mortal);
		await _database.SetObjectParent(await Node(kid), await Node(big));
		return (mortal.Object.DBRef, (await Node(big)).Object().DBRef, (await Node(kid)).Object().DBRef);

		async ValueTask<AnySharpObject> Node(DBRef dbref)
			=> await _database!.GetObjectNodeAsync(dbref) is AnySharpObject node
				? node
				: throw new InvalidOperationException($"#{dbref.Number} is missing.");
	}

	/// <summary>
	/// A second mortal player with an alias, connected, alone in a room of their own: the player +who and
	/// +where list, seen from an object that neither controls nor shares a room with them.
	/// </summary>
	private async Task<DBRef> SeedOtherPlayerAsync()
	{
		if (await _database!.GetObjectNodeAsync(new DBRef(1)) is not (AnySharpObject and SharpPlayer god))
			throw new InvalidOperationException("God (#1) is not seeded as a player.");
		var roomRef = await _database.CreateRoomAsync("ProfileOtherRoom", god);
		var other = await _database.CreatePlayerAsync("ProfileOther", "unused", roomRef, roomRef, 100);
		await _database.SetAttributeAsync(other, ["ALIAS"], MarkupText.Plain("PO"), god);
		var connectionService = _server!.Services.GetRequiredService<IConnectionService>();
		const long handle = 899_999L;
		await connectionService.Register(handle, "localhost", "localhost", "profile",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		await connectionService.Bind(handle, other);
		return other;
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
	/// that is code, as a game's would be. With <paramref name="ownRooms"/>, each waits in a room of their own
	/// and has an ALIAS, as on a game where +where lists every player apart.
	/// </summary>
	private async Task<List<DBRef>> ConnectPlayersAsync(int count, bool ownRooms)
	{
		var online = new List<DBRef>(count);
		var connectionService = _server!.Services.GetRequiredService<IConnectionService>();
		if (await _database!.GetObjectNodeAsync(new DBRef(1)) is not (AnySharpObject and SharpPlayer god))
			throw new InvalidOperationException("God (#1) is not seeded as a player.");
		for (var i = 0; i < count; i++)
		{
			var room = ownRooms ? await _database.CreateRoomAsync($"ProfileOnlineRoom{i}", god) : new DBRef(0);
			var player = await _database.CreatePlayerAsync($"ProfileOnline{i}", "unused", room, room, 100);
			await _database.SetAttributeAsync(player, ["THEME"], MarkupText.Plain("if(1,nord)"), god);
			if (ownRooms) await _database.SetAttributeAsync(player, ["ALIAS"], MarkupText.Plain($"PO{i}"), god);
			online.Add(player);
			var handle = 900_000L + i;
			await connectionService.Register(handle, "localhost", "localhost", "profile",
				_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
			await connectionService.Bind(handle, player);
		}

		return online;
	}

	/// <summary>
	/// <paramref name="count"/> things owned by a mortal, each with a STATUS, a LEVEL and a description, then STATUS
	/// indexed as text and LEVEL as numbers through the batched background build, as <c>@attribute/index</c> builds
	/// them. Returns the owner, a thing for the write scenarios, and a stranger who may read none of the copies.
	/// </summary>
	private async Task<(DBRef Owner, DBRef Write, DBRef Stranger)> SeedIndexWorldAsync(int count)
	{
		var room = await _database!.GetObjectNodeAsync(new DBRef(0)) is AnySharpObject { AsOptionalContainer: AnySharpContainer start }
			? start
			: throw new InvalidOperationException("Room #0 is not seeded.");
		var ownerRef = await _database.CreatePlayerAsync("IndexOwner", "unused", new DBRef(0), new DBRef(0), count + 100);
		if (await _database.GetObjectNodeAsync(ownerRef) is not (AnySharpObject and SharpPlayer owner))
			throw new InvalidOperationException("The index owner was not created.");
		string[] words = ["approved", "pending", "new", "held", "closed", "rejected", "draft", "staff", "player", "archived"];
		var seeding = Stopwatch.StartNew();
		DBRef last = default;
		for (var i = 0; i < count; i++)
		{
			last = await _database.CreateThingAsync($"Indexed {i}", room, owner, room);
			var status = i % 10_000 == 5 ? "rare" : words[i % words.Length];
			await _database.SetAttributesAsync(last,
			[
				new AttributeWrite(["STATUS"], MarkupText.Plain(status), owner, []),
				new AttributeWrite(["LEVEL"], MarkupText.Plain((i * 7919 % 1000).ToString("D3")), owner, []),
				new AttributeWrite(["DESCRIBE"], MarkupText.Plain($"A generated thing, number {i}, for the index scenarios."), owner, []),
			]);
		}

		Console.WriteLine($"seeded {count} things in {seeding.Elapsed.TotalSeconds:N1}s");
		var builder = _server!.Services.GetRequiredService<IAttributeIndexBuilder>();
		var indexes = _server.Services.GetRequiredService<IAttributeIndexStore>();
		foreach (var (name, options) in new[] { ("STATUS", AttributeIndexOptions.Default), ("LEVEL", new AttributeIndexOptions(Numeric: true)) })
		{
			await _database.CreateOrUpdateAttributeEntryAsync(name, []);
			if (await indexes.SetAttributeIndexAsync(name, options) is not SharpAttributeEntry)
				throw new InvalidOperationException($"{name} could not be indexed.");
			var build = Stopwatch.StartNew();
			var run = builder.Build(name);
			await run.Completion;
			Console.WriteLine($"index {name}: {run.Indexed} copies on {run.Scanned} objects in {build.Elapsed.TotalMilliseconds:N0} ms");
		}

		foreach (var usage in indexes.GetAttributeIndexUsage())
		{
			Console.WriteLine($"index {usage.Name}: {usage.Rows} rows, {usage.Pages} pages, {usage.Bytes} bytes");
		}

		var stranger = await _database.CreatePlayerAsync("IndexStranger", "unused", new DBRef(0), new DBRef(0), 100);
		return (owner.Object.DBRef, last, stranger);
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
