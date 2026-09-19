using Mediator;
using SharpMUSH.Library.Commands.Database;
using System.Text.Json;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Models.Portal.Widgets;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;


namespace SharpMUSH.Library.Services;

/// <summary>Object creation, wiring, and attribute writes.</summary>
public partial class PackageInstallService
{
	private async Task<Result<string>> CreateObjectAsync(
		PackageWriteTransaction writes,
		PackageObjectSpec spec,
		SharpPlayer pmWizard,
		Func<PackageRef, string?> resolve,
		List<string> notes,
		CancellationToken cancellationToken)
	{
		// Through the Mediator, not straight at the store: the create commands are what carry the
		// cache policy, and the one that matters here is the destination's ContentsTag. A package
		// whose object lands in the master room and is created behind the cache's back is inert —
		// command matching reads a contents list that predates it — until the entry expires or the
		// game restarts, which is exactly the "+wiki does nothing after install" report.
		//
		// ApplyDefaultFlags stays off: the manifest is the whole truth about a package object's
		// flags, and the stock thing_flags is no_command.
		ICommand<DBRef> create;
		switch (spec.Type)
		{
			case PackageObjectType.Room:
				create = new CreateRoomCommand(PrimaryName(spec.Name), pmWizard, ApplyDefaultFlags: false);
				break;
			case PackageObjectType.Thing:
				{
					var location = await ResolveContainerAsync(spec.Location, resolve, cancellationToken) ?? ToContainer(pmWizard);
					create = new CreateThingCommand(PrimaryName(spec.Name), location, pmWizard, location, ApplyDefaultFlags: false);
					break;
				}
			case PackageObjectType.Exit:
				{
					if (await ResolveContainerAsync(spec.Location, resolve, cancellationToken) is not AnySharpContainer location)
					{
						return new Error<string>(ExitSourceUnresolved(spec.Ref));
					}

					var parts = spec.Name.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
					create = new CreateExitCommand(parts[0], parts.Skip(1).ToArray(), location, pmWizard, ApplyDefaultFlags: false);
					break;
				}
			default:
				return new Error<string>($"Object type '{spec.Type}' is not supported by the apply engine.");
		}

		if (await writes.CreateAsync(create, cancellationToken) is not AnySharpObject created)
		{
			return new Error<string>($"Internal error: object {{{{{spec.Ref}}}}} vanished during apply.");
		}

		var objid = created.Object().DBRef.ToString();
		notes.Add($"Created {spec.Type.ToString().ToLowerInvariant()} {{{{{spec.Ref}}}}} as {objid}.");
		return objid;
	}

	private async Task<string?> ApplyObjectWiringAsync(
		PackageWriteTransaction writes,
		PackageObjectSpec spec,
		string objid,
		bool isNew,
		PackageChangeset changeset,
		Func<PackageRef, string?> resolve,
		CancellationToken cancellationToken)
	{
		var node = await GetKnownAsync(objid, cancellationToken);
		if (node is null)
		{
			return $"Internal error: object {{{{{spec.Ref}}}}} ({objid}) vanished during apply.";
		}

		// Exit destination.
		if (spec.Type == PackageObjectType.Exit && isNew)
		{
			var destination = await ResolveContainerAsync(spec.Destination, resolve, cancellationToken);
			if (destination is null)
			{
				return ExitDestinationUnresolved(spec.Ref);
			}

			if (node is not SharpExit exit)
			{
				return $"Internal error: exit {{{{{spec.Ref}}}}} ({objid}) is not an exit.";
			}

			await writes.LinkCreatedExitAsync(exit, destination, cancellationToken);
		}

		// Name updates for metadata drift. PrimaryName, as the create path uses: a manifest name
		// carries its aliases ("Out;out;o"), and nothing on the write path splits them, so passing
		// the whole string here would rename an object the install itself created as "Out" to
		// "Out;out;o" on the next run.
		if (changeset.Objects.Any(c => c.Ref == spec.Ref && c.Action == PackageObjectAction.UpdateMetadata))
		{
			await writes.SetNameAsync(node, PrimaryName(spec.Name), cancellationToken);
		}

		// Parent.
		if (spec.Parent is not null)
		{
			var parentObjid = resolve(spec.Parent);
			var parentNode = parentObjid is null ? null : await GetKnownAsync(parentObjid, cancellationToken);
			if (parentNode is null)
			{
				return ParentUnresolved(spec.Ref, spec.Parent);
			}

			await writes.SetParentAsync(node, parentNode, cancellationToken);
		}

		// Object flags, powers, locks, and attribute flags are applied in the
		// dedicated structure pass (three-way merge), not here.
		return null;
	}

	private async Task<string?> ApplyAttributeChangeAsync(
		PackageWriteTransaction writes,
		PackageManifest manifest,
		PackageAttributeChange change,
		string objid,
		string? newValue,
		Dictionary<string, PackageConflictDecision> decisions,
		SharpPlayer pmWizard,
		List<PackageRevisionSnapshotAttribute> preApply,
		Dictionary<(string Objid, string Attribute), string> finalValues,
		List<string> notes,
		CancellationToken cancellationToken)
	{
		if (HelperFunctions.ParseDbRef(objid) is not DBRef target)
		{
			return $"Internal error: invalid objid '{objid}'.";
		}

		var path = change.Attribute.Split('`');

		async Task WriteAsync(string value)
		{
			if (change.LiveValue is not null && change.PreviousAttribute is null)
			{
				preApply.Add(new PackageRevisionSnapshotAttribute(objid, change.Attribute, change.LiveValue));
			}

			await writes.SetAttributeAsync(target, path, MarkupText.Plain(value), pmWizard, cancellationToken);
		}

		async Task BaselineAsync(string packageValue, string? effectiveValue)
		{
			await writes.UpsertManagedAttributeAsync(new ManagedAttributeRecord(
				manifest.Name, objid, change.Attribute.ToUpperInvariant(),
				packageValue, ContentHash.Sha256Hex(packageValue), manifest.Version.ToString()));
			if (effectiveValue is not null)
			{
				// Null = the attribute does not exist live (a preserved local
				// deletion); it must not enter the rollback snapshot.
				finalValues[(objid, change.Attribute.ToUpperInvariant())] = effectiveValue;
			}
		}

		switch (change.Action)
		{
			case PackageAttributeAction.Create:
			case PackageAttributeAction.AutoUpgrade:
				await WriteAsync(newValue!);
				await BaselineAsync(newValue!, newValue!);
				return null;

			case PackageAttributeAction.NoChange:
			case PackageAttributeAction.Adopt:
			case PackageAttributeAction.KeepLocal:
				if (change.PreviousAttribute is not null && change.LiveValue is not null)
				{
					await WriteAsync(change.LiveValue);
				}

				// The baseline still advances to the package's value
				// (dpkg semantics: local drift stays visible, no re-prompting).
				// A preserved local deletion (LiveValue null) stays deleted.
				await BaselineAsync(newValue!, change.LiveValue ?? (change.Action == PackageAttributeAction.KeepLocal ? null : newValue));
				return null;

			case PackageAttributeAction.Delete:
				preApply.Add(new PackageRevisionSnapshotAttribute(objid, change.Attribute, change.LiveValue!));
				await writes.ClearAttributeAsync(target, path, cancellationToken);
				await writes.RemoveManagedAttributeAsync(manifest.Name, objid, change.Attribute.ToUpperInvariant());
				return null;

			case PackageAttributeAction.RemoveBaseline:
				await writes.RemoveManagedAttributeAsync(manifest.Name, objid, change.Attribute.ToUpperInvariant());
				return null;

			case PackageAttributeAction.Conflict:
				{
					var decision = decisions[DecisionKey(change.TargetRef, change.Attribute)];
					switch (decision.Resolution)
					{
						case PackageConflictResolution.TakeTheirs when change.Conflict == PackageConflictKind.ModifyDelete:
							// "Theirs" is the deletion.
							preApply.Add(new PackageRevisionSnapshotAttribute(objid, change.Attribute, change.LiveValue!));
							await writes.ClearAttributeAsync(target, path, cancellationToken);
							await writes.RemoveManagedAttributeAsync(manifest.Name, objid, change.Attribute.ToUpperInvariant());
							return null;
						case PackageConflictResolution.TakeTheirs:
							await WriteAsync(newValue!);
							await BaselineAsync(newValue!, newValue!);
							return null;
						case PackageConflictResolution.UseCustom when decision.CustomValue is not null:
							await WriteAsync(decision.CustomValue);
							await BaselineAsync(newValue ?? decision.CustomValue, decision.CustomValue);
							return null;
						case PackageConflictResolution.UseCustom:
							return CustomValueMissing($"Conflict {change.TargetRef}/{change.Attribute}");
						default: // KeepMine
							if (change.Conflict == PackageConflictKind.ModifyDelete)
							{
								// Keep the local value; the package no longer manages it.
								await writes.RemoveManagedAttributeAsync(manifest.Name, objid, change.Attribute.ToUpperInvariant());
								notes.Add($"{change.TargetRef}/{change.Attribute}: kept local value; no longer package-managed.");
								return null;
							}

							if (change.PreviousAttribute is not null && change.LiveValue is not null)
							{
								await WriteAsync(change.LiveValue);
							}

							// DeleteModify + KeepMine keeps the deletion: baseline advances, nothing live.
							await BaselineAsync(newValue!, change.LiveValue);
							return null;
					}
				}

			default:
				return $"Internal error: unhandled attribute action {change.Action}.";
		}
	}
}
