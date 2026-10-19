using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Tools.WikiSemantics.Native;

namespace SharpMUSH.Tests.Wiki.Semantics;

public class NativeSemanticPlanOverlayTests
{
	[Test]
	public async Task PendingCounterPutIsVisibleBeforeApply()
	{
		using var fixture = new NativeSemanticFixture();
		var tables = new NativeTables(fixture);
		var planned = fixture.Read(tx =>
		{
			using var work = WikiSemanticWork.Create(tx, tables.Projection, NativeSemanticFixture.Budget());
			var key = work.Key(2);
			work.Delta.Put(tables.Metadata, key, work.Number(7));
			return work.Counter(key);
		});
		var committed = fixture.Read(tx =>
		{
			using var work = WikiSemanticWork.Create(tx, tables.Projection, NativeSemanticFixture.Budget());
			return work.Counter(work.Key(2));
		});

		await Assert.That(committed).IsEqualTo(0UL);
		await Assert.That(planned).IsEqualTo(7UL);
	}

	[Test]
	public async Task PendingCounterDeleteMasksCommittedValue()
	{
		using var fixture = new NativeSemanticFixture();
		var tables = new NativeTables(fixture);
		await fixture.WriteAsync(tx =>
		{
			using var work = WikiSemanticWork.Create(tx, tables.Projection, NativeSemanticFixture.Budget());
			tx.Put(tables.Metadata, work.Key(2), work.Number(7));
			return true;
		});
		var observed = fixture.Read(tx =>
		{
			using var work = WikiSemanticWork.Create(tx, tables.Projection, NativeSemanticFixture.Budget());
			var key = work.Key(2);
			var absentPlan = work.Counter(key);
			work.Delta.Delete(tables.Metadata, key);
			return (AbsentPlan: absentPlan, DeletedPlan: work.Counter(key, 13));
		});
		var committed = fixture.Read(tx =>
		{
			using var work = WikiSemanticWork.Create(tx, tables.Projection, NativeSemanticFixture.Budget());
			return work.Counter(work.Key(2));
		});

		await Assert.That(observed.AbsentPlan).IsEqualTo(7UL);
		await Assert.That(committed).IsEqualTo(7UL);
		await Assert.That(observed.DeletedPlan).IsEqualTo(13UL);
	}

	[Test]
	public async Task TwoLogicalCounterIncrementsShareOneUnappliedDelta()
	{
		using var fixture = new NativeSemanticFixture();
		var tables = new NativeTables(fixture);
		var observed = fixture.Read(tx =>
		{
			using var work = WikiSemanticWork.Create(tx, tables.Projection, NativeSemanticFixture.Budget());
			var key = work.Key(2);
			var first = checked(work.Counter(key) + 1);
			work.Delta.Put(tables.Metadata, key, work.Number(first));
			var second = checked(work.Counter(key) + 1);
			work.Delta.Put(tables.Metadata, key, work.Number(second));
			return (First: first, Second: second, Final: work.Counter(key), Bytes: work.Delta.Bytes);
		});
		var committed = fixture.Read(tx =>
		{
			using var work = WikiSemanticWork.Create(tx, tables.Projection, NativeSemanticFixture.Budget());
			return work.Counter(work.Key(2));
		});

		await Assert.That(committed).IsEqualTo(0UL);
		await Assert.That(observed.Bytes).IsEqualTo(9L);
		await Assert.That(observed.First).IsEqualTo(1UL);
		await Assert.That(observed.Second).IsEqualTo(2UL);
		await Assert.That(observed.Final).IsEqualTo(2UL);
	}

	[Test]
	public async Task TypedRegistryReadSeesPendingPut()
	{
		using var fixture = new NativeSemanticFixture();
		var tables = new NativeTables(fixture);
		var plannedEntry = new WikiSemanticRegistryEntry(7, "pending");
		var observed = fixture.Read(tx =>
		{
			using var work = WikiSemanticWork.Create(tx, tables.Projection, NativeSemanticFixture.Budget());
			var key = work.Lookup(WikiSemanticRegistryKind.Page, plannedEntry.Identity);
			work.Delta.Put(tables.Registry, key, NativeStateCodec.Registry(plannedEntry));
			return work.Read(tables.Registry, key, NativeStateCodec.Registry);
		});

		await Assert.That(observed).IsEqualTo(plannedEntry);
	}

	[Test]
	public async Task TypedRegistryReadDistinguishesPendingDeleteFromAbsentPlan()
	{
		using var fixture = new NativeSemanticFixture();
		var tables = new NativeTables(fixture);
		var committedEntry = new WikiSemanticRegistryEntry(7, "committed");
		await fixture.WriteAsync(tx =>
		{
			using var work = WikiSemanticWork.Create(tx, tables.Projection, NativeSemanticFixture.Budget());
			tx.Put(tables.Registry, work.Lookup(WikiSemanticRegistryKind.Page, committedEntry.Identity), NativeStateCodec.Registry(committedEntry));
			return true;
		});
		var observed = fixture.Read(tx =>
		{
			using var work = WikiSemanticWork.Create(tx, tables.Projection, NativeSemanticFixture.Budget());
			var key = work.Lookup(WikiSemanticRegistryKind.Page, committedEntry.Identity);
			var absentPlan = work.Read(tables.Registry, key, NativeStateCodec.Registry);
			work.Delta.Delete(tables.Registry, key);
			return (AbsentPlan: absentPlan, DeletedPlan: work.Read(tables.Registry, key, NativeStateCodec.Registry));
		});

		await Assert.That(observed.AbsentPlan).IsEqualTo(committedEntry);
		await Assert.That(observed.DeletedPlan).IsNull();
	}
}
