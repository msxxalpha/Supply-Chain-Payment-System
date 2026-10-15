-- افزوده‌شدن مشارکت پارامتر در توصیه پرداخت غیرمحاسباتی
-- امن برای پایگاه‌های موجود؛ امتیازها و تاریخچه قبلی دست‌نخورده می‌مانند.
IF COL_LENGTH(N'dbo.PaymentParameters',N'ParticipatesInNonCalculatedRecommendation') IS NULL
BEGIN
 ALTER TABLE dbo.PaymentParameters ADD ParticipatesInNonCalculatedRecommendation bit NOT NULL
 CONSTRAINT DF_PaymentParameters_ParticipatesInNonCalculatedRecommendation DEFAULT(0) WITH VALUES;
END;
