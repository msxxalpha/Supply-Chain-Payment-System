-- Add maintenance mode fields to company settings.
-- Existing installations start with maintenance mode disabled.
IF COL_LENGTH(N'dbo.CompanySettings',N'MaintenanceMode') IS NULL
    ALTER TABLE dbo.CompanySettings ADD MaintenanceMode bit NOT NULL CONSTRAINT DF_CompanySettings_MaintenanceMode DEFAULT(0) WITH VALUES;
IF COL_LENGTH(N'dbo.CompanySettings',N'MaintenanceMessage') IS NULL
    ALTER TABLE dbo.CompanySettings ADD MaintenanceMessage nvarchar(1000) NOT NULL CONSTRAINT DF_CompanySettings_MaintenanceMessage DEFAULT(N'سامانه در حال انجام عملیات نگهداری و به‌روزرسانی است. پس از پایان عملیات، دسترسی مجدداً برقرار خواهد شد.') WITH VALUES;
