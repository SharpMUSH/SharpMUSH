using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// One page of an attribute index query (<c>indexed()</c>). Not cached: the index changes with every write of the
/// attribute, and a page is read once.
/// </summary>
public record GetIndexedAttributeQuery(IndexedAttributeQuerySpec Spec) : IQuery<IndexedAttributeResult>;

/// <summary>
/// How many objects an attribute index lists under one value, every one counted whoever may see it (what
/// <c>indexed(..., count, ...)</c> gives a wizard). Not cached, for the same reason.
/// </summary>
public record CountIndexedAttributeQuery(string Name, string Value) : IQuery<IndexedAttributeCount>;

/// <summary>
/// How many objects (of <paramref name="Type"/>, or every type when null) hold their own copy of an attribute: what
/// an index on it would be built over. Not cached.
/// </summary>
public record CountAttributeCopiesQuery(string Name, string? Type) : IQuery<long>;
