namespace KittyClaw.Core.Models;

public record TicketSummary(
    int Id,
    string Title,
    string Description,
    string Status,
    TicketPriority Priority,
    int SortOrder,
    string? AssignedTo,
    string CreatedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    List<Label> Labels,
    int CommentCount,
    DateTime? LastActivityAt,
    int? ParentId,
    List<SubTicketInfo> SubTickets)
{
    public DateTime? DueDate { get; init; }
    public string? CliRuntimeId { get; init; }
    public string? CaoRoleId { get; init; }
    public string? ModelProfileId { get; init; }
    public string? RiskLevel { get; init; }
    public string? Reviewer { get; init; }
    public string? RequiredEvidence { get; init; }
}

public record SubTicketInfo(int Id, string Title, string Status, string? AssignedTo);
