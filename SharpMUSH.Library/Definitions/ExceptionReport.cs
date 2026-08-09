using System.Buffers;
using System.Text;
using System.Text.Json;

namespace SharpMUSH.Library.Definitions;

/// <summary>
/// Builds the JSON payload carried by <see cref="ErrorMessages.Returns.ExceptionFormat"/>
/// (<c>#-1 EXCEPTION: {json}</c>) when a command throws.
///
/// <para><b>Disclosure rule.</b> The payload is built from an allowlist, never by scrubbing a
/// formatted exception. A mortal receives only fields that cannot carry runtime data:</para>
/// <list type="bullet">
/// <item><description><c>id</c> — correlation id, also written to the server log entry that holds
/// the full exception. This is what a player quotes in a bug report.</description></item>
/// <item><description><c>command</c> — the command word the player typed.</description></item>
/// <item><description><c>type</c> — the exception's simple CLR type name. A class name is a closed
/// set of identifiers chosen at compile time; it never contains a path, a connection string, a
/// dbref, or any other runtime value.</description></item>
/// </list>
/// <para>A privileged executor (God, Wizard, or Royalty — <c>IsPriv</c>) additionally receives
/// <c>message</c> and the <c>inner</c> exception chain, which CAN contain paths and connection
/// strings and so are withheld from everyone else.</para>
/// <para>Stack frames are never sent to any player, privileged or not: they are unreadable on a
/// MUSH line and the server log already has them, reachable by <c>id</c>.</para>
/// </summary>
public static class ExceptionReport
{
	/// <summary>Command words are short; a longer one is a parse artifact, not something to echo back.</summary>
	private const int MaxCommandLength = 64;

	/// <summary>Keeps a pathological exception message from filling a player's screen.</summary>
	private const int MaxMessageLength = 500;

	/// <summary>How far down the <see cref="Exception.InnerException"/> chain to report.</summary>
	private const int MaxInnerDepth = 3;

	/// <summary>
	/// A short, unique token written both to the player-visible payload and to the server log entry
	/// carrying the full exception, so the two can be tied together without disclosing anything.
	/// </summary>
	public static string NewCorrelationId() => Guid.NewGuid().ToString("N")[..12];

	/// <summary>
	/// The JSON object alone, without the <c>#-1 EXCEPTION: </c> prefix.
	/// </summary>
	/// <param name="exception">The exception that escaped the command.</param>
	/// <param name="command">The command word that produced it, if known.</param>
	/// <param name="correlationId">Token shared with the server log entry.</param>
	/// <param name="privileged">Whether the recipient may see the exception message and inner chain.</param>
	public static string Payload(Exception exception, string? command, string correlationId, bool privileged)
	{
		var buffer = new ArrayBufferWriter<byte>(256);

		using (var writer = new Utf8JsonWriter(buffer))
		{
			writer.WriteStartObject();
			writer.WriteString("id", correlationId);

			if (!string.IsNullOrWhiteSpace(command))
			{
				writer.WriteString("command", Clamp(command, MaxCommandLength));
			}

			writer.WriteString("type", exception.GetType().Name);

			if (privileged)
			{
				writer.WriteString("message", Clamp(exception.Message, MaxMessageLength));

				var inner = exception.InnerException;
				if (inner is not null)
				{
					writer.WriteStartArray("inner");
					for (var depth = 0; inner is not null && depth < MaxInnerDepth; depth++, inner = inner.InnerException)
					{
						writer.WriteStartObject();
						writer.WriteString("type", inner.GetType().Name);
						writer.WriteString("message", Clamp(inner.Message, MaxMessageLength));
						writer.WriteEndObject();
					}

					writer.WriteEndArray();
				}
			}

			writer.WriteEndObject();
		}

		return Encoding.UTF8.GetString(buffer.WrittenSpan);
	}

	/// <summary>
	/// The full player-visible return: <c>#-1 EXCEPTION: {json}</c>.
	/// </summary>
	public static string Format(Exception exception, string? command, string correlationId, bool privileged)
		=> string.Format(ErrorMessages.Returns.ExceptionFormat,
			Payload(exception, command, correlationId, privileged));

	private static string Clamp(string value, int max)
		=> value.Length <= max ? value : string.Concat(value.AsSpan(0, max), "...");
}
