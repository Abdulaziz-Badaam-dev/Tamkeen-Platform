using System.ComponentModel.DataAnnotations;

namespace Tamkeen.API.Data.Entities;

public sealed class Track
{
    public int Id { get; set; }
    [MaxLength(32)] public required string Slug { get; set; }
    public required string TitleAr { get; set; }
    public required string TitleEn { get; set; }
    public required string DescriptionAr { get; set; }
    [MaxLength(16)] public required string Status { get; set; }
    public int DisplayOrder { get; set; }
    public List<TrackTask> Tasks { get; set; } = [];
}
