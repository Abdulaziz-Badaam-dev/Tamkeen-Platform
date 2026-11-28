using Microsoft.EntityFrameworkCore;
using Tamkeen.API.Data.Entities;

namespace Tamkeen.API.Data;

public static class DbSeeder
{
    // Migrate and EnsureCreated invoke UseSeeding under EF's migration lock.
    public static void Seed(TamkeenDb db)
    {
        Track Track(string slug, string ar, string en, string description, string status, int order)
        {
            var existing = db.Tracks.Include(t => t.Tasks).SingleOrDefault(t => t.Slug == slug);
            if (existing != null) return existing;
            var track = new Track { Slug = slug, TitleAr = ar, TitleEn = en, DescriptionAr = description, Status = status, DisplayOrder = order };
            db.Tracks.Add(track);
            return track;
        }
        var backend = Track("backend", "تطوير الواجهات الخلفية", "Backend", "طوّر خدمات آمنة واختبر قراراتك البرمجية.", "Active", 1);
        Track("frontend", "تطوير الواجهات الأمامية", "Frontend", "ابنِ واجهات تفاعلية ميسرة.", "ComingSoon", 2);
        Track("full-stack", "تطوير الويب المتكامل", "Full Stack", "اربط تجربة المستخدم بخدمات الويب.", "ComingSoon", 3);
        void Task(int order, string title, string description, string requirements, bool weekly = false)
        {
            if (backend.Tasks.Any(t => t.OrderIndex == order)) return;
            backend.Tasks.Add(new TrackTask { OrderIndex = order, TitleAr = title, DescriptionAr = description,
                RequirementsAr = requirements, TaskType = weekly ? "Weekly" : "Daily", DurationHours = weekly ? 168 : 24,
                AllowedSubmissionMode = weekly ? "GitHubOrFiles" : "Files" });
        }
        Task(1, "خدمة دخول آمنة", "صمّم خدمة تتحقق من بيانات الدخول بأمان.",
            "تحقق من المدخلات واستخدم خوارزمية مخصصة لتجزئة كلمات المرور مع ملح مستقل. استخدم استعلامات ذات معاملات ورسالة عامة عند فشل الدخول.");
        Task(2, "واجهة إدارة المهام", "أنشئ نقاط REST لإدارة مهام فريق صغير.",
            "نفّذ الإنشاء والقراءة والتعديل والحذف. تحقّق من العنوان والحالة، وافصل الوصول للبيانات وأعد رموز HTTP ملائمة للمدخلات الخاطئة والسجلات المفقودة.");
        Task(3, "مشروع إدارة مكتبة", "ابنِ واجهة خلفية مصغرة لإدارة الكتب والإعارة.",
            "قسّم المشروع إلى طبقات، واحفظ الكتب والإعارات في قاعدة بيانات. تحقّق من توفر الكتاب قبل الإعارة ومن صحة المدخلات واستخدم استعلامات آمنة.", true);
        db.SaveChanges();
    }
}
