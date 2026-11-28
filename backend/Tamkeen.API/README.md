# واجهة تمكين الخلفية

يتطلب المشروع .NET 10 SDK. نفّذ الأوامر من مجلد `backend/Tamkeen.API`.

للتشغيل، وفّر اتصال SQL Server في `ConnectionStrings__DefaultConnection` ومفتاح توقيع عشوائي لا يقل عن 32 بايت في `Jwt__Key` عبر إعدادات البيئة الآمنة أو .NET User Secrets. يحتاج حساب قاعدة البيانات إلى صلاحيات تطبيق الترحيل الأولي. يطبّق التطبيق الترحيلات ويضيف المسارات والمهام الناقصة عند بدء التشغيل. تستخدم اختبارات التكامل قاعدة SQLite مؤقتة.

اضبط `Authentication__Google__ClientId` لدخول Google، واسمح للخادم بجلب مفاتيح التوقيع من `www.googleapis.com`. اضبط `Certificates__PublicBaseUrl` على عنوان API المنشور لتضمين رابط التحقق الصحيح في الشهادة. يقرأ CORS قائمة `Cors:Origins`، ويمكن ضبط أول عنوان عبر `Cors__Origins__0`. تدعم بيئة التطوير تلقائيًا المنفذين 5173 و3000 على localhost. تستخدم الشهادة QuestPDF برخصة Community وخط DejaVu Sans المضمّن. [عقد الواجهة الأمامية](../../docs/api-contract.md)

```sh
dotnet restore
dotnet build
dotnet test ../Tamkeen.API.Tests
dotnet run
```

يفتح ملف التشغيل المحلي واجهة Swagger على المسار `/swagger` في المنفذ `5088`. اضبط مفتاح `AiAgent:ApiKey` عبر .NET User Secrets أو متغير البيئة `AiAgent__ApiKey`. يحدّد `appsettings.json` النموذج الافتراضي `gemini-3.5-flash-lite` في `AiAgent:Model`، ويمكن تجاوزه بمتغير البيئة `AiAgent__Model`. مهلة طلب HTTP إلى Gemini هي 150 ثانية. يوضّح `appsettings.Example.json` أسماء الإعدادات. يحتاج اتصال Gemini إلى `generativelanguage.googleapis.com` ويحتاج جلب المستودعات إلى `api.github.com`. يمكن ضبط `GitHub__PersonalAccessToken` لزيادة حصة GitHub.

جرّب مهمة اصطناعية عبر `evaluate-files` برفع ملف UTF-8 وكتابة المتطلبات في `TaskDescription`. لمسار `evaluate-github` أرسل `repoUrl` و`branch` مع وصف المهمة. عند إعادة تسليم مهمة مسجّلة، يسترجع الوكيل ملاحظات آخر محاولة مرفوضة من قاعدة البيانات للمستخدم والمهمة نفسيهما. يشير `X-Submission-Truncated: true` إلى مراجعة جزئية. حدود مراجعة المستودعات هي 40 ملفًا و2 MiB من محتوى الملفات و25,000 حرف من النص المرقّم. يخضع كل طلب تقييم لحد زمني قدره ثلاث دقائق.
