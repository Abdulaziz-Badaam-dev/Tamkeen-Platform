using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace Tamkeen.API.Data.Entities;

public sealed class ApplicationUser : IdentityUser
{
    [MaxLength(160)] public required string FullName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(255)] public string? GoogleSubjectId { get; set; }
}
