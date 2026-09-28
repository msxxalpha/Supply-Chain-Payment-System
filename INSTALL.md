# راهنمای استقرار

## پیش‌نیازها

.NET 10 Runtime/SDK، SQL Server و در استقرار Windows در صورت نیاز IIS.

## پایگاه داده

برای نصب جدید، Database/001_initial.sql را روی SQL Server اجرا کنید و سپس connection string را برای محیط واقعی تنظیم کنید.

برای دیتابیسی که از نسخه قدیمی سامانه باقی مانده است، Database/002_upgrade_existing.sql را یک بار اجرا کنید. خود برنامه نیز در زمان شروع، ارتقاهای سازگار با نسخه جاری را به‌صورت خودکار کنترل و اعمال می‌کند.

## امنیت

InitialAdminPassword را در محیط واقعی به صورت Secret یا Environment Variable قرار دهید و مقدار واقعی آن را داخل مخزن commit نکنید. پیشنهاد می‌شود سامانه پشت HTTPS و شبکه داخلی سازمان منتشر شود.

## انتشار

    dotnet publish Indamin.Payment.Web.csproj -c Release -o ./publish

سپس خروجی publish را روی سرور قرار دهید و تنظیمات محیطی را اعمال کنید.
