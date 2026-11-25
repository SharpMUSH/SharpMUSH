using System.Globalization;
using System.Text.Json;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Implementation.Services;

/// <summary>How many connections an out-of-band package went to.</summary>
public readonly record struct OutOfBandSent(int Connections);

/// <summary>What sending an out-of-band package came to: the connections it reached, a refusal, or a bad message.</summary>
public union OutOfBandOutcome(OutOfBandSent, PermissionRefusal, Error<string>);

/// <summary>
/// Sends an out-of-band package to players or descriptors: the one implementation behind <c>oob()</c> and
/// <c>@oob</c>, so both take the same targets, check the same permission and deliver the same way.
/// </summary>
public interface IOutOfBandDelivery
{
	/// <summary>
	/// Sends <paramref name="package"/> with <paramref name="message"/> (JSON, or empty) to each of
	/// <paramref name="players"/>, a space-separated list of players and descriptors. A name that matches
	/// nothing is reported to <paramref name="executor"/> and skipped; a target that is not
	/// <paramref name="executor"/>'s own refuses the whole send unless they may reach others.
	/// </summary>
	ValueTask<OutOfBandOutcome> SendAsync(IMUSHCodeParser parser, AnySharpObject executor,
		string players, string package, string message, bool onlySupported);
}

/// <inheritdoc cref="IOutOfBandDelivery"/>
public sealed class OutOfBandDelivery(ILocateService locateService, IConnectionService connectionService, IMessageBus messageBus)
	: IOutOfBandDelivery
{
	private static readonly string[] OobScopes = [PortalPermission.GameWizard, PortalPermission.GamePower("Send_OOB")];

	/// <summary>
	/// Who may send out-of-band data to a connection that is not their own: a wizard, or anyone with the Send_OOB power.
	/// </summary>
	public static async ValueTask<bool> MayReachOthersAsync(AnySharpObject executor)
		=> await executor.IsWizard() || await executor.HasPower("Send_OOB");

	/// <inheritdoc/>
	public async ValueTask<OutOfBandOutcome> SendAsync(IMUSHCodeParser parser, AnySharpObject executor,
		string players, string package, string message, bool onlySupported)
	{
		if (!string.IsNullOrWhiteSpace(message))
		{
			try
			{
				using var _ = JsonDocument.Parse(message);
			}
			catch (JsonException)
			{
				return new Error<string>(ErrorMessages.Returns.InvalidJsonMessage);
			}
		}

		var mayReachOthers = await MayReachOthersAsync(executor);
		var executorRef = executor.Object().DBRef;
		var sentCount = 0;

		foreach (var target in ArgHelpers.NameListString(players))
		{
			// A descriptor reaches a connection before anyone has logged in on it, which is when a client
			// such as Mudlet sends External.Discord.Hello; see SOCKET`GMCP.
			if (long.TryParse(target, NumberStyles.None, CultureInfo.InvariantCulture, out var descriptor))
			{
				var connection = connectionService.Get(descriptor);
				if (connection is null)
				{
					continue;
				}

				if (!mayReachOthers && connection.Ref?.Number != executorRef.Number)
				{
					return PermissionRefusal.Needs(OobScopes);
				}

				sentCount += await SendToConnectionAsync(connection, package, message, onlySupported) ? 1 : 0;
				continue;
			}

			var locate = await locateService.LocateAndNotifyIfInvalid(parser, executor, executor, target, LocateFlags.All);

			if (locate is not AnySharpObject located || !located.IsPlayer)
			{
				continue;
			}

			if (!mayReachOthers && executorRef != located.Object().DBRef)
			{
				return PermissionRefusal.Needs(OobScopes);
			}

			await foreach (var connection in connectionService.Get(located.Object().DBRef))
			{
				sentCount += await SendToConnectionAsync(connection, package, message, onlySupported) ? 1 : 0;
			}
		}

		return new OutOfBandSent(sentCount);
	}

	/// <summary>
	/// Sends one package to one connection: WebSocket (portal) connections get a structured envelope the
	/// browser routes by package, GMCP-negotiated telnet connections a GMCP package. Any other connection
	/// (plain telnet without GMCP) is skipped, and so is a GMCP client that listed modules in Core.Supports
	/// without this package's when <paramref name="onlySupported"/> is set.
	/// </summary>
	private async ValueTask<bool> SendToConnectionAsync(IConnectionService.ConnectionData connection, string package,
		string message, bool onlySupported)
	{
		if (connection.ConnectionType == "websocket")
		{
			await messageBus.Publish(new WebSocketOutputMessage(connection.Handle, WebSocketOobEnvelope.Build(package, message)));
			return true;
		}

		if (connection.Metadata.GetValueOrDefault("GMCP", "0") != "1"
				|| (onlySupported && !GmcpSupports.Accepts(connection.Metadata.GetValueOrDefault(GmcpSupports.MetadataKey), package)))
		{
			return false;
		}

		await messageBus.Publish(new GMCPOutputMessage(connection.Handle, package, message));
		return true;
	}
}
