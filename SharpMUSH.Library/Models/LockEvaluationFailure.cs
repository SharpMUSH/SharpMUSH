namespace SharpMUSH.Library.Models;

/// <summary>
/// An eval lock's attribute could not be evaluated. Distinct from a value that merely fails to match:
/// both deny, but only this one says the game is broken.
/// </summary>
public readonly record struct LockEvaluationFailure(string AttributeName, string Reason);
