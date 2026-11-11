using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Generated;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Services.DatabaseConversion.TinyMux;

namespace SharpMUSH.Library.Services.DatabaseConversion;

/// <summary>
/// What the converter does for a source other than PennMUSH: which reader a file goes to, the options
/// the source's softcode needs, and the permissions its privileges become.
/// </summary>
public partial class PennMUSHDatabaseConverter
{
	/// <summary>
	/// Reads a TinyMUX flatfile and its companions into the model: the game's mail and channels are its
	/// <c>mail.db</c> (<see cref="DatabaseImportFiles.Mail"/>) and <c>comsys.db</c>
	/// (<see cref="DatabaseImportFiles.Chat"/>). A TinyMUX 2.14 SQLite database holds all three.
	/// </summary>
	private async Task<PennMUSHDatabase> ReadTinyMuxAsync(DatabaseImportFiles files, CancellationToken cancellationToken)
	{
		if (TinyMuxDatabaseReader.IsSqlite(files.Database))
		{
			return await ReadTinyMuxSqliteAsync(files, cancellationToken);
		}

		var databaseFilePath = files.Database;
		var mailDatabaseFilePath = files.Mail;
		var chatDatabaseFilePath = files.Chat;
		_logger.LogInformation("Reading a TinyMUX flatfile: {FilePath}", databaseFilePath);
		var database = await TinyMuxDatabaseReader.ReadFileAsync(databaseFilePath, cancellationToken);
		var version = TinyMuxDatabaseReader.FlatfileVersion(database);

		if (!string.IsNullOrEmpty(mailDatabaseFilePath))
		{
			try
			{
				database.Mail = TinyMuxMailReader.WithColour(
					await TinyMuxMailReader.ReadFileAsync(mailDatabaseFilePath, cancellationToken), version);
			}
			catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
			{
				_logger.LogWarning(ex, "Could not read the TinyMUX mail database: {FilePath}", mailDatabaseFilePath);
				database.Mail = new PennMUSHMailDatabase { ReadError = ex.Message };
			}
		}

		if (!string.IsNullOrEmpty(chatDatabaseFilePath))
		{
			try
			{
				TinyMuxComsysReader.Apply(database,
					await TinyMuxComsysReader.ReadFileAsync(chatDatabaseFilePath, cancellationToken), version);
			}
			catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
			{
				_logger.LogWarning(ex, "Could not read the TinyMUX comsys database: {FilePath}", chatDatabaseFilePath);
				database.Chat = new PennMUSHChatDatabase { ReadError = ex.Message };
			}
		}

		if (!string.IsNullOrEmpty(files.MailFolders))
		{
			database.ImportNotes.Add(new ImportWarning(ImportWarningKind.Mail,
				"The mail folder file that came with the TinyMUX flatfile was not read: " +
				"the folder file is RhostMUSH's, and a TinyMUX import reads only mail.db."));
		}

		return database;
	}

	/// <summary>Reads TinyMUX 2.14's <c>netmux.sqlite</c>, which holds the game, its mail and its comsys; side files are not read.</summary>
	private async Task<PennMUSHDatabase> ReadTinyMuxSqliteAsync(DatabaseImportFiles files, CancellationToken cancellationToken)
	{
		_logger.LogInformation("Reading a TinyMUX SQLite database: {FilePath}", files.Database);
		var database = await TinyMuxDatabaseReader.ReadSqliteFileAsync(files.Database, cancellationToken);
		foreach (var (file, kind) in new[] { (files.Mail, ImportWarningKind.Mail), (files.Chat, ImportWarningKind.Channels), (files.MailFolders, ImportWarningKind.Mail) })
		{
			if (string.IsNullOrEmpty(file)) continue;
			database.ImportNotes.Add(new ImportWarning(kind,
				$"{Path.GetFileName(file)} was not read: the TinyMUX SQLite database holds the game's mail and comsys itself."));
		}

		if (database.Mail.ReadError is { } mailError)
		{
			_logger.LogWarning("Could not read the TinyMUX mail from {FilePath}: {Error}", files.Database, mailError);
		}

		if (database.Chat.ReadError is { } chatError)
		{
			_logger.LogWarning("Could not read the TinyMUX comsys from {FilePath}: {Error}", files.Database, chatError);
		}

		return database;
	}

	/// <summary>
	/// Sets the options the source's softcode needs (<see cref="PennMUSHDatabase.SourceOptions"/>) in the
	/// configuration of the world being written: a <see cref="bool"/> as given, a <see cref="uint"/> raised
	/// to at least its value, an alias table's entries added to the aliases already there. Written through the Mediator, like the objects, so it lands in the same world,
	/// and signalled so a running game rereads its configuration.
	/// </summary>
	/// <remarks>
	/// It runs after the whole world is written, so a failure here is a warning: the conversion stands, and
	/// the options are left for the administrator to set. An option name this server does not have is
	/// reported the same way.
	/// </remarks>
	private async ValueTask EnableSourceOptionsAsync(PennMUSHDatabase source, PennMUSHConversionContext context,
		CancellationToken cancellationToken)
	{
		var names = string.Join(", ", source.SourceOptions.Keys);
		try
		{
			var options = _options.CurrentValue;
			var updated = options;
			var changed = new List<string>();
			foreach (var (name, wanted) in source.SourceOptions)
			{
				if (ConfigOptionWriter.PropertyOf(name) is not { } property)
				{
					context.Warn(ImportWarningKind.Configuration, $"The {source.SourceName} import would set {name}, which this server does not have; " +
						"set its equivalent by hand.");
					continue;
				}

				var current = ConfigAccessor.GetValue(updated, property);
				var value = (wanted, current) switch
				{
					(uint minimum, uint now) => Math.Max(minimum, now),
					// A restriction the source needs replaces the configured one; appended words would land in its message.
					(Dictionary<string, string[]> added, Dictionary<string, string[]> now) when IsRestrictionTable(name) => WithEntries(now, added),
					(Dictionary<string, string[]> added, Dictionary<string, string[]> now) => WithAliases(now, added),
					_ => wanted
				};
				if (Equals(current, value)) continue;

				updated = ConfigAccessor.WithValue(updated, property, value);
				changed.Add(name);
			}

			if (changed.Count == 0) return;

			await _mediator.Send(new SetExpandedServerDataCommand(nameof(SharpMUSHOptions),
				updated with { Database = context.WrittenDatabaseOptions ?? updated.Database }), cancellationToken);
			_configurationReload?.SignalChange();
			_logger.LogInformation("Set {Options} for the imported {Source} softcode", string.Join(", ", changed), source.SourceName);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "Could not set {Options} for the imported {Source} softcode", names, source.SourceName);
			context.WarnNeedsAction(ImportWarningKind.Configuration, $"{names} could not be set ({ex.Message}); the imported {source.SourceName} softcode needs them: " +
				"set them in the configuration and reload it.");
		}
	}

	private static bool IsRestrictionTable(string name)
		=> name.Equals("command_restrictions", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("function_restrictions", StringComparison.OrdinalIgnoreCase);

	/// <summary><paramref name="now"/> with each entry of <paramref name="added"/> in place of its own, or itself when nothing differs.</summary>
	internal static Dictionary<string, string[]> WithEntries(Dictionary<string, string[]> now, Dictionary<string, string[]> added)
	{
		if (added.All(entry => now.TryGetValue(entry.Key, out var words) && words.SequenceEqual(entry.Value)))
		{
			return now;
		}

		var merged = new Dictionary<string, string[]>(now, now.Comparer);
		foreach (var (name, words) in added)
		{
			foreach (var same in merged.Keys.Where(key => key.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList())
			{
				merged.Remove(same);
			}

			merged[name] = words;
		}

		return merged;
	}

	/// <summary>
	/// <paramref name="now"/> with the aliases in <paramref name="added"/> it lacks, or <paramref name="now"/>
	/// itself when it has them all, so an unchanged option is left alone.
	/// </summary>
	private static Dictionary<string, string[]> WithAliases(Dictionary<string, string[]> now,
		Dictionary<string, string[]> added)
	{
		var missing = added
			.Select(entry => (entry.Key, Aliases: entry.Value
				.Where(alias => !(now.GetValueOrDefault(entry.Key) ?? []).Contains(alias, StringComparer.OrdinalIgnoreCase))
				.ToArray()))
			.Where(entry => entry.Aliases.Length > 0)
			.ToList();
		if (missing.Count == 0) return now;

		var merged = new Dictionary<string, string[]>(now, now.Comparer);
		foreach (var (name, aliases) in missing)
		{
			merged[name] = [.. merged.GetValueOrDefault(name) ?? [], .. aliases];
		}

		return merged;
	}

	/// <summary>
	/// Grants each object the permission scopes its source privileges became (<see cref="PennMUSHObject.Permissions"/>),
	/// as allow overrides on the object, and writes the allows and denies its source powers and depowers became
	/// (<see cref="PennMUSHObject.Overrides"/>). A reach naming a role this world lacks reaches <c>player</c> instead,
	/// what the role's holders would rank as without it.
	/// </summary>
	private async Task GrantPermissionsAsync(PennMUSHDatabase source, PennMUSHConversionContext context,
		CancellationToken cancellationToken)
	{
		var holders = source.Objects
			.Where(o => (o.Permissions.Count > 0 || o.Overrides.Count > 0 || o.DeniedPowers.Count > 0) && context.DbrefMapping.ContainsKey(o.DBRef))
			.ToList();
		if (holders.Count == 0) return;

		if (_roleRegistry is null)
		{
			context.WarnNeedsAction(ImportWarningKind.Powers, $"Permissions were not granted to {string.Join(" ", holders.Select(o => $"#{o.DBRef}"))}: " +
				"no role store is available to this import; grant them with @permission.");
			return;
		}

		var roles = (await _roleRegistry.Value.GetRolesAsync(cancellationToken)).Select(role => role.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach (var holder in holders)
		{
			var number = context.DbrefMapping[holder.DBRef].Number;
			foreach (var scope in holder.Permissions.Distinct(StringComparer.Ordinal))
			{
				await _roleRegistry.Value.SetObjectOverrideAsync(number, scope, PermissionState.Allow);
			}

			foreach (var (scope, state, reach) in holder.Overrides)
			{
				var effectiveReach = reach;
				if (reach is not null && !roles.Contains(reach))
				{
					context.NoteUnconverted(ImportWarningKind.Powers, $"Reach {reach} on {scope}, a role this game does not have, so it reaches player",
						holder.DBRef);
					effectiveReach = BuiltInRoles.PlayerSlug;
				}

				await _roleRegistry.Value.SetObjectOverrideAsync(number, scope, state, effectiveReach);
			}
			// Legacy plugin-provided denies run last so a new allow cannot undo a restriction.
			foreach (var name in holder.DeniedPowers)
			{
				if (GamePowers.Find(name) is not { } power)
				{
					context.NoteUnconverted(ImportWarningKind.Powers, $"Depower of {name}, which is not a SharpMUSH power", holder.DBRef);
					continue;
				}

				await _roleRegistry.Value.SetObjectOverrideAsync(number, power.Scope, PermissionState.Deny);
			}

		}

		await _mediator.Send(new InvalidateGrantsCommand(null), cancellationToken);
	}
}
