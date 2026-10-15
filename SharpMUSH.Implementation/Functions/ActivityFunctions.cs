using System.Globalization;
using System.Text.Json;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	private static readonly JsonSerializerOptions ActivityJson = new(JsonSerializerDefaults.Web);

	/// <summary>The option keys <c>activity()</c> reads, as its help lists them.</summary>
	public static readonly IReadOnlyList<string> ActivityOptionKeys =
		["until", "days", "baseline", "minimum", "months", "dead", "zone", "limit"];

	/// <summary>
	/// <c>activity(&lt;report&gt;[, &lt;options&gt;])</c> — the staff activity reports, as JSON: <c>summary</c>,
	/// <c>drifting</c>, <c>cohorts</c>, <c>rooms</c> or <c>hours</c>, over the window a JSON options object
	/// describes. Counted from poses and scenes, never connection time; needs <c>players.view</c>.
	/// </summary>
	[SharpFunction(Name = "activity", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		Restrict = [PortalPermission.PlayersView], ParameterNames = ["report", "options"])]
	public async ValueTask<CallState> Activity(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var report = args["0"].Message!.ToPlainText().Trim().ToLowerInvariant();
		var optionsText = args.TryGetValue("1", out var given) ? given.Message!.ToPlainText().Trim() : string.Empty;

		return ReadActivityOptions(optionsText) switch
		{
			ActivityOptions options => await ActivityReportAsync(report, options),
			Error<string> error => new CallState($"#-1 {error.Value}")
		};
	}

	private async ValueTask<CallState> ActivityReportAsync(string report, ActivityOptions options) => report switch
	{
		"summary" => ActivityResult(await ActivityReports.SummaryAsync(options)),
		"drifting" => ActivityResult(await ActivityReports.DriftingAsync(options)),
		"cohorts" => ActivityResult(await ActivityReports.CohortsAsync(options)),
		"rooms" => ActivityResult(await ActivityReports.RoomsAsync(options)),
		"hours" => ActivityResult(await ActivityReports.HoursAsync(options)),
		_ => new CallState(ErrorMessages.Returns.NoSuchActivityReport)
	};

	private static CallState ActivityResult<T>(Result<T> result) where T : notnull => result switch
	{
		T value => new CallState(JsonSerializer.Serialize(value, ActivityJson)),
		Error<string> error => new CallState($"#-1 {error.Value.ToUpperInvariant().TrimEnd('.')}")
	};

	/// <summary>The options object, or why it cannot be read. Empty text is every default.</summary>
	public static Result<ActivityOptions> ReadActivityOptions(string text)
	{
		var options = new ActivityOptions();
		if (text.Length == 0) return options;

		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(text);
		}
		catch (JsonException)
		{
			return new Error<string>("OPTIONS MUST BE A JSON OBJECT");
		}

		using (document)
		{
			if (document.RootElement.ValueKind != JsonValueKind.Object) return new Error<string>("OPTIONS MUST BE A JSON OBJECT");
			foreach (var property in document.RootElement.EnumerateObject())
			{
				var key = property.Name.ToLowerInvariant();
				var value = property.Value;
				Result<ActivityOptions> next = key switch
				{
					"until" => value.ValueKind == JsonValueKind.String
						&& DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var until)
							? options with { Until = until }
							: new Error<string>("UNTIL MUST BE A DATE LIKE 2026-10-15"),
					"zone" => value.ValueKind == JsonValueKind.String
						? options with { Zone = value.GetString() }
						: new Error<string>("ZONE MUST BE A TIME ZONE NAME"),
					"days" or "baseline" or "minimum" or "months" or "dead" or "limit" => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
						? key switch
						{
							"days" => options with { Days = number },
							"baseline" => options with { Baseline = number },
							"minimum" => options with { MinimumDays = number },
							"months" => options with { Months = number },
							"dead" => options with { DeadDays = number },
							_ => options with { Limit = number }
						}
						: new Error<string>($"{key.ToUpperInvariant()} MUST BE A WHOLE NUMBER"),
					_ => new Error<string>($"UNKNOWN OPTION {key.ToUpperInvariant()}")
				};

				switch (next)
				{
					case ActivityOptions read:
						options = read;
						break;
					case Error<string> error:
						return error;
				}
			}
		}

		return options;
	}
}
