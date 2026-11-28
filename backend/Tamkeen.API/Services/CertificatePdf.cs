using System.Globalization;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Tamkeen.API.Data.Entities;

namespace Tamkeen.API.Services;

public sealed class CertificatePdf
{
    private const string Font = "DejaVu Sans";
    public CertificatePdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        using var stream = typeof(CertificatePdf).Assembly.GetManifestResourceStream("Tamkeen.CertificateFont")!;
        FontManager.RegisterFontFromStream(stream);
    }

    public byte[] Generate(Certificate certificate, string verificationUrl) => Document.Create(doc =>
        doc.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(28);
            page.DefaultTextStyle(s => s.FontFamily(Font).FontSize(15).FontColor("#153E42"));
            page.Content().Border(2).BorderColor("#43756D").Padding(25).ContentFromRightToLeft().Column(column =>
            {
                column.Spacing(10);
                column.Item().AlignCenter().Text("تمكين").FontSize(30);
                column.Item().AlignCenter().Text("شهادة إتمام المسار").FontSize(24);
                column.Item().PaddingTop(10).AlignCenter().Text("تمنح هذه الشهادة للمتدرّب");
                column.Item().AlignCenter().Text(certificate.TraineeName).FontSize(22);
                column.Item().AlignCenter().Text("لإتمام مسار " + certificate.TrackTitleAr);
                column.Item().AlignCenter().Text("الدرجة النهائية " + certificate.FinalOverallScore.ToString("0.##", CultureInfo.InvariantCulture) + " من 100");
                column.Item().AlignCenter().Text("تاريخ الإصدار " + certificate.IssuedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                column.Item().ContentFromLeftToRight().AlignCenter().Text(certificate.SerialNumber).FontSize(13);
                column.Item().AlignCenter().Text("التحقق من الشهادة").FontSize(12);
                column.Item().ContentFromLeftToRight().AlignCenter().Hyperlink(verificationUrl).Text(verificationUrl).FontSize(10);
            });
        })).GeneratePdf();
}
