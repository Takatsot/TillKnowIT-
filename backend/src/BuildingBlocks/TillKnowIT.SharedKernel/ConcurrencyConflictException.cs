namespace TillKnowIT.SharedKernel;

/// <summary>
/// Someone else changed the same record at the same moment. Stores throw this instead of the database library's own exception,
/// so application code can retry or answer "try again" without knowing which database is underneath.
/// </summary>
public sealed class ConcurrencyConflictException(string message, Exception? inner = null) : Exception(message, inner);
