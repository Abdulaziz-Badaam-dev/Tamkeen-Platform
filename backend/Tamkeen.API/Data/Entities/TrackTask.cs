namespace Tamkeen.API.Data.Entities;

public sealed class TrackTask
{
    public int Id { get; set; }
    public int TrackId { get; set; }
    public int OrderIndex { get; set; }
    public required string TitleAr { get; set; }
    public required string DescriptionAr { get; set; }
    public required string RequirementsAr { get; set; }
    public required string TaskType { get; set; }
    public int DurationHours { get; set; }
    public required string AllowedSubmissionMode { get; set; }
}
