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
	/// Reads a source file and its companions into the model. The format is told from the file's first
	/// line: TinyMUX starts its flatfile <c>+X</c>; anything else goes to the PennMUSH parser. A TinyMUX
	/// game's mail and channels are its <c>mail.db</c> and <c>comsys.db</c>.
	/// </summary>
	private async Task<PennMUSHDatabase> ReadSourceAsync(string databaseFilePath, string? mailDatabaseFilePath,
		string? chatDatabaseFilePath, CancellationToken cancellationToken)
	{
		if (TinyMuxDatabaseReader.IsTinyMuxFlatfile(databaseFilePath))
		{
			return await ReadTinyMuxAsync(databaseFilePath, mailDatabaseFilePath, chatDatabaseFilePath, cancellationToken);
		}

		return await ReadPennMUSHAsync(databaseFilePath, mailDatabaseFilePath, chatDatabaseFilePath, cancellationToken);
	}

	private async Task<PennMUSHDatabase> ReadTinyMuxAsync(string databaseFilePath, string? mailDatabaseFilePath,
		string? chatDatabaseFilePath, CancellationToken cancellationToken)
	{
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

		return database;
	}

	/// <summary>
	/// Sets the options the source's softcode needs (<see cref="PennMUSHDatabase.SourceOptions"/>) in the
	/// configuration of the world being written: a <see cref="bool"/> as given, a <see cref="uint"/> raised
	/// to at least its value. Written through the Mediator, like the objects, so it lands in the same world,
	/// and signalled so a running game rereads its configuration.
	/// </summary>
	/// <remarks>
	/// It runs after the whole world is written, so a failure here is a warning: the conversion stands, and
	/// the options are left for the administrator to set. An option this server does not have (a rename on
	/// another branch) is reported the same way.
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
					context.Warnings.Add($"The {source.SourceName} import would set {name}, which this server does not have; " +
						"set its equivalent by hand.");
					continue;
				}

				var current = ConfigAccessor.GetValue(updated, property);
				var value = (wanted, current) switch
				{
					(uint minimum, uint now) => Math.Max(minimum, now),
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
			context.Warnings.Add($"{names} could not be set ({ex.Message}); the imported {source.SourceName} softcode needs them " +
				"(PennMUSH's writes literal parentheses unescaped and reads @http's %q<status>): set them in the configuration " +
				"and reload it.");
		}
	}

	/// <summary>
	/// Grants each object the permission scopes its source privileges became
	/// (<see cref="PennMUSHObject.Permissions"/>), as allow overrides on the object.
	/// </summary>
	private async Task GrantPermissionsAsync(PennMUSHDatabase source, PennMUSHConversionContext context,
		CancellationToken cancellationToken)
	{
		var holders = source.Objects
			.Where(o => o.Permissions.Count > 0 && context.DbrefMapping.ContainsKey(o.DBRef))
			.ToList();
		if (holders.Count == 0) return;

		if (_roles is null)
		{
			context.Warnings.Add($"Permissions were not granted to {string.Join(" ", holders.Select(o => $"#{o.DBRef}"))}: " +
				"no role store is available to this import; grant them with @role.");
			return;
		}

		foreach (var holder in holders)
		{
			foreach (var scope in holder.Permissions.Distinct(StringComparer.Ordinal))
			{
				await _roles.SetObjectOverrideAsync(context.DbrefMapping[holder.DBRef].Number, scope, PermissionState.Allow);
			}
		}

		await _mediator.Send(new InvalidateGrantsCommand(null), cancellationToken);
	}
}
