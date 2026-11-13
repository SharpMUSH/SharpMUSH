using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

/// <summary>TinyMUX's <c>@query</c>, run as SharpMUSH's <c>@mapsql</c> on the game's one SQL connection.</summary>
public partial class Commands
{
	private const string MapSqlCommand = "@MAPSQL";

	/// <summary>
	/// <c>@query/sql &lt;object&gt;/&lt;attribute&gt;=&lt;database&gt;/&lt;query&gt;</c>: the query's rows each trigger
	/// the attribute, as <c>@mapsql</c> does. SharpMUSH has one SQL connection, so the database name is not used.
	/// </summary>
	[SharpCommand(Name = "@DIALECT_QUERY", Switches = ["SQL"], Behavior = CB.Default | CB.EqSplit | CB.RSNoParse,
		MinArgs = 0, MaxArgs = 2, ParameterNames = ["object/attribute", "database/query"])]
	public async ValueTask<Option<CallState>> DialectQuery(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.ArgumentsOrdered;
		var target = args.GetValueOrDefault("0")?.Message ?? MarkupText.Empty;
		var right = args.GetValueOrDefault("1")?.Message ?? MarkupText.Empty;
		// Text keeps the carrier characters the plain text leaves out, so the index slices the markup.
		var slash = right.Text.IndexOf('/');
		if (!parser.CurrentState.Switches.Contains("SQL") || target.ToPlainText().Trim().Length == 0
			|| slash < 0 || slash == right.Length - 1)
		{
			const string usage = "Give an object/attribute and a database/query: @query/sql <object>/<attribute>=<database>/<query>.";
			await NotifyService.Notify(executor, usage);
			return new CallState($"#-1 {usage}");
		}

		// @mapsql's lock, which the dispatcher checks only for a command it runs itself.
		if (!await CommandPermission.Value.CanRunAsync(executor, MapSqlCommand))
			return await NotifyService.PermissionDeniedNeeds(executor,
				CommandRestrictions.PermissionsOf(FindCommand(MapSqlCommand)?.LibraryInformation.Attribute.CommandLock ?? string.Empty));

		// The left side is already evaluated and @mapsql reads it as text; the query is evaluated once, by @mapsql.
		var query = right.Substring(slash + 1, right.Length - slash - 1);
		var depth = parser.CurrentState.ArgumentsOrdered.GetValueOrDefault("1")?.Depth ?? 0;
		return await parser.With(state => state with
		{
			ArgumentList = ArgumentList.Of(new CallState(target, depth),
				new CallState(query, depth, null, async () => (await parser.FunctionParse(query))?.Message)),
			Switches = []
		}, async inner => await MapSql(inner, _2));
	}
}
