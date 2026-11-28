using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tamkeen.API.Data;
using Tamkeen.API.Data.Entities;
using Tamkeen.API.Services;
using Tamkeen.API.Services.Auth;

namespace Tamkeen.API.Controllers;

[ApiController, Route("api/certificates")]
public sealed class CertificatesController(TamkeenDb db, IConfiguration config, IWebHostEnvironment env) : ControllerBase
{
    private static object Details(Certificate c) => new { c.SerialNumber, c.TraineeName, c.TrackTitleAr, c.FinalOverallScore, c.IssuedAt };

    [Authorize, HttpGet("my")]
    public async Task<IActionResult> My(CancellationToken ct)
    {
        var id = User.UserId();
        var rows = await db.Certificates.AsNoTracking().Include(c => c.Enrollment)
            .Where(c => c.UserId == id).OrderByDescending(c => c.Id).ToListAsync(ct);
        return Ok(rows.Select(c => new { certificate = Details(c), finalEvaluation = SubmissionReport.Final(c.Enrollment) }));
    }

    [AllowAnonymous, HttpGet("verify/{serialNumber}")]
    public async Task<IActionResult> Verify(string serialNumber, CancellationToken ct)
    {
        var c = await db.Certificates.AsNoTracking().SingleOrDefaultAsync(c => c.SerialNumber == serialNumber, ct);
        return c == null ? NotFound() : Ok(new { authentic = true, certificate = Details(c) });
    }

    [Authorize, HttpGet("{serialNumber}/pdf")]
    public async Task<IActionResult> Pdf(string serialNumber, [FromServices] CertificatePdf pdf, CancellationToken ct)
    {
        var id = User.UserId();
        var c = await db.Certificates.AsNoTracking().SingleOrDefaultAsync(c => c.SerialNumber == serialNumber && c.UserId == id, ct);
        if (c == null) return NotFound();
        string? origin = config["Certificates:PublicBaseUrl"] ?? (env.IsDevelopment() ? "http://localhost:5088" : null);
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return Problem(statusCode: 503, detail: "Configure Certificates:PublicBaseUrl.");
        return File(pdf.Generate(c, origin!.TrimEnd('/') + "/api/certificates/verify/" + c.SerialNumber),
            "application/pdf", c.SerialNumber + ".pdf");
    }
}
