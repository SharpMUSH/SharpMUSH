using SharpMUSH.Library.Clusters;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// RhostMUSH's clusters after an import has merged each into one object: the numbers of the objects merged into it
/// name it, the cluster_*() functions and iscluster() work on it, and @cluster under the RhostMUSH dialect does.
/// </summary>
public class RhostClusterTests : ServerTestBase
{
	private static int _nextMember = 4_000_000 + Random.Shared.Next(0, 100_000) * 10;

	private static IDisposable Dialect(string dialect) => TestOptionsOverride.Scope(options => options with
	{
		Compatibility = options.Compatibility with { Dialect = dialect }
	});

	/// <summary>A thing with VA, VB and FN set, recorded as a cluster that also answers to a number no object holds.</summary>
	private async Task<(DBRef Cluster, int Member)> ClusterAsync()
	{
		var cluster = DBRef.Parse(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName("Cluster")}"));
		await Cmd($"&VA {cluster}=This is a test");
		await Cmd($"&VB {cluster}=This is another test.");
		await Cmd($"&VC {cluster}=Test Test Test whisooo!");
		await Cmd($"&FN {cluster}=Got %0 and %1");
		var objid = (await Mediator.Send(new GetObjectNodeQuery(cluster))).Expect<AnySharpObject>().Object().DBRef;
		var member = Interlocked.Add(ref _nextMember, 10);
		await ObjectClusters.AddAsync(Mediator, objid, [member]);
		return (cluster, member);
	}

	[Test]
	public async Task AMergedObjectsNumberNamesTheCluster()
	{
		var (cluster, member) = await ClusterAsync();

		await Assert.That(await Eval($"get(#{member}/VA)")).IsEqualTo("This is a test");
		await Assert.That(await Eval($"num(#{member})")).IsEqualTo($"#{cluster.Number}");
		await Assert.That(await Eval($"[iscluster(#{member})] [iscluster({cluster})] [iscluster(me)] [iscluster(#{member + 1})]"))
			.IsEqualTo("1 1 0 #-1");
		await Assert.That(await Eval($"cluster_stats({cluster},1)")).IsEqualTo($"#{cluster.Number} #{member}");
	}

	[Test]
	public async Task ClusterFunctionsAreTheirNamesakesOnTheCluster()
	{
		var (cluster, member) = await ClusterAsync();
		var plain = DBRef.Parse(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName("NotCluster")}"));
		await Cmd($"&VA {plain}=plain");

		await Assert.That(await Eval($"[cluster_get(#{member}/VA)]|[cluster_get({plain}/VA)]"))
			.IsEqualTo("This is a test|#-1 NO SUCH CLUSTER");
		await Assert.That(await Eval($"[cluster_get_eval({cluster}/FN)]|[cluster_xget(#{member},VB)]"))
			.IsEqualTo("Got  and |This is another test.");
		await Assert.That(await Eval($"[cluster_default({cluster}/NOPE,none)]|[cluster_edefault({cluster}/NOPE,add(1,2))]"))
			.IsEqualTo("none|3");
		await Assert.That(await Eval($"[cluster_u({cluster}/FN,a,b)]|[cluster_ulocal(#{member}/FN,c,d)]|[cluster_udefault({cluster}/NOPE,dflt,x)]"))
			.IsEqualTo("Got a and b|Got c and d|dflt");
		await Assert.That(await Eval($"[cluster_hasattr({cluster}/VA)] [cluster_hasattr({cluster},NOPE)] [cluster_hasflag({cluster},wizard)]"))
			.IsEqualTo("1 0 0");
		await Assert.That(await Eval($"cluster_lattr({cluster}/V*)")).IsEqualTo("VA VB VC");
		await Assert.That(await Eval($"[cluster_attrcnt({cluster})] [cluster_attrcnt({cluster}/V*)] [cluster_attrcnt({cluster}/^V\\[AB\\]$)] [cluster_vattrcnt({cluster})]"))
			.IsEqualTo("4 3 2 1");
		await Assert.That(await Eval($"[cluster_stats({cluster},0)] [cluster_stats({cluster},4,VB)] [cluster_stats({plain},0)]"))
			.IsEqualTo($"1 #{cluster.Number} #-1 NO SUCH CLUSTER");
		await Assert.That(await Eval($"cluster_add({cluster},{plain})")).IsEqualTo("#-1 A CLUSTER IS ONE OBJECT HERE");
	}

	[Test]
	public async Task ClusterGrepMatchesAnywhereOrAsAWord()
	{
		var (cluster, _) = await ClusterAsync();
		var number = cluster.Number;
		var plain = DBRef.Parse(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName("NotCluster")}"));
		await Cmd($"&VA {plain}=This is plain");

		await Assert.That(await Eval($"cluster_grep({plain},v*,is)")).IsEqualTo("#-1 NO SUCH CLUSTER");

		await Assert.That(await Eval($"cluster_grep({cluster},v*,is)")).IsEqualTo("VA VB VC");
		await Assert.That(await Eval($"cluster_grep({cluster},v*,is,,1)")).IsEqualTo("VA VB");
		await Assert.That(await Eval($"cluster_grep({cluster},v*,*another*,1)")).IsEqualTo($"[#{number}] VB");
		await Assert.That(await Eval($"cluster_grep({cluster},v*,whisooo,2)")).IsEqualTo($"#{number}/VC");
		await Assert.That(await Eval($"[cluster_regrep({cluster},v*,^This)]|[cluster_regrepi({cluster},v*,^this)]|[cluster_regrep({cluster},^V\\[BC\\]$,test,1)]"))
			.IsEqualTo("VA VB|VA VB|VB");
	}

	[Test]
	public async Task ClusterSetAndWipeChangeTheCluster()
	{
		var (cluster, member) = await ClusterAsync();

		await Eval($"cluster_set(#{member},TMP_ONE:one)");
		await Eval($"cluster_set({cluster},TMP_TWO:two)");
		await Assert.That(await Eval($"[get({cluster}/TMP_ONE)] [get({cluster}/TMP_TWO)]")).IsEqualTo("one two");
		await Assert.That(await Eval($"cluster_wipe({cluster}/TMP_*,1)")).IsEqualTo("#-1 CLUSTER_WIPE TAKES A WILDCARD PATTERN ONLY");
		await Eval($"cluster_wipe(#{member}/TMP_*)");
		await Assert.That(await Eval($"lattr({cluster}/TMP_*)")).IsEmpty();
		await Assert.That(await Eval($"get({cluster}/VA)")).IsEqualTo("This is a test");
	}

	[Test]
	public async Task ClusterCommandWorksOnTheClustersObject()
	{
		var (cluster, member) = await ClusterAsync();
		using (Dialect("rhost"))
		{
			await Assert.That(await Cmd($"@cluster/list #{member}")).Contains($"answering to #{cluster.Number} #{member}");
			await Cmd($"@cluster/set #{member}=ZZ:[add(2,3)]");
			await Cmd($"@cluster/edit #{member}/VA=test,trial");
			await Assert.That(await Cmd($"@cluster/new {cluster}")).StartsWith("@cluster: a cluster is one object here");
		}

		await Assert.That(await Eval($"[get({cluster}/ZZ)]|[get({cluster}/VA)]")).IsEqualTo("5|This is a trial");
	}
}
