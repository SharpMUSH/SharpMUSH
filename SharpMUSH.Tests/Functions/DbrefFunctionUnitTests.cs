using SharpMUSH.Library.Definitions;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class DbrefFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;

	private IConnectionService ConnectionService =>
		WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	[Test]
	public async Task Loc()
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain("loc(%#)"));
		await Assert.That(result.ToPlainText()).StartsWith("#0:");
	}

	[Test]
	public async Task Controls()
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain("controls(%#,%#)"));
		await Assert.That(result.ToPlainText()).IsEqualTo("1");
	}

	[Test]
	public async Task Home()
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain("home(%#)"));
		await Assert.That(result.ToPlainText()).StartsWith("#0:");
	}

	[Test]
	public async Task LocOnCurrentRoom()
	{
		// loc() on a room is its drop-to, or says it has none.
		var result = await Parser.EvaluateAsync(MarkupText.Plain("loc(%l)"));
		await Assert.That(result.ToPlainText()).Matches("^(#[0-9]+:[0-9]+|#-1 NO DROP-TO)$");
	}

	[Test]
	public async Task HomeOnCurrentRoom()
	{
		// home() on a room is its drop-to, or says it has none.
		var result = await Parser.EvaluateAsync(MarkupText.Plain("home(%l)"));
		await Assert.That(result.ToPlainText()).Matches("^(#[0-9]+:[0-9]+|#-1 NO DROP-TO)$");
	}


	[Test]
	[Arguments("entrances(%l)", "")]
	public async Task Entrances(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("followers(%#)", "")]
	public async Task Followers(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("following(%#)", "")]
	public async Task Following(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("locate(%#,nonsense-does-not-exist,*)", ErrorMessages.Returns.NoMatch)]
	[Arguments("first(locate(%#,me,*),:)", "#1")]
	[Arguments("first(locate(%#,here,*),:)", "#0")]
	public async Task Locate(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("create({0})", "locate(%#,{0},*)")]
	// TODO: Enable when tel() is implemented
	// [Arguments("tel(create(content-object),create(container-object))", "locate(%#,container-object's content-object,*)")]
	public async Task CreateAndLocate(string create, string locate)
	{
		// Unique object name per run. The unit suite shares ONE accumulating DB and CI retries re-run this
		// test against it; with a fixed name a retry creates a SECOND object of the same name, so locate()
		// then matches an ambiguous set and a single transient create->locate miss becomes a permanent
		// failure. A per-invocation name keeps the create->locate mapping 1:1 and lets retries re-run clean.
		var name = $"silly-object-{Guid.NewGuid():N}";
		var result = await Parser.EvaluateAsync(MarkupText.Plain(string.Format(create, name)));
		var located = await Parser.EvaluateAsync(MarkupText.Plain(string.Format(locate, name)));

		await Assert.That(result.ToPlainText()).IsEqualTo(located.ToPlainText());
	}

	[Test]
	public async Task Lock_OnFreshObject()
	{
		// Create a dedicated object so we don't collide with other tests that set locks on %#
		var createResult = await Parser.EvaluateAsync(MarkupText.Plain("create(LockTestObj_Dbref)"));
		var dbref = createResult.ToPlainText();

		var result = await Parser.EvaluateAsync(MarkupText.Plain($"lock({dbref})"));
		await Assert.That(result.ToPlainText()).IsEqualTo("*UNLOCKED*");
	}

	[Test]
	[Arguments("Basic")]
	[Arguments("basic")]
	[Arguments("BASIC")]
	public async Task Elock_NoLock_Passes(string lockName)
	{
		// Create a dedicated object so parallel tests setting locks on %# don't interfere
		var createResult = await Parser.EvaluateAsync(MarkupText.Plain($"create(ElockTestObj_{lockName})"));
		var dbref = createResult.ToPlainText();

		var result = await Parser.EvaluateAsync(MarkupText.Plain($"elock({dbref}/{lockName},%#)"));
		await Assert.That(result.ToPlainText()).IsEqualTo("1");
	}

	[Test]
	public async Task Elock_NoLock_DefaultLock_Passes()
	{
		// elock(obj, victim) without /lockname defaults to Basic
		var createResult = await Parser.EvaluateAsync(MarkupText.Plain("create(ElockTestObj_Default)"));
		var dbref = createResult.ToPlainText();

		var result = await Parser.EvaluateAsync(MarkupText.Plain($"elock({dbref},%#)"));
		await Assert.That(result.ToPlainText()).IsEqualTo("1");
	}

	[Test]
	public async Task Elock_InvalidObject_ReturnsError()
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain("elock(#99999,%#)"));
		await Assert.That(result.ToPlainText()).IsEqualTo("#-1 NO MATCH");
	}

	[Test]
	public async Task Elock_InvalidVictim_ReturnsError()
	{
		var createResult = await Parser.EvaluateAsync(MarkupText.Plain("create(ElockTestObj_BadVictim)"));
		var dbref = createResult.ToPlainText();

		var result = await Parser.EvaluateAsync(MarkupText.Plain($"elock({dbref}/Basic,#99999)"));
		await Assert.That(result.ToPlainText()).IsEqualTo(ErrorMessages.Returns.NoMatch);
	}

	[Test]
	public async Task Lock_CaseInsensitive_LockName()
	{
		var createResult = await Parser.EvaluateAsync(MarkupText.Plain("create(LockTestObj_CaseInsensitive)"));
		var dbref = createResult.ToPlainText();

		var resultBasic = await Parser.EvaluateAsync(MarkupText.Plain($"lock({dbref}/Basic)"));
		var resultLower = await Parser.EvaluateAsync(MarkupText.Plain($"lock({dbref}/basic)"));
		var resultUpper = await Parser.EvaluateAsync(MarkupText.Plain($"lock({dbref}/BASIC)"));

		await Assert.That(resultBasic.ToPlainText()).IsEqualTo(resultLower.ToPlainText());
		await Assert.That(resultBasic.ToPlainText()).IsEqualTo(resultUpper.ToPlainText());
	}

	[Test]
	public async Task Lock_EmptyLockName_AfterSlash()
	{
		var createResult = await Parser.EvaluateAsync(MarkupText.Plain("create(LockTestObj_EmptySlash)"));
		var dbref = createResult.ToPlainText();

		var result = await Parser.EvaluateAsync(MarkupText.Plain($"lock({dbref}/)"));
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task Lockflags_CaseInsensitive()
	{
		var createResult = await Parser.EvaluateAsync(MarkupText.Plain("create(LockflagsTestObj_Case)"));
		var dbref = createResult.ToPlainText();

		var resultBasic = await Parser.EvaluateAsync(MarkupText.Plain($"lockflags({dbref}/Basic)"));
		var resultLower = await Parser.EvaluateAsync(MarkupText.Plain($"lockflags({dbref}/basic)"));

		await Assert.That(resultBasic.ToPlainText()).IsEqualTo(resultLower.ToPlainText());
	}

	[Test]
	[Arguments("rloc(#1,0)", "#0")]
	public async Task Rloc(string str, string expected)
	{
		TestDiagnostics.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("slev()", "")]
	public async Task Slev(string str, string expected)
	{
		TestDiagnostics.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("llocks(%#)", "")]
	public async Task Llocks(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("llockflags()", "")]
	[Arguments("llockflags(Basic)", "")]
	public async Task Llockflags(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task LockownerReportsAbsentLock()
	{
		var created = await Parser.EvaluateAsync(MarkupText.Plain($"create(LockOwnerAbsent{Guid.NewGuid():N})"));
		var target = created.ToPlainText();
		var result = await Parser.EvaluateAsync(MarkupText.Plain($"lockowner({target})"));
		await Assert.That(result.ToPlainText()).IsEqualTo("#-1 NO SUCH LOCK");
	}


	[Test]
	[Arguments("lockfilter(%# %l,Basic,1)", "")]
	public async Task Lockfilter(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("andflags(%#,P)", "1")]       // P = player type
	[Arguments("andflags(%#,PW)", "1")]      // P = player, W = wizard
	[Arguments("andflags(%#,TW)", "0")]      // T = thing (executor is not a thing)
	[Arguments("andflags(%#,Pr)", "0")]      // P = player, r = royalty (executor lacks royalty)
	[Arguments("andflags(%#,Wc)", "1")]      // oracle andflags.1: W=wizard, c=connected
	[Arguments("andflags(%#,W_)", "0")]      // oracle andflags.2: _=puppet (god isn't puppet)
	[Arguments("andflags(%#,W~)", "0")]      // oracle andflags.3: ~=noaccents
	[Arguments("andflags(%#,W!~)", "1")]     // oracle andflags.4: !~=not noaccents
	[Arguments("andflags(%#,WP)", "1")]      // oracle andflags.6: W=wizard, P=player
	[Arguments("andflags(%#,WT)", "0")]      // oracle andflags.7: T=thing
	public async Task Andflags(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>
	/// x is CLOUDY on an exit and TERSE on a player or thing; letter_to_flagptr takes the one whose type
	/// covers the object's, so a TERSE thing answers x.
	/// </summary>
	[Test]
	public async Task AndflagsReadsASharedLetterByTheObjectsType()
	{
		var thing = (await Parser.EvaluateAsync(MarkupText.Plain(
			$"create({TestIsolationHelpers.GenerateUniqueName("TerseLetter")})"))).ToPlainText();
		await Parser.FunctionParse(MarkupText.Plain($"set({thing},TERSE)"));

		var result = await Parser.EvaluateAsync(MarkupText.Plain($"andflags({thing},Tx)"));
		await Assert.That(result.ToPlainText()).IsEqualTo("1");
	}

	[Test]
	[Arguments("orflags(%#,PLAYER)", "1")]
	[Arguments("orflags(%#,WIZARD PLAYER)", "1")]
	[Arguments("orflags(%#,~W)", "1")]       // oracle orflags.1
	[Arguments("orflags(%#,~_)", "0")]       // oracle orflags.2: ~=noaccents, _=puppet — neither set
	[Arguments("orflags(%#,ET)", "0")]       // oracle orflags.5: E=exit type, T=thing type
	[Arguments("orflags(%#,EP)", "1")]       // oracle orflags.6: E=exit, P=player — player matches
	public async Task Orflags(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("andlflags(%#,PLAYER)", "1")]
	[Arguments("andlflags(%#,wizard connected)", "1")]    // oracle andlflags.1
	[Arguments("andlflags(%#,wizard flunky)", "0")]       // oracle andlflags.2
	[Arguments("andlflags(%#,wizard !noaccents)", "1")]   // oracle andlflags.3
	[Arguments("andlflags(%#,wizard !puppet)", "1")]      // oracle andlflags.4
	[Arguments("andlflags(%#,puppet wizard)", "0")]       // oracle andlflags.5
	[Arguments("andlflags(%#,noaccents wizard)", "0")]    // oracle andlflags.6
	[Arguments("andlflags(%#,player connected)", "1")]    // oracle andlflags.8
	public async Task Andlflags(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("orlflags(%#,PLAYER)", "1")]
	[Arguments("orlflags(%#,wizard connected)", "1")]    // oracle orlflags.1
	[Arguments("orlflags(%#,wizard flunky)", "1")]       // oracle orlflags.2
	[Arguments("orlflags(%#,flunky wizard)", "1")]       // oracle orlflags.3
	[Arguments("orlflags(%#,myopic noaccents)", "0")]    // oracle orlflags.4
	[Arguments("orlflags(%#,myopic !noaccents)", "1")]   // oracle orlflags.5
	[Arguments("orlflags(%#,thing player)", "1")]        // oracle orlflags.7
	public async Task Orlflags(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}


	[Test]
	[Arguments("andlpowers(%#,Guest)", "0")]
	public async Task Andlpowers(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("orlpowers(%#,Guest)", "0")]
	public async Task Orlpowers(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task NextDbref_ReturnsValidDbref()
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain("nextdbref()"));
		var dbrefStr = result.ToPlainText();

		await Assert.That(dbrefStr).StartsWith("#");
		await Assert.That(dbrefStr).Contains(":");

		var parts = dbrefStr.TrimStart('#').Split(':');
		await Assert.That(parts.Length).IsEqualTo(2);
		await Assert.That(int.TryParse(parts[0], out _)).IsTrue();
	}

	[Test]
	public async Task Lsearchr_WithRegexPattern()
	{
		await Parser.FunctionParse(MarkupText.Plain("create(TestObject123)"));

		var result = await Parser.EvaluateAsync(MarkupText.Plain("lsearchr(%#,NAME=TestObject[0-9]+)"));
		var dbrefs = result.ToPlainText();

		// The result can be empty if the object wasn't visible or permissions prevented it
		await Assert.That(dbrefs).IsNotNull();
	}

	[Test]
	[NotInParallel] // two searches over every player, which other tests create concurrently
	public async Task Lsearchr_BehavesLikeLsearch_WhenNoRegexNeeded()
	{
		var lsearchResult = await Parser.EvaluateAsync(MarkupText.Plain("lsearch(%#,TYPE=PLAYER)"));
		var lsearchrResult = await Parser.EvaluateAsync(MarkupText.Plain("lsearchr(%#,TYPE=PLAYER)"));

		await Assert.That(lsearchrResult.ToPlainText()).IsEqualTo(lsearchResult.ToPlainText());
	}

	/// <summary>
	/// Tests that functions accepting dbrefs also accept objids (#N:timestamp format).
	/// </summary>
	[Test]
	public async Task ObjId_AcceptedByLocFunction()
	{
		var objIdResult = await Parser.EvaluateAsync(MarkupText.Plain("objid(%#)"));
		var objId = objIdResult.ToPlainText();

		var resultWithObjId = await Parser.EvaluateAsync(MarkupText.Plain($"loc({objId})"));
		var resultWithDbRef = await Parser.EvaluateAsync(MarkupText.Plain("loc(%#)"));

		await Assert.That(resultWithObjId.ToPlainText()).IsEqualTo(resultWithDbRef.ToPlainText());
	}

	[Test]
	public async Task ObjId_AcceptedByNameFunction()
	{
		var objIdResult = await Parser.EvaluateAsync(MarkupText.Plain("objid(%#)"));
		var objId = objIdResult.ToPlainText();

		var resultWithObjId = await Parser.EvaluateAsync(MarkupText.Plain($"name({objId})"));
		var resultWithDbRef = await Parser.EvaluateAsync(MarkupText.Plain("name(%#)"));

		await Assert.That(resultWithObjId.ToPlainText()).IsEqualTo(resultWithDbRef.ToPlainText());
	}

	[Test]
	public async Task ObjId_AcceptedByLocateFunction()
	{
		var objIdResult = await Parser.EvaluateAsync(MarkupText.Plain("objid(%#)"));
		var objId = objIdResult.ToPlainText();

		var locateResult = await Parser.EvaluateAsync(MarkupText.Plain($"first(locate(%#,{objId},*),;)"));

		await Assert.That(locateResult.ToPlainText()).StartsWith("#1");
	}

	[Test]
	public async Task ObjId_WithWrongTimestamp_FailsToLocate()
	{
		var locateResult = await Parser.EvaluateAsync(MarkupText.Plain("locate(%#,#1:0,*)"));

		await Assert.That(locateResult.ToPlainText()).IsEqualTo(ErrorMessages.Returns.NoMatch);
	}

	[Test]
	public async Task PercentColon_ReturnsFullObjId()
	{
		// %: should return the full objid of the enactor (e.g. #1:1234567890)
		var result = await Parser.EvaluateAsync(MarkupText.Plain("%:"));
		var objId = result.ToPlainText();

		await Assert.That(objId).Matches(@"^#\d+:\d+$");

		var objIdFromFunc = await Parser.EvaluateAsync(MarkupText.Plain("objid(%#)"));
		await Assert.That(objId).IsEqualTo(objIdFromFunc.ToPlainText());
	}

	/// <summary>
	/// PennMUSH <c>fun_loc</c> (<c>fundb.c:1459</c>) returns <c>Location(it)</c>, and an exit's location
	/// is its destination — not the room it sits in, which is what <c>where()</c> reports.
	/// </summary>
	[Test]
	public async Task LocOfAnExitIsItsDestination()
	{
		var destName = TestIsolationHelpers.GenerateUniqueName("LocExitDest");
		var digResult = await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dig {destName}"));
		var destDbRef = digResult.Message.ToPlainText()!.Trim();

		var exitName = TestIsolationHelpers.GenerateUniqueName("LocExit");
		var openResult = await CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@open {exitName}={destDbRef}"));
		var exitDbRef = openResult.Message.ToPlainText()!.Trim();

		var result = await Parser.EvaluateAsync(MarkupText.Plain($"loc({exitDbRef})"));

		await Assert.That(result.ToPlainText()).IsEqualTo(destDbRef);
	}

	/// <summary>
	/// The complement: <c>where()</c> reports the room an exit sits in (PennMUSH <c>Source()</c>).
	/// </summary>
	[Test]
	public async Task WhereOfAnExitIsTheRoomItSitsIn()
	{
		var destName = TestIsolationHelpers.GenerateUniqueName("WhereExitDest");
		var digResult = await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dig {destName}"));
		var destDbRef = digResult.Message.ToPlainText()!.Trim();

		var exitName = TestIsolationHelpers.GenerateUniqueName("WhereExit");
		var openResult = await CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@open {exitName}={destDbRef}"));
		var exitDbRef = openResult.Message.ToPlainText()!.Trim();

		var result = await Parser.EvaluateAsync(MarkupText.Plain($"where({exitDbRef})"));

		await Assert.That(result.ToPlainText()).IsNotEqualTo(destDbRef);
	}
}
