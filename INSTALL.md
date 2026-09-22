# راهنمای استقرار

## پیش‌نیازها

.NET 10 Runtime/SDK، SQL Server و در استقرار Windows در صورت نیاز IIS.

## پایگاه داده

Database/001_initial.sql را اجرا کنید، سپس connection string را برای محیط واقعی تنظیم کنید.

## امنیت

InitialAdminPassword را در محیط واقعی به صورت Secret یا Environment Variable قرار دهید و مقدار واقعی آن را داخل مخزن commit نکنید. پیشنهاد می‌شود سامانه پشت HTTPS و شبکه داخلی سازمان منتشر شود.

## انتشار

    dotnet publish Indamin.Payment.Web.csproj -c Release -o ./publish

سپس خروجی publish را روی سرور قرار دهید و تنظیمات محیطی را اعمال کنید.
