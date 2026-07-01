using KittyClaw.Core.Models;
using Microsoft.Extensions.Logging;

namespace KittyClaw.Core.Services;

/// <summary>
/// Service for recursive card decomposition.
/// Handles splitting tickets into child cards, preview/apply workflow,
/// and container/executable mode transitions.
/// </summary>
public sealed class DecompositionService
{
    private readonly TreeService _treeService;
    private readonly TicketService _tickets;
    private readonly ProjectService _projects;
    private readonly ILogger? _logger;

    public const int RecommendedMaxDepth = 3;
    public const int HardMaxDepth = 5;

    public DecompositionService(
        TreeService treeService,
        TicketService tickets,
        ProjectService projects,
        ILogger? logger = null)
    {
        _treeService = treeService;
        _tickets = tickets;
        _projects = projects;
        _logger = logger;
    }

    /// <summary>
    /// Check if a ticket can be decomposed.
    /// </summary>
    public async Task<(bool CanDecompose, string? Reason)> CanDecomposeAsync(
        string projectSlug, int ticketId, CancellationToken ct = default)
    {
        var ticket = await _tickets.GetTicketAsync(projectSlug, ticketId);
        if (ticket is null) return (false, "Ticket not found");

        // Check if Done
        if (ticket.Status == "Done")
            return (false, "Cannot decompose a completed ticket");

        // Check if has active run
        if (ticket.SubtreeHasActiveRun)
            return (false, "Ticket has an active run. Stop the run first.");

        // Check if blocked
        if (ticket.Status == "Blocked")
            return (false, "Ticket is blocked. Resolve the blocker first.");

        // Check depth
        if (ticket.Depth >= HardMaxDepth)
            return (false, $"Max depth ({HardMaxDepth}) exceeded. Use checklist items instead.");

        if (ticket.Depth >= RecommendedMaxDepth)
            return (true, $"Warning: depth {ticket.Depth + 1} exceeds recommended max ({RecommendedMaxDepth}). Consider using checklist items.");

        return (true, null);
    }

    /// <summary>
    /// Preview decomposition - generate child cards without applying.
    /// </summary>
    public async Task<DecompositionPreview> PreviewDecompositionAsync(
        string projectSlug,
        int ticketId,
        DecompositionRequest request,
        CancellationToken ct = default)
    {
        var (canDecompose, reason) = await CanDecomposeAsync(projectSlug, ticketId, ct);
        if (!canDecompose)
        {
            return new DecompositionPreview
            {
                TicketId = ticketId,
                IsValid = false,
                RejectionReason = reason
            };
        }

        var ticket = await _tickets.GetTicketAsync(projectSlug, ticketId);
        if (ticket is null)
        {
            return new DecompositionPreview
            {
                TicketId = ticketId,
                IsValid = false,
                RejectionReason = "Ticket not found"
            };
        }

        var preview = new DecompositionPreview
        {
            TicketId = ticketId,
            ParentTitle = ticket.Title,
            CurrentDepth = ticket.Depth,
            NewDepth = ticket.Depth + 1,
            IsValid = true,
            Children = request.Children ?? new List<DecompositionChild>(),
            ParallelGroups = request.ParallelGroups ?? new List<DecompositionParallelGroup>(),
            Warnings = new List<string>()
        };

        // Add depth warning
        if (ticket.Depth + 1 > RecommendedMaxDepth)
        {
            preview.Warnings.Add($"Depth {ticket.Depth + 1} exceeds recommended max ({RecommendedMaxDepth}). Consider using checklist items.");
        }

        // Validate children don't create cycles
        foreach (var child in preview.Children)
        {
            if (child.Title == ticket.Title)
            {
                preview.Warnings.Add($"Child '{child.Title}' has same title as parent. Consider renaming.");
            }
        }

        // Check if decomposition is too small
        if (preview.Children.Count <= 1)
        {
            preview.Warnings.Add("Only 1 child generated. This task may be too small to decompose.");
        }

        return preview;
    }

    /// <summary>
    /// Apply decomposition - create children and convert parent to container.
    /// </summary>
    public async Task<DecompositionResult> ApplyDecompositionAsync(
        string projectSlug,
        int ticketId,
        DecompositionPreview preview,
        string? decomposedBy = null,
        CancellationToken ct = default)
    {
        if (!preview.IsValid)
        {
            return new DecompositionResult
            {
                Success = false,
                Error = preview.RejectionReason
            };
        }

        var ticket = await _tickets.GetTicketAsync(projectSlug, ticketId);
        if (ticket is null)
        {
            return new DecompositionResult
            {
                Success = false,
                Error = "Ticket not found"
            };
        }

        var createdChildren = new List<int>();

        try
        {
            // Create child tickets
            int order = 0;
            foreach (var childSpec in preview.Children)
            {
                var childTicket = await _tickets.CreateTicketAsync(
                    projectSlug,
                    childSpec.Title,
                    childSpec.Description ?? "",
                    decomposedBy ?? "system",
                    "Backlog",
                    null, // labels
                    TicketPriority.NiceToHave,
                    null, // assignee
                    ticketId, // parentId
                    null, null, null, null, null, null, null, null, null, null, true, null);

                if (childTicket is not null)
                {
                    // Update child with decomposition-specific fields
                    await using var db = _projects.GetProjectDb(projectSlug);
                    var dbTicket = await db.Tickets.FindAsync(childTicket.Id);
                    if (dbTicket is not null)
                    {
                        dbTicket.Kind = childSpec.Kind ?? "subtask";
                        dbTicket.ExecutionRole = childSpec.AgentRole ?? "worker";
                        dbTicket.ExecutionMode = childSpec.ExecutionMode ?? "manual";
                        dbTicket.TreeOrder = order++;
                        dbTicket.IsOptional = childSpec.IsOptional;
                        await db.SaveChangesAsync(ct);
                    }

                    createdChildren.Add(childTicket.Id);
                }
            }

            // Convert parent to container
            await using var parentDb = _projects.GetProjectDb(projectSlug);
            var parentTicket = await parentDb.Tickets.FindAsync(ticketId);
            if (parentTicket is not null)
            {
                parentTicket.ContainerMode = "container";
                parentTicket.CompletionPolicy = "all_required_leaf_tasks_done";
                parentTicket.SplitOrigin = preview.SplitOrigin ?? "manual";
                parentTicket.DecomposedBy = decomposedBy;
                parentTicket.DecomposedAt = DateTime.UtcNow;
                parentTicket.HasChildren = true;
                parentTicket.ChildCount = createdChildren.Count;
                await parentDb.SaveChangesAsync(ct);
            }

            // Update tree counts
            await _treeService.ReparentAsync(projectSlug, ticketId, ticket.ParentId, ct);

            _logger?.LogInformation(
                "Decomposed ticket {Id} into {Count} children. Parent converted to container.",
                ticketId, createdChildren.Count);

            return new DecompositionResult
            {
                Success = true,
                CreatedChildIds = createdChildren,
                ParentConvertedToContainer = true
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to decompose ticket {Id}", ticketId);
            return new DecompositionResult
            {
                Success = false,
                Error = $"Decomposition failed: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Get leaf tasks for a ticket (recursive).
    /// </summary>
    public async Task<List<Ticket>> GetLeafTasksAsync(string projectSlug, int ticketId, CancellationToken ct = default)
    {
        var subtree = await _treeService.GetSubtreeAsync(projectSlug, ticketId, ct);
        var ticket = await _tickets.GetTicketAsync(projectSlug, ticketId);
        
        if (ticket is null) return new List<Ticket>();

        // Leaf tasks are those without children in the subtree
        var ticketsWithChildren = subtree.Where(t => t.HasChildren).Select(t => t.Id).ToHashSet();
        var leafTasks = subtree.Where(t => !ticketsWithChildren.Contains(t.Id)).ToList();
        
        // Also include the ticket itself if it's a leaf (no children at all)
        if (!ticket.HasChildren)
        {
            leafTasks.Insert(0, ticket);
        }

        return leafTasks;
    }

    /// <summary>
    /// Calculate leaf task progress for a container.
    /// </summary>
    public async Task<LeafProgress> CalculateLeafProgressAsync(string projectSlug, int ticketId, CancellationToken ct = default)
    {
        var leafTasks = await GetLeafTasksAsync(projectSlug, ticketId, ct);
        
        return new LeafProgress
        {
            TotalLeaves = leafTasks.Count,
            DoneLeaves = leafTasks.Count(t => t.Status == "Done"),
            RunningLeaves = leafTasks.Count(t => t.Status == "InProgress"),
            BlockedLeaves = leafTasks.Count(t => t.Status == "Blocked"),
            FailedLeaves = leafTasks.Count(t => t.Status == "Failed"),
            WaitingLeaves = leafTasks.Count(t => t.Status is "Backlog" or "Ready"),
            OptionalLeaves = leafTasks.Count(t => t.IsOptional),
            RequiredDone = leafTasks.Count(t => t.Status == "Done" && !t.IsOptional),
            RequiredTotal = leafTasks.Count(t => !t.IsOptional)
        };
    }
}

/// <summary>
/// Decomposition request from user or planner.
/// </summary>
public sealed class DecompositionRequest
{
    public List<DecompositionChild> Children { get; set; } = new();
    public List<DecompositionParallelGroup> ParallelGroups { get; set; } = new();
    public string? SplitOrigin { get; set; }
}

/// <summary>
/// Specification for a child card to be created.
/// </summary>
public sealed class DecompositionChild
{
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? Kind { get; set; } = "subtask";
    public string? AgentRole { get; set; } = "worker";
    public string? ExecutionMode { get; set; } = "manual";
    public bool IsOptional { get; set; }
    public List<string> DependsOn { get; set; } = new();
    public List<string> EvidenceRequired { get; set; } = new();
}

/// <summary>
/// Parallel group specification for decomposition.
/// </summary>
public sealed class DecompositionParallelGroup
{
    public required string Name { get; set; }
    public List<string> ChildTitles { get; set; } = new();
    public string JoinPolicy { get; set; } = "all_done";
    public int MaxConcurrency { get; set; } = 2;
}

/// <summary>
/// Preview of decomposition before applying.
/// </summary>
public sealed class DecompositionPreview
{
    public int TicketId { get; set; }
    public string? ParentTitle { get; set; }
    public int CurrentDepth { get; set; }
    public int NewDepth { get; set; }
    public bool IsValid { get; set; }
    public string? RejectionReason { get; set; }
    public string? SplitOrigin { get; set; }
    public List<DecompositionChild> Children { get; set; } = new();
    public List<DecompositionParallelGroup> ParallelGroups { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Result of applying decomposition.
/// </summary>
public sealed class DecompositionResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<int> CreatedChildIds { get; set; } = new();
    public bool ParentConvertedToContainer { get; set; }
}

/// <summary>
/// Leaf task progress summary.
/// </summary>
public sealed class LeafProgress
{
    public int TotalLeaves { get; set; }
    public int DoneLeaves { get; set; }
    public int RunningLeaves { get; set; }
    public int BlockedLeaves { get; set; }
    public int FailedLeaves { get; set; }
    public int WaitingLeaves { get; set; }
    public int OptionalLeaves { get; set; }
    public int RequiredDone { get; set; }
    public int RequiredTotal { get; set; }
    public double ProgressPercent => RequiredTotal > 0 ? (double)RequiredDone / RequiredTotal * 100 : 0;
    public bool AllRequiredDone => RequiredDone >= RequiredTotal;
}
