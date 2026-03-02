using Core.Arango;
using Core.Arango.Migration;
using Core.Arango.Protocol;
using DotNext.Threading;
using MarkupString;
using Mediator;
using Microsoft.Extensions.Logging;
using OneOf.Types;
using SharpMUSH.Database.Models;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace SharpMUSH.Database.ArangoDB;

public partial class ArangoDatabase
{
	#region Attributes

	private IAsyncEnumerable<SharpAttributeFlag> GetAttributeFlagsAsync(string id,
		CancellationToken ct = default) =>
		arangoDb.Query.ExecuteStreamAsync<SharpAttributeFlagQueryResult>(handle,
				$"FOR v in 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphAttributeFlags} RETURN v",
				new Dictionary<string, object> { { StartVertex, id } }, cancellationToken: ct)
			.Select(x =>
				new SharpAttributeFlag()
				{
					Name = x.Name,
					Symbol = x.Symbol,
					System = x.System,
					Inheritable = x.Inheritable,
					Id = x.Id
				});
	private IAsyncEnumerable<SharpAttribute> GetAllAttributesAsync(string id, CancellationToken ct = default)
	{
		// This only works for when we get a non-attribute as our ID.
		// Adjustment is needed if we get an attribute ID.
		IAsyncEnumerable<SharpAttributeQueryResult> sharpAttributeResults;
		if (id.StartsWith(DatabaseConstants.Attributes))
		{
			sharpAttributeResults = arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle,
				$"FOR v IN 1..999 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphAttributes} RETURN v",
				new Dictionary<string, object>() { { StartVertex, id } }, cancellationToken: ct);
		}
		else
		{
			sharpAttributeResults = arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle,
				$"LET start = FIRST(FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphObjects} RETURN v) FOR v IN 1..999 OUTBOUND start GRAPH {DatabaseConstants.GraphAttributes} RETURN v",
				new Dictionary<string, object> { { "startVertex", id } }, cancellationToken: ct);
		}

		return sharpAttributeResults
			.Select(SharpAttributeQueryToSharpAttribute);
	}

	private IAsyncEnumerable<LazySharpAttribute> GetAllLazyAttributesAsync(string id, CancellationToken ct = default)
	{
		// This only works for when we get a non-attribute as our ID.
		// Adjustment is needed if we get an attribute ID.
		IAsyncEnumerable<SharpAttributeQueryResult> sharpAttributeResults;
		if (id.StartsWith(DatabaseConstants.Attributes))
		{
			sharpAttributeResults = arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle,
				$"FOR v IN 1..999 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphAttributes} RETURN v",
				new Dictionary<string, object>() { { StartVertex, id } }, cancellationToken: ct);
		}
		else
		{
			sharpAttributeResults = arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle,
				$"LET start = FIRST(FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphObjects} RETURN v) FOR v IN 1..999 OUTBOUND start GRAPH {DatabaseConstants.GraphAttributes} RETURN v",
				new Dictionary<string, object> { { "startVertex", id } }, cancellationToken: ct);
		}

		return sharpAttributeResults
			.Select(SharpAttributeQueryToLazySharpAttribute);
	}
	public IAsyncEnumerable<SharpAttributeEntry> GetAllAttributeEntriesAsync(CancellationToken ct = default)
		=> arangoDb.Query.ExecuteStreamAsync<SharpAttributeEntry>(handle,
			$"FOR v IN {DatabaseConstants.AttributeEntries:@} RETURN v", true, cancellationToken: ct);

	public async ValueTask<SharpAttributeEntry?> GetSharpAttributeEntry(string name, CancellationToken ct = default)
		=> (await arangoDb.Query.ExecuteAsync<SharpAttributeEntry>(handle,
				$"FOR v IN {DatabaseConstants.AttributeEntries:@} FILTER v.Name == {name} RETURN v", true,
				cancellationToken: ct))
			.FirstOrDefault();

	public async ValueTask<SharpAttributeEntry?> CreateOrUpdateAttributeEntryAsync(string name, string[] defaultFlags,
		string? limit = null, string[]? enumValues = null, CancellationToken ct = default)
	{
		// Check if entry already exists
		var existing = await GetSharpAttributeEntry(name, ct);

		if (existing != null)
		{
			// Update existing entry - build document System.Text.Json.JsonElementally to omit null fields
			var document = new Dictionary<string, object>
			{
				{ "_key", existing.Id!.Split('/')[1] },
				{ "Name", name },
				{ "DefaultFlags", defaultFlags }
			};

			if (limit != null)
				document["Limit"] = limit;
			if (enumValues != null)
				document["Enum"] = enumValues;

			var updated = await arangoDb.Document.UpdateAsync<Dictionary<string, object>, SharpAttributeEntry>(handle,
				DatabaseConstants.AttributeEntries,
				document,
				waitForSync: true,
				cancellationToken: ct,
				returnNew: true);

			return updated.New;
		}
		else
		{
			// Create new entry - build document System.Text.Json.JsonElementally to omit null fields
			var document = new Dictionary<string, object>
			{
				{ "_key", name.ToUpper() },
				{ "Name", name },
				{ "DefaultFlags", defaultFlags }
			};

			if (limit != null)
				document["Limit"] = limit;
			if (enumValues != null)
				document["Enum"] = enumValues;

			var created = await arangoDb.Document.CreateAsync<Dictionary<string, object>, SharpAttributeEntry>(handle,
				DatabaseConstants.AttributeEntries,
				document,
				waitForSync: true,
				cancellationToken: ct,
				returnNew: true);

			return created.New;
		}
	}

	public async ValueTask<bool> DeleteAttributeEntryAsync(string name, CancellationToken ct = default)
	{
		var existing = await GetSharpAttributeEntry(name, ct);
		if (existing == null)
		{
			return false;
		}

		await arangoDb.Document.DeleteAsync<object>(handle, DatabaseConstants.AttributeEntries, existing.Id!.Split('/')[1],
			waitForSync: true, cancellationToken: ct);

		return true;
	}
	private IAsyncEnumerable<SharpAttribute> GetTopLevelAttributesAsync(string id, CancellationToken ct = default)
	{
		// This only works for when we get a non-attribute as our ID.
		// Adjustment is needed if we get an attribute ID.
		IAsyncEnumerable<SharpAttributeQueryResult> sharpAttributeResults;
		if (id.StartsWith(DatabaseConstants.Attributes))
		{
			sharpAttributeResults = arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle,
				$"FOR v IN 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphAttributes} RETURN v",
				new Dictionary<string, object> { { StartVertex, id } }, cancellationToken: ct);
		}
		else
		{
			sharpAttributeResults = arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle,
				$"LET start = FIRST(FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphObjects} RETURN v) FOR v IN 1..1 OUTBOUND start GRAPH {DatabaseConstants.GraphAttributes} RETURN v",
				new Dictionary<string, object> { { StartVertex, id } }, cancellationToken: ct);
		}

		return sharpAttributeResults
			.Select(SharpAttributeQueryToSharpAttribute);
	}

	private async ValueTask<SharpAttributeEntry?> GetRelatedAttributeEntry(string id, CancellationToken ct = default)
	{
		var result = await arangoDb.Query.ExecuteAsync<SharpAttributeEntryQueryResult>(handle,
			$"FOR v IN 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphAttributeEntries} RETURN v",
			new Dictionary<string, object> { { StartVertex, id } }, cancellationToken: ct);

		if (result is null) return null;
		var entry = result.First();

		return new SharpAttributeEntry
		{
			DefaultFlags = entry.DefaultFlags,
			Name = entry.Name,
			Enum = entry.Enum,
			Id = entry.Id,
			Limit = entry.Limit
		};
	}

	private async ValueTask<SharpAttribute> SharpAttributeQueryToSharpAttribute(SharpAttributeQueryResult x,
		CancellationToken cancellationToken = default)
		=> new(
			x.Id,
			x.Key,
			x.Name,
			await GetAttributeFlagsAsync(x.Id, cancellationToken).ToArrayAsync(cancellationToken),
			null,
			x.LongName,
			new AsyncLazy<IAsyncEnumerable<SharpAttribute>>(ct => Task.FromResult(GetTopLevelAttributesAsync(x.Id, ct))),
			new AsyncLazy<SharpPlayer?>(async ct => await GetAttributeOwnerAsync(x.Id, ct)),
			new AsyncLazy<SharpAttributeEntry?>(async ct => await GetRelatedAttributeEntry(x.Id, ct)))
		{
			Value = MarkupStringModule.deserialize(x.Value)
		};

	private IAsyncEnumerable<LazySharpAttribute> GetTopLevelLazyAttributesAsync(string id,
		CancellationToken cancellationToken = default)
	{
		// This only works for when we get a non-attribute as our ID.
		// Adjustment is needed if we get an attribute ID.
		IAsyncEnumerable<SharpAttributeQueryResult> sharpAttributeResults;
		if (id.StartsWith(DatabaseConstants.Attributes))
		{
			sharpAttributeResults = arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle,
				$"FOR v IN 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphAttributes} RETURN v",
				new Dictionary<string, object> { { StartVertex, id } }, cancellationToken: cancellationToken);
		}
		else
		{
			sharpAttributeResults = arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle,
				$"LET start = FIRST(FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphObjects} RETURN v) FOR v IN 1..1 OUTBOUND start GRAPH {DatabaseConstants.GraphAttributes} RETURN v",
				new Dictionary<string, object> { { StartVertex, id } }, cancellationToken: cancellationToken);
		}

		return sharpAttributeResults
			.Select<SharpAttributeQueryResult, LazySharpAttribute>(async (x, ctOuter) =>
				new LazySharpAttribute(
					x.Id,
					x.Key,
					x.Name,
					await GetAttributeFlagsAsync(x.Id, ctOuter).ToArrayAsync(ctOuter),
					null,
					x.LongName,
					new AsyncLazy<IAsyncEnumerable<LazySharpAttribute>>(ct =>
						Task.FromResult(GetTopLevelLazyAttributesAsync(x.Id, ct))),
					new AsyncLazy<SharpPlayer?>(async ct => await GetAttributeOwnerAsync(x.Id, ct)),
					new AsyncLazy<SharpAttributeEntry?>(async ct => await GetRelatedAttributeEntry(x.Id, ct)),
					Value: new AsyncLazy<MarkupStringModule.MarkupString>(async ct =>
						MarkupStringModule.deserialize(await GetAttributeValue(x.Key, ct)))));
	}

	public async IAsyncEnumerable<LazySharpAttribute> GetLazyAttributesAsync(DBRef dbref,
		string attributePattern, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var startVertex = $"{DatabaseConstants.Objects}/{dbref.Number}";
		var result =
			await arangoDb.Query.ExecuteAsync<SharpObjectQueryResult>(handle, $"RETURN DOCUMENT({startVertex})", cache: true,
				cancellationToken: cancellationToken);

		var pattern = WildcardToRegex()
			.Replace(attributePattern, m => m.Value switch
			{
				"**" => ".*",
				"*" => "[^`]*",
				"?" => ".",
				_ => $"\\{m.Value}"
			});

		if (!result.Any())
		{
			yield break;
		}

		// OPTIONS { indexHint: "inverted_index_name", forceIndexHint: true }
		// This doesn't seem like it can be done on a GRAPH query?
		const string query =
			$"FOR v IN 1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphAttributes} FILTER v.LongName =~ @pattern RETURN v";

		var queryResult = arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle, query,
			new Dictionary<string, object>
			{
				{ StartVertex, startVertex },
				{ "pattern", $"^{pattern}$" }
			}, cancellationToken: cancellationToken);

		await foreach (var item in queryResult.WithCancellation(cancellationToken))
		{
			yield return await SharpAttributeQueryToLazySharpAttribute(item, cancellationToken);
		}
	}


	public async IAsyncEnumerable<SharpAttribute> GetAttributesAsync(DBRef dbref, string attributePattern,
		[EnumeratorCancellation] CancellationToken ct = default)
	{
		var startVertex = $"{DatabaseConstants.Objects}/{dbref.Number}";
		var result =
			await arangoDb.Query.ExecuteAsync<SharpObjectQueryResult>(handle,
				$"FOR v IN 1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphObjects} RETURN v",
				new Dictionary<string, object>
				{
					{ StartVertex, startVertex }
				}, cache: true,
				cancellationToken: ct);

		var pattern = WildcardToRegex().Replace(attributePattern, m => m.Value switch
		{
			"**" => ".*",
			"*" => "[^`]*",
			"?" => ".",
			_ => $"\\{m.Value}"
		});

		if (!result.Any())
		{
			yield break;
		}

		// OPTIONS { indexHint: "inverted_index_name", forceIndexHint: true }
		// This doesn't seem like it can be done on a GRAPH query?
		const string query =
			$"FOR v IN 1..99999 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphAttributes} FILTER v.LongName =~ @pattern  RETURN v";

		// FILTER v.LongName =~ @pattern 

		var queryResult = arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle, query,
			new Dictionary<string, object>
			{
				{ StartVertex, result.First().Id },
				{ "pattern", $"^{pattern}$" }
			}, cancellationToken: ct);

		await foreach (var item in queryResult.WithCancellation(ct))
		{
			yield return await SharpAttributeQueryToSharpAttribute(item, ct);
		}
	}

	public async IAsyncEnumerable<SharpAttribute> GetAttributesByRegexAsync(DBRef dbref,
		string attributePattern, [EnumeratorCancellation] CancellationToken ct = default)
	{
		var startVertex = $"{DatabaseConstants.Objects}/{dbref.Number}";
		var result =
			await arangoDb.Query.ExecuteAsync<SharpObjectQueryResult>(handle,
				$"FOR v IN 1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphObjects} RETURN v",
				new Dictionary<string, object>
				{
					{ StartVertex, startVertex }
				}, cache: true,
				cancellationToken: ct);

		var pattern = $"(?i){attributePattern}"; // Add case-insensitive flag

		if (!result.Any())
		{
			yield break;
		}

		// Pattern matching supports hierarchical attribute trees with proper backtick handling:
		// - Single wildcard (*) matches within one tree level: "FOO*" matches "FOOBAR" but not "FOO`BAR"
		// - Double wildcard (**) matches across tree levels: "FOO**" matches "FOOBAR" and "FOO`BAR`BAZ"
		// - Question mark (?) matches a single character
		// The WildcardToRegex() conversion properly escapes backticks in single wildcards.
		//
		// Results are sorted hierarchically (parent before children) by LongName.

		// OPTIONS { indexHint: "inverted_index_name", forceIndexHint: true }
		// This doesn't seem like it can be done on a GRAPH query?
		const string query =
			$"FOR v IN 1..99999 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphAttributes} FILTER v.LongName =~ @pattern SORT v.LongName ASC RETURN v";

		var queryResult = arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle, query,
			new Dictionary<string, object>
			{
				{ StartVertex, result.First().Id },
				{ "pattern", pattern }
			}, cancellationToken: ct);

		await foreach (var item in queryResult.WithCancellation(ct))
		{
			yield return await SharpAttributeQueryToSharpAttribute(item, ct);
		}
	}


	public async IAsyncEnumerable<LazySharpAttribute> GetLazyAttributesByRegexAsync(DBRef dbref,
		string attributePattern, [EnumeratorCancellation] CancellationToken ct = default)
	{
		var startVertex = $"{DatabaseConstants.Objects}/{dbref.Number}";
		var result =
			await arangoDb.Query.ExecuteAsync<SharpObjectQueryResult>(handle, $"RETURN DOCUMENT({startVertex})", cache: true,
				cancellationToken: ct);

		if (!result.Any())
		{
			yield break;
		}

		// Pattern matching supports hierarchical attribute trees with proper backtick handling:
		// - Single wildcard (*) matches within one tree level: "FOO*" matches "FOOBAR" but not "FOO`BAR"
		// - Double wildcard (**) matches across tree levels: "FOO**" matches "FOOBAR" and "FOO`BAR`BAZ"
		// - Question mark (?) matches a single character
		// The WildcardToRegex() conversion properly escapes backticks in single wildcards.
		//
		// Results are sorted hierarchically (parent before children) by LongName.

		// OPTIONS { indexHint: "inverted_index_name", forceIndexHint: true }
		// This doesn't seem like it can be done on a GRAPH query?
		var pattern = $"(?i){attributePattern}"; // Add case-insensitive flag
		const string query =
			$"FOR v IN 1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphAttributes} FILTER v.LongName =~ @pattern SORT v.LongName ASC RETURN v";

		var result2 = arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle, query,
			new Dictionary<string, object>
			{
				{ StartVertex, startVertex },
				{ "pattern", pattern }
			}, cancellationToken: ct);

		await foreach (var item in result2.WithCancellation(ct))
		{
			yield return await SharpAttributeQueryToLazySharpAttribute(item, ct);
		}
	}

	private async ValueTask<LazySharpAttribute> SharpAttributeQueryToLazySharpAttribute(SharpAttributeQueryResult x,
		CancellationToken cancellationToken = default)
		=> new(
			x.Id,
			x.Key,
			x.Name,
			await GetAttributeFlagsAsync(x.Id, cancellationToken).ToArrayAsync(cancellationToken),
			null,
			x.LongName,
			new AsyncLazy<IAsyncEnumerable<LazySharpAttribute>>(ct =>
				Task.FromResult(GetTopLevelLazyAttributesAsync(x.Id, ct))),
			new AsyncLazy<SharpPlayer?>(async ct => await GetObjectOwnerAsync(x.Id, ct)),
			new AsyncLazy<SharpAttributeEntry?>(async ct => await GetRelatedAttributeEntry(x.Id, ct)),
			new AsyncLazy<MarkupStringModule.MarkupString>(async ct =>
				MarkupStringModule.deserialize(await GetAttributeValue(x.Key, ct))));

	private async ValueTask<string> GetAttributeValue(string key, CancellationToken ct = default)
	{
		var result = await arangoDb.Document.GetAsync<SharpAttributeQueryResult>(
			handle,
			DatabaseConstants.Attributes, key,
			cancellationToken: ct);
		return result?.Value ?? string.Empty;
	}

	public async IAsyncEnumerable<SharpAttribute>? GetAttributesRegexAsync(DBRef dbref,
		string attributePattern,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var startVertex = $"{DatabaseConstants.Objects}/{dbref.Number}";
		var result =
			await arangoDb.Query.ExecuteAsync<SharpObjectQueryResult>(handle, $"RETURN DOCUMENT({startVertex})", cache: true,
				cancellationToken: cancellationToken);

		if (!result.Any())
		{
			yield break;
		}

		const string query =
			$"FOR v IN 1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphAttributes} FILTER v.LongName =~ @pattern RETURN v";

		var result2 = arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle, query,
			new Dictionary<string, object>()
			{
				{ StartVertex, startVertex },
				{ "pattern", attributePattern }
			}, cancellationToken: cancellationToken);

		await foreach (var item in result2.WithCancellation(cancellationToken))
		{
			yield return await SharpAttributeQueryToSharpAttribute(item, cancellationToken);
		}
	}
	public async IAsyncEnumerable<SharpAttribute> GetAttributeAsync(DBRef dbref, string[] attribute,
		[EnumeratorCancellation] CancellationToken ct = default)
	{
		var startVertex = $"{DatabaseConstants.Objects}/{dbref.Number}";

		const string let =
			$"LET start = FIRST(FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphObjects} RETURN v)";
		const string query =
			$"{let} FOR v,e,p IN 1..@max OUTBOUND start GRAPH {DatabaseConstants.GraphAttributes} PRUNE condition = NTH(@attr,LENGTH(p.edges)-1) != v.Name FILTER !condition RETURN v";

		var result = arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle, query,
			new Dictionary<string, object>()
			{
				{ "attr", attribute.Select(x => x.ToUpper()) },
				{ StartVertex, startVertex },
				{ "max", attribute.Length }
			}, cancellationToken: ct);

		if (result == null)
		{
			yield break;
		}

		var count = 0;
		var resulted = await result.Select(async (item, _, innerCt) =>
		{
			count++;
			return await SharpAttributeQueryToSharpAttribute(item, innerCt);
		}).ToArrayAsync(cancellationToken: ct);

		if (count != attribute.Length)
		{
			yield break;
		}

		foreach (var item in resulted)
		{
			yield return item;
		}
	}

	public IAsyncEnumerable<LazySharpAttribute> GetLazyAttributeAsync(DBRef dbref,
		string[] attribute, CancellationToken ct = default)
	{
		var startVertex = $"{DatabaseConstants.Objects}/{dbref.Number}";

		const string let =
			$"LET start = FIRST(FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphObjects} RETURN v)";
		const string query =
			$"{let} FOR v,e,p IN 1..@max OUTBOUND start GRAPH {DatabaseConstants.GraphAttributes} PRUNE condition = NTH(@attr,LENGTH(p.edges)-1) != v.Name FILTER !condition RETURN v";

		var result = arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle, query,
			new Dictionary<string, object>
			{
				{ "attr", attribute.Select(x => x.ToUpper()) },
				{ StartVertex, startVertex },
				{ "max", attribute.Length }
			}, cancellationToken: ct);

		return result?.Select(SharpAttributeQueryToLazySharpAttribute)
			?? AsyncEnumerable.Empty<LazySharpAttribute>();
	}

	public async ValueTask<bool> SetAttributeAsync(DBRef dbref, string[] attribute, MarkupStringModule.MarkupString value,
		SharpPlayer owner, CancellationToken ct = default)
	{
		ArgumentException.ThrowIfNullOrEmpty(owner.Id);
		attribute = attribute.Select(x => x.ToUpper()).ToArray();

		var transactionHandle = await arangoDb.Transaction.BeginAsync(handle, new ArangoTransaction
		{
			LockTimeout = DatabaseBehaviorConstants.TransactionTimeout,
			WaitForSync = true,
			AllowImplicit = false,
			Collections = new ArangoTransactionScope
			{
				Exclusive =
				[
					DatabaseConstants.Attributes,
					DatabaseConstants.HasAttribute,
					DatabaseConstants.HasAttributeFlag,
					DatabaseConstants.HasAttributeOwner
				],
				Read =
				[
					DatabaseConstants.Attributes, DatabaseConstants.HasAttribute, DatabaseConstants.Objects,
					DatabaseConstants.HasAttributeFlag,
					DatabaseConstants.IsObject, DatabaseConstants.Players, DatabaseConstants.Rooms, DatabaseConstants.Things,
					DatabaseConstants.Exits
				]
			}
		}, ct);

		var startVertex = $"{DatabaseConstants.Objects}/{dbref.Number}";
		const string let1 =
			$"LET start = (FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphObjects} RETURN v._id)";
		const string let2 =
			$"LET foundAttributes = (FOR v,e,p IN 1..@max OUTBOUND FIRST(start) GRAPH {DatabaseConstants.GraphAttributes} PRUNE condition = NTH(@attr,LENGTH(p.edges)-1) != v.Name FILTER !condition RETURN v._id)";
		const string query = $"{let1} {let2} RETURN APPEND(start, foundAttributes)";

		var result = await arangoDb.Query.ExecuteAsync<string[]>(handle, query, new Dictionary<string, object>
		{
			{ "attr", attribute },
			{ StartVertex, startVertex },
			{ "max", attribute.Length }
		}, cancellationToken: ct);

		var actualResult = result.First();

		var matches = actualResult.Length;
		var remaining = attribute.Skip(matches - 1).ToArray();
		var lastId = actualResult.Last();

		// Create Path
		foreach (var nextAttr in remaining.Select((attrName, i) => (value: attrName, i)))
		{
			var longName = string.Join('`', attribute.SkipLast(remaining.Length - 1 - nextAttr.i));

			var sharpAttributeEntry = await GetSharpAttributeEntry(longName, ct);

			// Get flags from the attribute entry and resolve them
			var flagNames = sharpAttributeEntry?.DefaultFlags ?? [];
			var resolvedFlags = new List<SharpAttributeFlag>();
			foreach (var flagName in flagNames)
			{
				var flag = await GetAttributeFlagAsync(flagName, ct);
				if (flag != null)
				{
					resolvedFlags.Add(flag);
				}
			}

			var newOne = await arangoDb.Document.CreateAsync<SharpAttributeCreateRequest, SharpAttributeQueryResult>(
				transactionHandle, DatabaseConstants.Attributes,
				new SharpAttributeCreateRequest(nextAttr.value.ToUpper(),
					nextAttr.i == remaining.Length - 1
						? MarkupStringModule.serialize(value)
						: string.Empty,
					longName),
				waitForSync: true, cancellationToken: ct, returnNew: true);

			foreach (var flag in resolvedFlags)
			{
				await SetAttributeFlagAsync(transactionHandle, newOne.New.Id, flag, ct);
			}

			await arangoDb.Graph.Edge.CreateAsync(transactionHandle, DatabaseConstants.GraphAttributes,
				DatabaseConstants.HasAttribute,
				new SharpEdgeCreateRequest(lastId, newOne.Id), waitForSync: true, cancellationToken: ct);

			await arangoDb.Graph.Edge.CreateAsync(transactionHandle, DatabaseConstants.GraphAttributeOwners,
				DatabaseConstants.HasAttributeOwner,
				new SharpEdgeCreateRequest(newOne.Id, owner.Id!), waitForSync: true, cancellationToken: ct);

			lastId = newOne.Id;
		}

		// Update Path
		if (remaining.Length == 0)
		{
			await arangoDb.Document.UpdateAsync(transactionHandle, DatabaseConstants.Attributes,
				new { Key = lastId.Split('/')[1], Value = MarkupStringModule.serialize(value) }, waitForSync: true,
				mergeObjects: true, cancellationToken: ct);

			await arangoDb.Graph.Edge.CreateAsync(transactionHandle, DatabaseConstants.GraphAttributeOwners,
				DatabaseConstants.HasAttributeOwner,
				new SharpEdgeCreateRequest(lastId, owner.Id!), waitForSync: true, cancellationToken: ct);
		}

		await arangoDb.Transaction.CommitAsync(transactionHandle, ct);

		return true;
	}
	public async ValueTask<bool> SetAttributeFlagAsync(SharpObject dbref, string[] attribute, SharpAttributeFlag flag,
		CancellationToken ct = default)
	{
		var attrInfo = GetAttributeAsync(dbref.DBRef, attribute, ct);
		var attr = await attrInfo.LastOrDefaultAsync(cancellationToken: ct);
		if (attr is null) return false;

		await SetAttributeFlagAsync(attr, flag, ct);
		return true;
	}

	public async ValueTask SetAttributeFlagAsync(SharpAttribute attr, SharpAttributeFlag flag,
		CancellationToken ct = default)
		=> await arangoDb.Graph.Edge.CreateAsync(handle,
			DatabaseConstants.AttributeFlags, DatabaseConstants.HasAttributeFlag,
			new SharpEdgeCreateRequest(attr.Id, flag.Id!), cancellationToken: ct);


	private async ValueTask SetAttributeFlagAsync(ArangoHandle transactionHandle, string attrId, SharpAttributeFlag flag,
		CancellationToken ct = default)
		=> await arangoDb.Graph.Edge.CreateAsync(transactionHandle,
			DatabaseConstants.GraphAttributeFlags, DatabaseConstants.HasAttributeFlag,
			new SharpEdgeCreateRequest(attrId, flag.Id!), cancellationToken: ct);


	public async ValueTask<bool> UnsetAttributeFlagAsync(SharpObject dbref, string[] attribute, SharpAttributeFlag flag,
		CancellationToken ct = default)
	{
		var attrInfo = GetAttributeAsync(dbref.DBRef, attribute, ct);
		var attr = await attrInfo.LastOrDefaultAsync(cancellationToken: ct);
		if (attr is null) return false;

		await UnsetAttributeFlagAsync(attr, flag, ct);
		return true;
	}

	public async ValueTask UnsetAttributeFlagAsync(SharpAttribute attr, SharpAttributeFlag flag,
		CancellationToken ct = default) =>
		await arangoDb.Document.UpdateAsync(handle, DatabaseConstants.Attributes, new
		{
			attr.Key,
			Value = attr.Flags.ToImmutableArray().Remove(flag)
		}, cancellationToken: ct);

	public async ValueTask<SharpAttributeFlag?> GetAttributeFlagAsync(string flagName, CancellationToken ct = default) =>
		(await arangoDb.Query.ExecuteAsync<SharpAttributeFlagQueryResult>(handle,
			"FOR v in @@C1 FILTER UPPER(v.Name) == UPPER(@flag) RETURN v",
			bindVars: new Dictionary<string, object>
			{
				{ "@C1", DatabaseConstants.AttributeFlags },
				{ "flag", flagName }
			}, cache: true, cancellationToken: ct))
		.Select(SharpAttributeFlagQueryResultToSharpFlag)
		.FirstOrDefault();

	private static SharpAttributeFlag SharpAttributeFlagQueryResultToSharpFlag(SharpAttributeFlagQueryResult arg) =>
		new()
		{
			Id = arg.Id,
			Name = arg.Name,
			Inheritable = arg.Inheritable,
			Key = arg.Key,
			Symbol = arg.Symbol,
			System = arg.System
		};

	public IAsyncEnumerable<SharpAttributeFlag> GetAttributeFlagsAsync(CancellationToken ct = default) =>
		arangoDb.Query.ExecuteStreamAsync<SharpAttributeFlag>(handle,
			$"FOR v in {DatabaseConstants.AttributeFlags:@} RETURN v",
			cache: true, cancellationToken: ct);

	public async ValueTask<bool> ClearAttributeAsync(DBRef dbref, string[] attribute, CancellationToken ct = default)
	{
		// Set the contents to empty, or remove entirely if no children.
		attribute = attribute.Select(x => x.ToUpper()).ToArray();

		// Get the attribute
		var attrs = GetAttributeAsync(dbref, attribute, ct);
		var targetAttr = await attrs.LastOrDefaultAsync(ct);
		if (targetAttr is null) return false;

		// Check if attribute has children (just need to know if any exist)
		var children = await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v IN 1..1 OUTBOUND {targetAttr.Id} GRAPH {DatabaseConstants.GraphAttributes} LIMIT 1 RETURN v._id",
			cancellationToken: ct);

		if (children.Any())
		{
			// Has children, just clear the value
			await arangoDb.Document.UpdateAsync(handle, DatabaseConstants.Attributes,
				new { Key = targetAttr.Key, Value = MarkupStringModule.serialize(MarkupStringModule.empty()) },
				mergeObjects: true, cancellationToken: ct);
		}
		else
		{
			// No children, remove the attribute
			await arangoDb.Graph.Vertex.RemoveAsync(handle, DatabaseConstants.GraphAttributes,
				DatabaseConstants.Attributes, targetAttr.Key, cancellationToken: ct);
		}

		return true;
	}

	public async ValueTask<bool> WipeAttributeAsync(DBRef dbref, string[] attribute, CancellationToken ct = default)
	{
		// Wipe a list of attributes. We assume the calling code figured out the permissions part.
		attribute = attribute.Select(x => x.ToUpper()).ToArray();

		// Get the attribute
		var attrs = GetAttributeAsync(dbref, attribute, ct);
		var targetAttr = await attrs.LastOrDefaultAsync(ct);
		if (targetAttr is null) return false;

		// Get all descendants (children, grandchildren, etc.) - traverse to max depth
		var descendants = arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle,
			$"FOR v IN 1..999 OUTBOUND {targetAttr.Id} GRAPH {DatabaseConstants.GraphAttributes} RETURN v",
			cancellationToken: ct);

		// Remove all descendants first (bottom-up) to avoid orphans
		await foreach (var descendant in descendants.Reverse().WithCancellation(ct))
		{
			await arangoDb.Graph.Vertex.RemoveAsync(handle, DatabaseConstants.GraphAttributes,
				DatabaseConstants.Attributes, descendant.Key, cancellationToken: ct);
		}

		// Remove the target attribute itself
		await arangoDb.Graph.Vertex.RemoveAsync(handle, DatabaseConstants.GraphAttributes,
			DatabaseConstants.Attributes, targetAttr.Key, cancellationToken: ct);

		return true;
	}
	public async IAsyncEnumerable<AttributeWithInheritance> GetAttributeWithInheritanceAsync(
		DBRef dbref,
		string[] attribute,
		bool checkParent = true,
		[EnumeratorCancellation] CancellationToken ct = default)
	{
		attribute = attribute.Select(x => x.ToUpper()).ToArray();
		var startVertex = $"{DatabaseConstants.Objects}/{dbref.Number}";

		var query = $@"
			LET startObj = DOCUMENT(@startVertex)
			LET startThing = FIRST(FOR v IN 1..1 INBOUND startObj GRAPH {DatabaseConstants.GraphObjects} RETURN v)
			LET startObjKey = PARSE_IDENTIFIER(@startVertex).key
			LET attrPath = @attr
			LET maxDepth = @max
			
			LET selfAttrs = (
				FOR v,e,p IN 1..maxDepth OUTBOUND startThing GRAPH {DatabaseConstants.GraphAttributes}
					PRUNE condition = NTH(attrPath, LENGTH(p.edges)-1) != v.Name
					FILTER !condition
					RETURN v
			)
			LET selfResult = LENGTH(selfAttrs) == maxDepth ? {{
				attributes: selfAttrs,
				sourceId: startObjKey,
				source: 'Self',
				filterFlags: false
			}} : null
			
			LET parentResults = @checkParent && selfResult == null ? (
				FOR parent IN 1..100 OUTBOUND startObj GRAPH {DatabaseConstants.GraphParents}
					LET parentThing = FIRST(FOR v IN 1..1 INBOUND parent GRAPH {DatabaseConstants.GraphObjects} RETURN v)
					LET parentObjId = parent._key
					LET parentAttrs = (
						FOR v,e,p IN 1..maxDepth OUTBOUND parentThing GRAPH {DatabaseConstants.GraphAttributes}
							PRUNE condition = NTH(attrPath, LENGTH(p.edges)-1) != v.Name
							FILTER !condition
							RETURN v
					)
					FILTER LENGTH(parentAttrs) == maxDepth
					LIMIT 1
					RETURN {{
						attributes: parentAttrs,
						sourceId: parentObjId,
						source: 'Parent',
						filterFlags: true
					}}
			) : []
			
			LET zoneResults = @checkParent && selfResult == null && LENGTH(parentResults) == 0 ? (
				LET inheritanceChain = APPEND([startObj], (
					FOR parent IN 1..100 OUTBOUND startObj GRAPH {DatabaseConstants.GraphParents}
						RETURN parent
				))
				
				FOR obj IN inheritanceChain
					FOR zone IN 1..100 OUTBOUND obj GRAPH {DatabaseConstants.GraphZones}
						LET zoneThing = FIRST(FOR v IN 1..1 INBOUND zone GRAPH {DatabaseConstants.GraphObjects} RETURN v)
						LET zoneObjId = zone._key
						LET zoneAttrs = (
							FOR v,e,p IN 1..maxDepth OUTBOUND zoneThing GRAPH {DatabaseConstants.GraphAttributes}
								PRUNE condition = NTH(attrPath, LENGTH(p.edges)-1) != v.Name
								FILTER !condition
								RETURN v
						)
						FILTER LENGTH(zoneAttrs) == maxDepth
						LIMIT 1
						RETURN {{
							attributes: zoneAttrs,
							sourceId: zoneObjId,
							source: 'Zone',
							filterFlags: true
						}}
			) : []
			
			LET allResults = APPEND([selfResult], APPEND(parentResults, zoneResults))
			LET filtered = (FOR r IN allResults FILTER r != null RETURN r)
			RETURN FIRST(filtered)
		";

		var bindVars = new Dictionary<string, object>
		{
			{ "attr", attribute },
			{ StartVertex, startVertex },
			{ "max", attribute.Length },
			{ "checkParent", checkParent }
		};

		var results = await arangoDb.Query.ExecuteAsync<QueryResult>(handle, query, bindVars, cancellationToken: ct);
		var result = results.FirstOrDefault();

		if (result == null || result.attributes == null)
		{
			yield break;
		}

		var sourceDbRef = ParseDbRefFromId(result.sourceId);

		var sourceType = result.source switch
		{
			"Self" => AttributeSource.Self,
			"Parent" => AttributeSource.Parent,
			"Zone" => AttributeSource.Zone,
			_ => throw new InvalidOperationException($"Unknown source type: {result.source}")
		};

		var attrs = new SharpAttribute[result.attributes.Count];
		for (int i = 0; i < result.attributes.Count; i++)
		{
			attrs[i] = await SharpAttributeQueryToSharpAttribute(result.attributes[i], ct);
		}

		var lastAttr = attrs.Last();
		var flags = result.filterFlags
			? lastAttr.Flags.Where(f => f.Inheritable)
			: lastAttr.Flags;

		yield return new AttributeWithInheritance(attrs, sourceDbRef, sourceType, flags);
	}

	private async ValueTask<SharpAttribute> SharpAttributeQueryToSharpAttributeSimple(SharpAttributeQueryResult x,
		CancellationToken cancellationToken = default)
	{
		var flags = await GetAttributeFlagsAsync(x.Id, cancellationToken).ToArrayAsync(cancellationToken);

		return new SharpAttribute(
			x.Id,
			x.Key,
			x.Name,
			flags,
			null,
			x.LongName,
			new AsyncLazy<IAsyncEnumerable<SharpAttribute>>(ct => Task.FromResult(GetTopLevelAttributesAsync(x.Id, ct))),
			new AsyncLazy<SharpPlayer?>(async ct => await GetAttributeOwnerAsync(x.Id, ct)),
			new AsyncLazy<SharpAttributeEntry?>(async ct => await GetRelatedAttributeEntry(x.Id, ct)))
		{
			Value = MarkupStringModule.deserialize(x.Value)
		};
	}

	private record QueryResult(
		List<SharpAttributeQueryResult>? attributes,
		string sourceId,
		string source,
		bool filterFlags
	);

	private static DBRef ParseDbRefFromId(string key)
	{
		if (int.TryParse(key, out var number))
		{
			return new DBRef(number);
		}
		throw new InvalidOperationException($"Cannot parse DBRef from key: {key}");
	}

	public async IAsyncEnumerable<LazyAttributeWithInheritance> GetLazyAttributeWithInheritanceAsync(
		DBRef dbref,
		string[] attribute,
		bool checkParent = true,
		[EnumeratorCancellation] CancellationToken ct = default)
	{
		attribute = attribute.Select(x => x.ToUpper()).ToArray();

		var startVertex = $"{DatabaseConstants.Objects}/{dbref.Number}";

		var query = $@"
			LET startObj = DOCUMENT(@startVertex)
			LET startThing = FIRST(FOR v IN 1..1 INBOUND startObj GRAPH {DatabaseConstants.GraphObjects} RETURN v)
			LET startObjKey = PARSE_IDENTIFIER(@startVertex).key
			LET attrPath = @attr
			LET maxDepth = @max
			
			LET selfAttrs = (
				FOR v,e,p IN 1..maxDepth OUTBOUND startThing GRAPH {DatabaseConstants.GraphAttributes}
					PRUNE condition = NTH(attrPath, LENGTH(p.edges)-1) != v.Name
					FILTER !condition
					RETURN v
			)
			LET selfResult = LENGTH(selfAttrs) == maxDepth ? {{
				attributes: selfAttrs,
				sourceId: startObjKey,
				source: 'Self',
				filterFlags: false
			}} : null
			
			LET parentResults = @checkParent && selfResult == null ? (
				FOR parent IN 1..100 OUTBOUND startObj GRAPH {DatabaseConstants.GraphParents}
					LET parentThing = FIRST(FOR v IN 1..1 INBOUND parent GRAPH {DatabaseConstants.GraphObjects} RETURN v)
					LET parentObjId = parent._key
					LET parentAttrs = (
						FOR v,e,p IN 1..maxDepth OUTBOUND parentThing GRAPH {DatabaseConstants.GraphAttributes}
							PRUNE condition = NTH(attrPath, LENGTH(p.edges)-1) != v.Name
							FILTER !condition
							RETURN v
					)
					FILTER LENGTH(parentAttrs) == maxDepth
					LIMIT 1
					RETURN {{
						attributes: parentAttrs,
						sourceId: parentObjId,
						source: 'Parent',
						filterFlags: true
					}}
			) : []
			
			LET zoneResults = @checkParent && selfResult == null && LENGTH(parentResults) == 0 ? (
				LET inheritanceChain = APPEND([startObj], (
					FOR parent IN 1..100 OUTBOUND startObj GRAPH {DatabaseConstants.GraphParents}
						RETURN parent
				))
				
				FOR obj IN inheritanceChain
					FOR zone IN 1..100 OUTBOUND obj GRAPH {DatabaseConstants.GraphZones}
						LET zoneThing = FIRST(FOR v IN 1..1 INBOUND zone GRAPH {DatabaseConstants.GraphObjects} RETURN v)
						LET zoneObjId = zone._key
						LET zoneAttrs = (
							FOR v,e,p IN 1..maxDepth OUTBOUND zoneThing GRAPH {DatabaseConstants.GraphAttributes}
								PRUNE condition = NTH(attrPath, LENGTH(p.edges)-1) != v.Name
								FILTER !condition
								RETURN v
						)
						FILTER LENGTH(zoneAttrs) == maxDepth
						LIMIT 1
						RETURN {{
							attributes: zoneAttrs,
							sourceId: zoneObjId,
							source: 'Zone',
							filterFlags: true
						}}
			) : []
			
			LET allResults = APPEND([selfResult], APPEND(parentResults, zoneResults))
			LET filtered = (FOR r IN allResults FILTER r != null RETURN r)
			RETURN FIRST(filtered)
		";

		var bindVars = new Dictionary<string, object>
		{
			{ "attr", attribute },
			{ StartVertex, startVertex },
			{ "max", attribute.Length },
			{ "checkParent", checkParent }
		};

		var results = await arangoDb.Query.ExecuteAsync<QueryResult>(handle, query, bindVars, cancellationToken: ct);
		var result = results.FirstOrDefault();

		if (result == null || result.attributes == null)
		{
			yield break;
		}

		var sourceDbRef = ParseDbRefFromId(result.sourceId);

		var sourceType = result.source switch
		{
			"Self" => AttributeSource.Self,
			"Parent" => AttributeSource.Parent,
			"Zone" => AttributeSource.Zone,
			_ => throw new InvalidOperationException($"Unknown source type: {result.source}")
		};

		var attrs = new LazySharpAttribute[result.attributes.Count];
		for (int i = 0; i < result.attributes.Count; i++)
		{
			attrs[i] = await SharpAttributeQueryToLazySharpAttribute(result.attributes[i], ct);
		}

		var lastAttr = attrs.Last();
		var flags = result.filterFlags
			? lastAttr.Flags.Where(f => f.Inheritable)
			: lastAttr.Flags;

		yield return new LazyAttributeWithInheritance(attrs, sourceDbRef, sourceType, flags);
	}

	private async ValueTask<LazySharpAttribute> SharpAttributeQueryToLazySharpAttributeSimple(SharpAttributeQueryResult x,
		CancellationToken cancellationToken = default)
	{
		var flags = await GetAttributeFlagsAsync(x.Id, cancellationToken).ToArrayAsync(cancellationToken);

		return new LazySharpAttribute(
			x.Id,
			x.Key,
			x.Name,
			flags,
			null,
			x.LongName,
			new AsyncLazy<IAsyncEnumerable<LazySharpAttribute>>(ct => Task.FromResult(GetTopLevelLazyAttributesAsync(x.Id, ct))),
			new AsyncLazy<SharpPlayer?>(async ct => await GetAttributeOwnerAsync(x.Id, ct)),
			new AsyncLazy<SharpAttributeEntry?>(async ct => await GetRelatedAttributeEntry(x.Id, ct)),
			new AsyncLazy<MarkupStringModule.MarkupString>(ct =>
				Task.FromResult(MarkupStringModule.deserialize(x.Value))));
	}

	[GeneratedRegex(@"\*\*|[.*+?^${}()|[\]/]")]
	private static partial Regex WildcardToRegex();

	#endregion
}
