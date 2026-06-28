using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace KittyClaw.Core.Automation;

/// <summary>
/// In-memory store for FailureLogEntry records.
/// Acts as the backend for the failure logbook.
/// </summary>
public sealed class FailureLogStore
{
    private readonly ConcurrentDictionary<string, FailureLogEntry> _entries = new();
    private readonly ILogger? _logger;

    public FailureLogStore(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Record a failure for a ticket. Idempotent if the same (ticketId, kind, createdAt) entry already exists.
    /// </summary>
    public FailureLogEntry Record(FailureLogEntry entry)
    {
        _entries[entry.Id] = entry;
        _logger?.LogInformation("[FAILURE] ticket #{TicketId} [{Kind}]: {Message}",
            entry.TicketId, entry.Kind, entry.Message);
        return entry;
    }

    /// <summary>
    /// Get all entries for a ticket, newest first.
    /// </summary>
    public IReadOnlyList<FailureLogEntry> ForTicket(string projectSlug, int ticketId) =>
        _entries.Values
            .Where(e => e.ProjectSlug == projectSlug && e.TicketId == ticketId)
            .OrderByDescending(e => e.CreatedAt)
            .ToList();

    /// <summary>
    /// Get all unresolved entries for a ticket.
    /// </summary>
    public IReadOnlyList<FailureLogEntry> UnresolvedForTicket(string projectSlug, int ticketId) =>
        ForTicket(projectSlug, ticketId).Where(e => !e.Resolved).ToList();

    /// <summary>
    /// Get all unresolved entries for a project, newest first.
    /// </summary>
    public IReadOnlyList<FailureLogEntry> UnresolvedForProject(string projectSlug) =>
        _entries.Values
            .Where(e => e.ProjectSlug == projectSlug && !e.Resolved)
            .OrderByDescending(e => e.CreatedAt)
            .ToList();

    /// <summary>
    /// Get all entries for a project, newest first.
    /// </summary>
    public IReadOnlyList<FailureLogEntry> ForProject(string projectSlug) =>
        _entries.Values
            .Where(e => e.ProjectSlug == projectSlug)
            .OrderByDescending(e => e.CreatedAt)
            .ToList();

    /// <summary>
    /// Mark an entry as resolved.
    /// </summary>
    public bool Resolve(string entryId)
    {
        if (_entries.TryGetValue(entryId, out var entry))
        {
            entry.Resolved = true;
            entry.ResolvedAt = DateTimeOffset.UtcNow;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Delete all entries for a ticket.
    /// </summary>
    public void ClearForTicket(string projectSlug, int ticketId)
    {
        var ids = _entries.Values
            .Where(e => e.ProjectSlug == projectSlug && e.TicketId == ticketId)
            .Select(e => e.Id)
            .ToList();
        foreach (var id in ids)
            _entries.TryRemove(id, out _);
    }

    /// <summary>
    /// Get the most recent unresolved failure for a ticket, if any.
    /// </summary>
    public FailureLogEntry? LatestUnresolved(string projectSlug, int ticketId) =>
        UnresolvedForTicket(projectSlug, ticketId).FirstOrDefault();
}