using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class DbrefFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	public async Task Loc()
	{
		var result = (await Parser.FunctionParse(MModule.single("loc(%#)")))?.Message!;
		await Assert.That(result.ToPlainText()).StartsWith("#0:");
	}

	[Test]
	public async Task Controls()
	{
		var result = (await Parser.FunctionParse(MModule.single("controls(%#,%#)")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("1");
	}

	[Test]
	public async Task Home()
	{
		var result = (await Parser.FunctionParse(MModule.single("home(%#)")))?.Message!;
		await Assert.That(result.ToPlainText()).StartsWith("#0:");
	}

	[Test]
	public async Task LocOnCurrentRoom()
	{
		// loc() on the current room (%l) should return drop-to or #-1
		var result = (await Parser.FunctionParse(MModule.single("loc(%l)")))?.Message!;
		await Assert.That(result.ToPlainText()).Matches("^(#[0-9]+:[0-9]+|#-1)$");
	}

	[Test]
	public async Task HomeOnCurrentRoom()
	{
		// home() on the current room (%l) should return drop-to or #-1
		var result = (await Parser.FunctionParse(MModule.single("home(%l)")))?.Message!;
		await Assert.That(result.ToPlainText()).Matches("^(#[0-9]+:[0-9]+|#-1)$");
	}


	[Test]
	[Arguments("entrances(%l)", "")]
	public async Task Entrances(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("followers(%#)", "")]
	public async Task Followers(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("following(%#)", "")]
	public async Task Following(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("locate(%#,nonsense-does-not-exist,*)", "#-1")]
	[Arguments("first(locate(%#,me,*),:)", "#1")]
	[Arguments("first(locate(%#,here,*),:)", "#0")]
	public async Task Locate(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[NotInParallel]
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
		var result = (await Parser.FunctionParse(MModule.single(string.Format(create, name))))?.Message!;
		var located = (await Parser.FunctionParse(MModule.single(string.Format(locate, name))))?.Message!;

		await Assert.That(result.ToPlainText()).IsEqualTo(located.ToPlainText());
	}

	[Test]
	public async Task Lock_OnFreshObject()
	{
		// Create a dedicated object so we don't collide with other tests that set locks on %#
		var createResult = (await Parser.FunctionParse(MModule.single("create(LockTestObj_Dbref)")))?.Message!;
		var dbref = createResult.ToPlainText();

		var result = (await Parser.FunctionParse(MModule.single($"lock({dbref})")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("*UNLOCKED*");
	}

	[Test]
	[Arguments("Basic")]
	[Arguments("basic")]
	[Arguments("BASIC")]
	public async Task Elock_NoLock_Passes(string lockName)
	{
		// Create a dedicated object so parallel tests setting locks on %# don't interfere
		var createResult = (await Parser.FunctionParse(MModule.single($"create(ElockTestObj_{lockName})")))?.Message!;
		var dbref = createResult.ToPlainText();

		var result = (await Parser.FunctionParse(MModule.single($"elock({dbref}/{lockName},%#)")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("1");
	}

	[Test]
	public async Task Elock_NoLock_DefaultLock_Passes()
	{
		// elock(obj, victim) without /lockname defaults to Basic
		var createResult = (await Parser.FunctionParse(MModule.single("create(ElockTestObj_Default)")))?.Message!;
		var dbref = createResult.ToPlainText();

		var result = (await Parser.FunctionParse(MModule.single($"elock({dbref},%#)")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("1");
	}

	[Test]
	public async Task Elock_InvalidObject_ReturnsError()
	{
		var result = (await Parser.FunctionParse(MModule.single("elock(#99999,%#)")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("#-1 NO MATCH");
	}

	[Test]
	public async Task Elock_InvalidVictim_ReturnsError()
	{
		var createResult = (await Parser.FunctionParse(MModule.single("create(ElockTestObj_BadVictim)")))?.Message!;
		var dbref = createResult.ToPlainText();

		var result = (await Parser.FunctionParse(MModule.single($"elock({dbref}/Basic,#99999)")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("#-1");
	}

	[Test]
	public async Task Lock_CaseInsensitive_LockName()
	{
		var createResult = (await Parser.FunctionParse(MModule.single("create(LockTestObj_CaseInsensitive)")))?.Message!;
		var dbref = createResult.ToPlainText();

		var resultBasic = (await Parser.FunctionParse(MModule.single($"lock({dbref}/Basic)")))?.Message!;
		var resultLower = (await Parser.FunctionParse(MModule.single($"lock({dbref}/basic)")))?.Message!;
		var resultUpper = (await Parser.FunctionParse(MModule.single($"lock({dbref}/BASIC)")))?.Message!;

		await Assert.That(resultBasic.ToPlainText()).IsEqualTo(resultLower.ToPlainText());
		await Assert.That(resultBasic.ToPlainText()).IsEqualTo(resultUpper.ToPlainText());
	}

	[Test]
	public async Task Lock_EmptyLockName_AfterSlash()
	{
		var createResult = (await Parser.FunctionParse(MModule.single("create(LockTestObj_EmptySlash)")))?.Message!;
		var dbref = createResult.ToPlainText();

		var result = (await Parser.FunctionParse(MModule.single($"lock({dbref}/)")))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task Lockflags_CaseInsensitive()
	{
		var createResult = (await Parser.FunctionParse(MModule.single("create(LockflagsTestObj_Case)")))?.Message!;
		var dbref = createResult.ToPlainText();

		var resultBasic = (await Parser.FunctionParse(MModule.single($"lockflags({dbref}/Basic)")))?.Message!;
		var resultLower = (await Parser.FunctionParse(MModule.single($"lockflags({dbref}/basic)")))?.Message!;

		await Assert.That(resultBasic.ToPlainText()).IsEqualTo(resultLower.ToPlainText());
	}

	[Test]
	[Arguments("rloc(#1,0)", "#0")]
	public async Task Rloc(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("slev()", "")]
	public async Task Slev(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("stext()", "")]
	public async Task Stext(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("llocks(%#)", "")]
	public async Task Llocks(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("llockflags()", "")]
	[Arguments("llockflags(Basic)", "")]
	public async Task Llockflags(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("first(lockowner(%#),:)", "#1")]
	public async Task Lockowner(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("lockfilter(%# %l,Basic,1)", "")]
	public async Task Lockfilter(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
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
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
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
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
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
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
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
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}


	[Test]
	[Arguments("andlpowers(%#,Guest)", "0")]
	public async Task Andlpowers(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("orlpowers(%#,Guest)", "0")]
	public async Task Orlpowers(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task NextDbref_ReturnsValidDbref()
	{
		var result = (await Parser.FunctionParse(MModule.single("nextdbref()")))?.Message!;
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
		await Parser.FunctionParse(MModule.single("create(TestObject123)"));

		var result = (await Parser.FunctionParse(MModule.single("lsearchr(%#,NAME=TestObject[0-9]+)")))?.Message!;
		var dbrefs = result.ToPlainText();

		// The result can be empty if the object wasn't visible or permissions prevented it
		await Assert.That(dbrefs).IsNotNull();
	}

	[Test]
	[NotInParallel]
	public async Task Lsearchr_BehavesLikeLsearch_WhenNoRegexNeeded()
	{
		var lsearchResult = (await Parser.FunctionParse(MModule.single("lsearch(%#,TYPE=PLAYER)")))?.Message!;
		var lsearchrResult = (await Parser.FunctionParse(MModule.single("lsearchr(%#,TYPE=PLAYER)")))?.Message!;

		await Assert.That(lsearchrResult.ToPlainText()).IsEqualTo(lsearchResult.ToPlainText());
	}

	/// <summary>
	/// Tests that functions accepting dbrefs also accept objids (#N:timestamp format).
	/// </summary>
	[Test]
	public async Task ObjId_AcceptedByLocFunction()
	{
		var objIdResult = (await Parser.FunctionParse(MModule.single("objid(%#)")))?.Message!;
		var objId = objIdResult.ToPlainText();

		var resultWithObjId = (await Parser.FunctionParse(MModule.single($"loc({objId})")))?.Message!;
		var resultWithDbRef = (await Parser.FunctionParse(MModule.single("loc(%#)")))?.Message!;

		await Assert.That(resultWithObjId.ToPlainText()).IsEqualTo(resultWithDbRef.ToPlainText());
	}

	[Test]
	public async Task ObjId_AcceptedByNameFunction()
	{
		var objIdResult = (await Parser.FunctionParse(MModule.single("objid(%#)")))?.Message!;
		var objId = objIdResult.ToPlainText();

		var resultWithObjId = (await Parser.FunctionParse(MModule.single($"name({objId})")))?.Message!;
		var resultWithDbRef = (await Parser.FunctionParse(MModule.single("name(%#)")))?.Message!;

		await Assert.That(resultWithObjId.ToPlainText()).IsEqualTo(resultWithDbRef.ToPlainText());
	}

	[Test]
	public async Task ObjId_AcceptedByLocateFunction()
	{
		var objIdResult = (await Parser.FunctionParse(MModule.single("objid(%#)")))?.Message!;
		var objId = objIdResult.ToPlainText();

		var locateResult = (await Parser.FunctionParse(MModule.single($"first(locate(%#,{objId},*),;)")))?.Message!;

		await Assert.That(locateResult.ToPlainText()).StartsWith("#1");
	}

	[Test]
	public async Task ObjId_WithWrongTimestamp_FailsToLocate()
	{
		var locateResult = (await Parser.FunctionParse(MModule.single("locate(%#,#1:0,*)")))?.Message!;

		await Assert.That(locateResult.ToPlainText()).IsEqualTo("#-1");
	}

	[Test]
	public async Task PercentColon_ReturnsFullObjId()
	{
		// %: should return the full objid of the enactor (e.g. #1:1234567890)
		var result = (await Parser.FunctionParse(MModule.single("%:")))?.Message!;
		var objId = result.ToPlainText();

		await Assert.That(objId).Matches(@"^#\d+:\d+$");

		var objIdFromFunc = (await Parser.FunctionParse(MModule.single("objid(%#)")))?.Message!;
		await Assert.That(objId).IsEqualTo(objIdFromFunc.ToPlainText());
	}
}
