using OpenTelemetry;
using OpenTelemetry.Metrics;
using System.Text;

namespace SharpMUSH.Tests;

/// <summary>
/// An OpenTelemetry metrics exporter that writes collected metrics to a markdown file.
/// Register via <see cref="MeterProviderBuilderExtensions"/> using
/// <c>ConfigureOpenTelemetryMeterProvider(m => m.AddReader(new PeriodicExportingMetricReader(new FileMetricExporter(path), int.MaxValue)))</c>.
/// Trigger export by calling <c>MeterProvider.ForceFlush()</c> at the end of the test session.
/// </summary>
internal sealed class FileMetricExporter(string filePath) : BaseExporter<Metric>
{
	public override ExportResult Export(in Batch<Metric> batch)
	{
		var sb = new StringBuilder();
		sb.AppendLine("## 📊 Test Telemetry Summary");
		sb.AppendLine();

		foreach (var metric in batch)
		{
			sb.AppendLine($"### {metric.Name}");
			if (!string.IsNullOrEmpty(metric.Description))
				sb.AppendLine($"_{metric.Description}_");
			sb.AppendLine();

			switch (metric.MetricType)
			{
				case MetricType.Histogram:
					AppendHistogramTable(sb, metric);
					break;
				case MetricType.LongSum:
				case MetricType.LongSumNonMonotonic:
					AppendLongSumTable(sb, metric);
					break;
				case MetricType.LongGauge:
					AppendLongGaugeTable(sb, metric);
					break;
				default:
					sb.AppendLine($"_Metric type `{metric.MetricType}` not rendered._");
					sb.AppendLine();
					break;
			}
		}

		try
		{
			// Overwrite intentional: metrics reader uses cumulative temporality, so each
			// ForceFlush() contains the complete session snapshot.
			File.WriteAllText(filePath, sb.ToString());
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[FileMetricExporter] Failed to write telemetry to '{filePath}': {ex.Message}");
			return ExportResult.Failure;
		}

		return ExportResult.Success;
	}

	private static void AppendHistogramTable(StringBuilder sb, Metric metric)
	{
		sb.AppendLine($"| Tags | Count | Sum ({metric.Unit}) | Min | Max |");
		sb.AppendLine("|------|-------|-----|-----|-----|");

		foreach (ref readonly var point in metric.GetMetricPoints())
		{
			var tags = FormatTags(point.Tags);
			var count = point.GetHistogramCount();
			var sum = point.GetHistogramSum();
			string min = "-", max = "-";
			if (point.TryGetHistogramMinMaxValues(out double minVal, out double maxVal))
			{
				min = minVal.ToString("F2");
				max = maxVal.ToString("F2");
			}
			sb.AppendLine($"| {tags} | {count} | {sum:F2} | {min} | {max} |");
		}

		sb.AppendLine();
	}

	private static void AppendLongSumTable(StringBuilder sb, Metric metric)
	{
		sb.AppendLine("| Tags | Value |");
		sb.AppendLine("|------|-------|");

		foreach (ref readonly var point in metric.GetMetricPoints())
		{
			var tags = FormatTags(point.Tags);
			sb.AppendLine($"| {tags} | {point.GetSumLong()} |");
		}

		sb.AppendLine();
	}

	private static void AppendLongGaugeTable(StringBuilder sb, Metric metric)
	{
		sb.AppendLine("| Tags | Value |");
		sb.AppendLine("|------|-------|");

		foreach (ref readonly var point in metric.GetMetricPoints())
		{
			var tags = FormatTags(point.Tags);
			sb.AppendLine($"| {tags} | {point.GetGaugeLastValueLong()} |");
		}

		sb.AppendLine();
	}

	private static string FormatTags(ReadOnlyTagCollection tags)
	{
		var parts = new List<string>();
		foreach (var tag in tags)
			parts.Add($"{tag.Key}={tag.Value}");
		return parts.Count > 0 ? string.Join(", ", parts) : "(none)";
	}
}
