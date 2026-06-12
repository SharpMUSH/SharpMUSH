using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// Unit tests for the pure plan engine (decisions 20.7, 20.15, 20.20):
/// the three-way truth table, delete/rename classification, dependency and
/// conflict checks, and $command collision detection. No database required.
/// </summary>
public class PackagePlanServiceTests
{
	private readonly PackagePlanService _service = new();
	private readonly PackageManifestService _manifests = new();

	private static readonly DateTimeOffset Anchor = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

	private PackageManifest Parse(string yaml) => _manifests.ParseManifest(yaml).AsT0.Manifest;

	private static InstalledPackageRecord Installed(string id, string version = "1.0.0") => new(
		id, version, "https://example.com/repo", $"{id}/", "commit-1", "main", Anchor, 1);

	private static LiveObjectState LiveObject(string objid, string name, params (string Attr, string Value)[] attrs) => new(
		objid, true, name,
		attrs.ToDictionary(a => a.Attr, a => a.Value, StringComparer.OrdinalIgnoreCase));

	private static PackagePlanInputs Inputs(
		PackageManifest manifest,
		InstalledPackageRecord? installed = null,
		IReadOnlyList<PackageObjectRecord>? installedObjects = null,
		IReadOnlyList<ManagedAttributeRecord>? baselines = null,
		IReadOnlyList<InstalledPackageRecord>? allInstalled = null,
		IReadOnlyList<ManagedAttributeRecord>? otherManaged = null,
		LivePackageState? live = null,
		IReadOnlyDictionary<string, string>? configure = null) => new(
		manifest,
		installed,
		installedObjects ?? [],
		baselines ?? [],
		allInstalled ?? (installed is null ? [] : [installed]),
		otherManaged ?? [],
		live ?? LivePackageState.Empty,
		new Dictionary<string, string> { ["room_zero"] = "#0:0" },
		configure ?? new Dictionary<string, string>(),
		new Dictionary<string, string>());

	private const string SimpleManifest =
		"""
		package: probe
		version: "1.1"
		objects:
		  - ref: a
		    type: thing
		    name: Thing A
		    attributes:
		      FN_X: "value-new"
		""";

	#region Fresh install

	[Test]
	public async Task FreshInstall_CreatesEverything()
	{
		var changeset = _service.ComputeChangeset(Inputs(Parse(SimpleManifest)));

		await Assert.That(changeset.Kind).IsEqualTo(PackageRevisionKind.Install);
		await Assert.That(changeset.FromVersion).IsNull();
		await Assert.That(changeset.IsBlocked).IsFalse();
		await Assert.That(changeset.Objects.Single().Action).IsEqualTo(PackageObjectAction.Create);
		var attr = changeset.Attributes.Single();
		await Assert.That(attr.Action).IsEqualTo(PackageAttributeAction.Create);
		await Assert.That(attr.NewValue).IsEqualTo("value-new");
	}

	[Test]
	public async Task FreshInstall_CodeIndirects_RefAttrSynthesized()
	{
		var manifest = Parse(
			"""
			package: probe
			version: "1.0"
			objects:
			  - ref: a
			    type: thing
			    name: A
			    attributes:
			      FN_X: "u({{b}}/FN_Y)"
			  - ref: b
			    type: thing
			    name: B
			""");

		var changeset = _service.ComputeChangeset(Inputs(manifest));

		// Code never carries dbrefs or tokens — it recalls via v(PM`REFS`...).
		var code = changeset.Attributes.Single(a => a.Attribute == "FN_X");
		await Assert.That(code.NewValue).IsEqualTo("u([v(PM`REFS`B)]/FN_Y)");
		await Assert.That(code.RequiresApplyResolution).IsFalse();

		// The engine-managed ref attr carries the (apply-time) resolution.
		var refAttr = changeset.Attributes.Single(a => a.Attribute == "PM`REFS`B");
		await Assert.That(refAttr.Action).IsEqualTo(PackageAttributeAction.Create);
		await Assert.That(refAttr.NewValue).IsEqualTo("{{b}}");
		await Assert.That(refAttr.RequiresApplyResolution).IsTrue();
		await Assert.That(changeset.Notes.Any(n => n.Contains("apply time"))).IsTrue();
	}

	[Test]
	public async Task RepointedRefAttr_SurvivesUpgrade_AsKeepLocal()
	{
		// The point of decision 20.21: a user re-points PM`REFS`B locally; the
		// package's own resolution is unchanged, so the re-point is preserved.
		var manifest = Parse(
			"""
			package: probe
			version: "1.1"
			objects:
			  - ref: a
			    type: thing
			    name: Thing A
			    attributes:
			      FN_X: "u({{b}}/FN_Y)"
			  - ref: b
			    type: thing
			    name: Thing B
			""");
		var installed = Installed("probe");
		var installedObjects = new[]
		{
			new PackageObjectRecord("probe", "a", "#10:1", "thing"),
			new PackageObjectRecord("probe", "b", "#20:5", "thing")
		};
		var baselines = new[]
		{
			new ManagedAttributeRecord("probe", "#10:1", "FN_X", "u([v(PM`REFS`B)]/FN_Y)", "h1", "1.0.0"),
			new ManagedAttributeRecord("probe", "#10:1", "PM`REFS`B", "#20:5", "h2", "1.0.0")
		};
		var live = new LivePackageState(new Dictionary<string, LiveObjectState>
		{
			["#10:1"] = LiveObject("#10:1", "Thing A",
				("FN_X", "u([v(PM`REFS`B)]/FN_Y)"), ("PM`REFS`B", "#99:9")), // user re-pointed!
			["#20:5"] = LiveObject("#20:5", "Thing B")
		});

		var changeset = _service.ComputeChangeset(Inputs(manifest, installed, installedObjects, baselines, live: live));

		var refAttr = changeset.Attributes.Single(a => a.Attribute == "PM`REFS`B");
		await Assert.That(refAttr.Action).IsEqualTo(PackageAttributeAction.KeepLocal);
		await Assert.That(refAttr.LiveValue).IsEqualTo("#99:9");
		await Assert.That(changeset.Attributes.Single(a => a.Attribute == "FN_X").Action)
			.IsEqualTo(PackageAttributeAction.NoChange);
	}

	#endregion

	#region Three-way truth table

	private PackageChangeset UpgradeWith(string? baseValue, string? liveValue, string newValue = "value-new")
	{
		var manifest = Parse(SimpleManifest.Replace("value-new", newValue));
		var installed = Installed("probe");
		var installedObjects = new[] { new PackageObjectRecord("probe", "a", "#10:1", "thing") };
		var baselines = baseValue is null
			? []
			: new[] { new ManagedAttributeRecord("probe", "#10:1", "FN_X", baseValue, "hash", "1.0.0") };
		var liveAttrs = liveValue is null
			? Array.Empty<(string, string)>()
			: [("FN_X", liveValue)];
		var live = new LivePackageState(new Dictionary<string, LiveObjectState>
		{
			["#10:1"] = LiveObject("#10:1", "Thing A", liveAttrs)
		});

		return _service.ComputeChangeset(Inputs(manifest, installed, installedObjects, baselines, live: live));
	}

	[Test]
	public async Task Table_AllUnchanged_NoChange()
	{
		var attr = UpgradeWith("same", "same", "same").Attributes.Single();
		await Assert.That(attr.Action).IsEqualTo(PackageAttributeAction.NoChange);
	}

	[Test]
	public async Task Table_PackageChanged_UserDidNot_AutoUpgrade()
	{
		var attr = UpgradeWith("old", "old").Attributes.Single();
		await Assert.That(attr.Action).IsEqualTo(PackageAttributeAction.AutoUpgrade);
		await Assert.That(attr.BaseValue).IsEqualTo("old");
		await Assert.That(attr.LiveValue).IsEqualTo("old");
		await Assert.That(attr.NewValue).IsEqualTo("value-new");
	}

	[Test]
	public async Task Table_UserChanged_PackageDidNot_KeepLocal()
	{
		var attr = UpgradeWith("value-new", "customized").Attributes.Single();
		await Assert.That(attr.Action).IsEqualTo(PackageAttributeAction.KeepLocal);
	}

	[Test]
	public async Task Table_BothChanged_Conflict_WithAllThreePanes()
	{
		var attr = UpgradeWith("old", "customized").Attributes.Single();
		await Assert.That(attr.Action).IsEqualTo(PackageAttributeAction.Conflict);
		await Assert.That(attr.Conflict).IsEqualTo(PackageConflictKind.ModifyModify);
		await Assert.That(attr.BaseValue).IsEqualTo("old");
		await Assert.That(attr.LiveValue).IsEqualTo("customized");
		await Assert.That(attr.NewValue).IsEqualTo("value-new");
	}

	[Test]
	public async Task Table_BothChangedToSameValue_NoChange()
	{
		var attr = UpgradeWith("old", "value-new").Attributes.Single();
		await Assert.That(attr.Action).IsEqualTo(PackageAttributeAction.NoChange);
	}

	[Test]
	public async Task Table_UserDeleted_PackageUnchanged_KeepLocal()
	{
		var attr = UpgradeWith("value-new", liveValue: null).Attributes.Single();
		await Assert.That(attr.Action).IsEqualTo(PackageAttributeAction.KeepLocal);
		await Assert.That(attr.LiveValue).IsNull();
	}

	[Test]
	public async Task Table_UserDeleted_PackageChanged_DeleteModifyConflict()
	{
		var attr = UpgradeWith("old", liveValue: null).Attributes.Single();
		await Assert.That(attr.Action).IsEqualTo(PackageAttributeAction.Conflict);
		await Assert.That(attr.Conflict).IsEqualTo(PackageConflictKind.DeleteModify);
	}

	[Test]
	public async Task Table_Unmanaged_LiveMatches_Adopt()
	{
		var attr = UpgradeWith(baseValue: null, "value-new").Attributes.Single();
		await Assert.That(attr.Action).IsEqualTo(PackageAttributeAction.Adopt);
	}

	[Test]
	public async Task Table_Unmanaged_LiveDiffers_AddAddConflict()
	{
		var attr = UpgradeWith(baseValue: null, "someone elses code").Attributes.Single();
		await Assert.That(attr.Action).IsEqualTo(PackageAttributeAction.Conflict);
		await Assert.That(attr.Conflict).IsEqualTo(PackageConflictKind.AddAdd);
	}

	[Test]
	public async Task Table_Unmanaged_LiveAbsent_Create()
	{
		var attr = UpgradeWith(baseValue: null, liveValue: null).Attributes.Single();
		await Assert.That(attr.Action).IsEqualTo(PackageAttributeAction.Create);
	}

	#endregion

	#region Attribute deletions (attr removed from new version)

	private PackageChangeset DroppedAttrChangeset(string? liveValue)
	{
		// New version no longer carries FN_GONE.
		var manifest = Parse(SimpleManifest);
		var installed = Installed("probe");
		var installedObjects = new[] { new PackageObjectRecord("probe", "a", "#10:1", "thing") };
		var baselines = new[]
		{
			new ManagedAttributeRecord("probe", "#10:1", "FN_X", "value-new", "h1", "1.0.0"),
			new ManagedAttributeRecord("probe", "#10:1", "FN_GONE", "base-gone", "h2", "1.0.0")
		};
		var liveAttrs = new List<(string, string)> { ("FN_X", "value-new") };
		if (liveValue is not null)
		{
			liveAttrs.Add(("FN_GONE", liveValue));
		}

		var live = new LivePackageState(new Dictionary<string, LiveObjectState>
		{
			["#10:1"] = LiveObject("#10:1", "Thing A", liveAttrs.ToArray())
		});

		return _service.ComputeChangeset(Inputs(manifest, installed, installedObjects, baselines, live: live));
	}

	[Test]
	public async Task Dropped_Unmodified_Delete()
	{
		var attr = DroppedAttrChangeset("base-gone").Attributes.Single(a => a.Attribute == "FN_GONE");
		await Assert.That(attr.Action).IsEqualTo(PackageAttributeAction.Delete);
	}

	[Test]
	public async Task Dropped_LocallyModified_ModifyDeleteConflict()
	{
		var attr = DroppedAttrChangeset("customized").Attributes.Single(a => a.Attribute == "FN_GONE");
		await Assert.That(attr.Action).IsEqualTo(PackageAttributeAction.Conflict);
		await Assert.That(attr.Conflict).IsEqualTo(PackageConflictKind.ModifyDelete);
		await Assert.That(attr.LiveValue).IsEqualTo("customized");
	}

	[Test]
	public async Task Dropped_AlreadyGoneLive_RemoveBaseline()
	{
		var attr = DroppedAttrChangeset(null).Attributes.Single(a => a.Attribute == "FN_GONE");
		await Assert.That(attr.Action).IsEqualTo(PackageAttributeAction.RemoveBaseline);
	}

	#endregion

	#region Objects: rename, delete, recreate, metadata

	[Test]
	public async Task Rename_KeepsDbref_NeverDestroyCreate()
	{
		var manifest = Parse(
			"""
			package: probe
			version: "2.0"
			objects:
			  - ref: bbs_core
			    type: thing
			    name: BBS Core
			    previous_refs: [bbs_parent]
			    attributes:
			      FN_X: "kept"
			""");
		var installed = Installed("probe");
		var installedObjects = new[] { new PackageObjectRecord("probe", "bbs_parent", "#20:5", "thing") };
		var baselines = new[] { new ManagedAttributeRecord("probe", "#20:5", "FN_X", "kept", "h", "1.0.0") };
		var live = new LivePackageState(new Dictionary<string, LiveObjectState>
		{
			["#20:5"] = LiveObject("#20:5", "BBS Core", ("FN_X", "kept"))
		});

		var changeset = _service.ComputeChangeset(Inputs(manifest, installed, installedObjects, baselines, live: live));

		var obj = changeset.Objects.Single();
		await Assert.That(obj.Action).IsEqualTo(PackageObjectAction.Rename);
		await Assert.That(obj.Objid).IsEqualTo("#20:5");
		await Assert.That(obj.RenamedFromRef).IsEqualTo("bbs_parent");
		// Baselines keyed by objid still line up across the rename.
		await Assert.That(changeset.Attributes.Single().Action).IsEqualTo(PackageAttributeAction.NoChange);
	}

	[Test]
	public async Task RemovedObject_Delete_WithContentsWarning()
	{
		var manifest = Parse(SimpleManifest);
		var installed = Installed("probe");
		var installedObjects = new[]
		{
			new PackageObjectRecord("probe", "a", "#10:1", "thing"),
			new PackageObjectRecord("probe", "old_room", "#11:1", "room")
		};
		var live = new LivePackageState(new Dictionary<string, LiveObjectState>
		{
			["#10:1"] = LiveObject("#10:1", "Thing A", ("FN_X", "value-new")),
			["#11:1"] = LiveObject("#11:1", "Old Room") with { HasContents = true }
		});

		var changeset = _service.ComputeChangeset(Inputs(manifest, installed, installedObjects, live: live));

		var deleted = changeset.Objects.Single(o => o.Ref == "old_room");
		await Assert.That(deleted.Action).IsEqualTo(PackageObjectAction.Delete);
		await Assert.That(deleted.Objid).IsEqualTo("#11:1");
		await Assert.That(changeset.Notes.Any(n => n.Contains("contains objects"))).IsTrue();
	}

	[Test]
	public async Task DestroyedOutOfBand_RecreateMissing()
	{
		var manifest = Parse(SimpleManifest);
		var installed = Installed("probe");
		var installedObjects = new[] { new PackageObjectRecord("probe", "a", "#10:1", "thing") };

		var changeset = _service.ComputeChangeset(Inputs(manifest, installed, installedObjects));

		await Assert.That(changeset.Objects.Single().Action).IsEqualTo(PackageObjectAction.RecreateMissing);
		await Assert.That(changeset.Notes.Any(n => n.Contains("destroyed outside"))).IsTrue();
	}

	[Test]
	public async Task NameDrift_UpdateMetadata()
	{
		var manifest = Parse(SimpleManifest);
		var installed = Installed("probe");
		var installedObjects = new[] { new PackageObjectRecord("probe", "a", "#10:1", "thing") };
		var live = new LivePackageState(new Dictionary<string, LiveObjectState>
		{
			["#10:1"] = LiveObject("#10:1", "Renamed By Hand", ("FN_X", "value-new"))
		});

		var changeset = _service.ComputeChangeset(Inputs(manifest, installed, installedObjects, live: live));

		var obj = changeset.Objects.Single();
		await Assert.That(obj.Action).IsEqualTo(PackageObjectAction.UpdateMetadata);
		await Assert.That(obj.MetadataDiffs!.Single()).Contains("Renamed By Hand");
	}

	#endregion

	#region Dependencies & conflicts

	[Test]
	public async Task MissingDependency_Blocks_AndCarriesSourceHint()
	{
		var manifest = Parse(
			"""
			package: probe
			version: "1.0"
			depends:
			  - package: who-where
			    version: ">=1.0"
			    source:
			      repo: https://github.com/SharpMUSH/SharpMUSH-Packages
			      path: who-where/
			objects:
			  - ref: a
			    type: thing
			    name: A
			""");

		var changeset = _service.ComputeChangeset(Inputs(manifest));

		await Assert.That(changeset.IsBlocked).IsTrue();
		var issue = changeset.DependencyIssues.Single();
		await Assert.That(issue.PackageId).IsEqualTo("who-where");
		await Assert.That(issue.InstalledVersion).IsNull();
		await Assert.That(issue.Source!.Repo).Contains("SharpMUSH-Packages");
	}

	[Test]
	public async Task VersionMismatch_Blocks_SatisfiedDependency_DoesNot()
	{
		var manifest = Parse(
			"""
			package: probe
			version: "1.0"
			depends:
			  - who-where: ">=2.0"
			objects:
			  - ref: a
			    type: thing
			    name: A
			""");

		var tooOld = _service.ComputeChangeset(Inputs(manifest, allInstalled: [Installed("who-where", "1.2.0")]));
		await Assert.That(tooOld.IsBlocked).IsTrue();
		await Assert.That(tooOld.DependencyIssues.Single().InstalledVersion).IsEqualTo("1.2.0");

		var satisfied = _service.ComputeChangeset(Inputs(manifest, allInstalled: [Installed("who-where", "2.1.0")]));
		await Assert.That(satisfied.IsBlocked).IsFalse();
	}

	[Test]
	public async Task InstalledConflict_Blocks_EvenForPrereleaseVersions()
	{
		var manifest = Parse(
			"""
			package: probe
			version: "1.0"
			conflicts:
			  - legacy-bbs
			objects:
			  - ref: a
			    type: thing
			    name: A
			""");

		var changeset = _service.ComputeChangeset(Inputs(
			manifest, allInstalled: [Installed("legacy-bbs", "0.9.0-beta")]));

		await Assert.That(changeset.IsBlocked).IsTrue();
		await Assert.That(changeset.DependencyIssues.Single().IsConflict).IsTrue();
	}

	#endregion

	#region $command collisions

	[Test]
	public async Task CommandPatternCollision_Detected_AcrossPackages()
	{
		var manifest = Parse(
			"""
			package: probe
			version: "1.0"
			objects:
			  - ref: a
			    type: thing
			    name: A
			    attributes:
			      CMD_+WHO: "$+who:@pemit %#=mine"
			      CMD_OTHER: "$+unique *:@pemit %#=fine"
			      FN_PLAIN: "not a command"
			""");
		var otherManaged = new[]
		{
			new ManagedAttributeRecord("who-where", "#30:1", "CMD_+WHO", "$+WHO :@pemit %#=theirs", "h", "1.2.0")
		};

		var changeset = _service.ComputeChangeset(Inputs(manifest, otherManaged: otherManaged));

		var collision = changeset.CommandCollisions.Single();
		await Assert.That(collision.Pattern).IsEqualTo("+who");
		await Assert.That(collision.OtherPackageId).IsEqualTo("who-where");
		await Assert.That(collision.Attribute).IsEqualTo("CMD_+WHO");
	}

	[Test]
	public async Task CommandPatternExtraction_NormalizesCaseAndWhitespace()
	{
		await Assert.That(PackagePlanService.ExtractCommandPattern("$+BBread   *:@pemit %#=x")).IsEqualTo("+bbread *");
		await Assert.That(PackagePlanService.ExtractCommandPattern("plain value")).IsNull();
		await Assert.That(PackagePlanService.ExtractCommandPattern("$broken-no-colon")).IsNull();
	}

	#endregion

	#region Configure resolution

	[Test]
	public async Task ConfigureAnswers_ResolveIntoComparedValues()
	{
		var manifest = Parse(
			"""
			package: probe
			version: "1.1"
			configure:
			  storage:
			    label: "Storage"
			objects:
			  - ref: a
			    type: thing
			    name: Thing A
			    attributes:
			      FN_X: "get({{?storage}}/DATA)"
			""");
		var installed = Installed("probe");
		var installedObjects = new[] { new PackageObjectRecord("probe", "a", "#10:1", "thing") };
		var baselines = new[]
		{
			new ManagedAttributeRecord("probe", "#10:1", "FN_X", "get([v(PM`REFS`STORAGE)]/DATA)", "h", "1.0.0"),
			new ManagedAttributeRecord("probe", "#10:1", "PM`REFS`STORAGE", "#42:9", "h2", "1.0.0")
		};
		var live = new LivePackageState(new Dictionary<string, LiveObjectState>
		{
			["#10:1"] = LiveObject("#10:1", "Thing A",
				("FN_X", "get([v(PM`REFS`STORAGE)]/DATA)"), ("PM`REFS`STORAGE", "#42:9"))
		});

		// Answer present: code and ref attr both match their baselines → NoChange.
		var answered = _service.ComputeChangeset(Inputs(
			manifest, installed, installedObjects, baselines, live: live,
			configure: new Dictionary<string, string> { ["storage"] = "#42:9" }));
		await Assert.That(answered.Attributes.All(a => a.Action == PackageAttributeAction.NoChange)).IsTrue();

		// No answer yet: only the ref ATTR awaits resolution; code is total.
		var unanswered = _service.ComputeChangeset(Inputs(
			manifest, installed, installedObjects, baselines, live: live));
		await Assert.That(unanswered.Attributes.Single(a => a.Attribute == "FN_X").RequiresApplyResolution).IsFalse();
		await Assert.That(unanswered.Attributes.Single(a => a.Attribute == "PM`REFS`STORAGE").RequiresApplyResolution).IsTrue();
	}

	#endregion
}
