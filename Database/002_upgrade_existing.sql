/*
  Upgrade an existing IndaminSupplyChainPayment database to the current application schema.
  Use this script only for an existing database that was created from an older version.
  A fresh installation should continue to use Database/001_initial.sql.
*/
USE [IndaminSupplyChainPayment];
GO

/* Supplier initial claim */
IF COL_LENGTH(N'dbo.Suppliers',N'InitialClaimAmount') IS NULL
    ALTER TABLE dbo.Suppliers ADD InitialClaimAmount decimal(20,2) NOT NULL
        CONSTRAINT DF_Suppliers_InitialClaimAmount DEFAULT(0);
GO

/* Payment parameter snapshots and invoice/supplier-evaluation extensions */
IF OBJECT_ID(N'dbo.PaymentRunParameterSnapshots',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PaymentRunParameterSnapshots(
        Id bigint IDENTITY PRIMARY KEY,
        PaymentRunId int NOT NULL,
        PaymentParameterId int NOT NULL,
        Code nvarchar(100) NOT NULL,
        Title nvarchar(300) NOT NULL,
        Type int NOT NULL,
        Weight decimal(8,4) NOT NULL,
        MaxScore decimal(8,2) NOT NULL,
        ScoringGuide nvarchar(max) NOT NULL,
        ScoringMethod int NOT NULL,
        SortOrder int NOT NULL,
        CONSTRAINT FK_PaymentRunParameterSnapshots_Run FOREIGN KEY(PaymentRunId)
            REFERENCES dbo.PaymentRuns(Id) ON DELETE CASCADE
    );
END;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_PaymentRunParameterSnapshots_Key'
    AND object_id=OBJECT_ID(N'dbo.PaymentRunParameterSnapshots'))
    CREATE UNIQUE INDEX UX_PaymentRunParameterSnapshots_Key
    ON dbo.PaymentRunParameterSnapshots(PaymentRunId,PaymentParameterId);
GO

IF COL_LENGTH(N'dbo.PaymentRunInvoices',N'SupplyCapacity') IS NULL
    ALTER TABLE dbo.PaymentRunInvoices ADD SupplyCapacity decimal(18,2) NOT NULL CONSTRAINT DF_PaymentRunInvoices_SupplyCapacity DEFAULT(0);
IF COL_LENGTH(N'dbo.PaymentRunInvoices',N'SupplierInitialClaimAmount') IS NULL
    ALTER TABLE dbo.PaymentRunInvoices ADD SupplierInitialClaimAmount decimal(20,2) NOT NULL CONSTRAINT DF_PaymentRunInvoices_SupplierInitialClaimAmount DEFAULT(0);
IF COL_LENGTH(N'dbo.PaymentRunInvoices',N'SupplierOutstandingDebt') IS NULL
    ALTER TABLE dbo.PaymentRunInvoices ADD SupplierOutstandingDebt decimal(20,2) NOT NULL CONSTRAINT DF_PaymentRunInvoices_SupplierOutstandingDebt DEFAULT(0);
IF COL_LENGTH(N'dbo.PaymentRunInvoices',N'CalculatedAllocatedAmount') IS NULL
    ALTER TABLE dbo.PaymentRunInvoices ADD CalculatedAllocatedAmount decimal(20,2) NOT NULL CONSTRAINT DF_PaymentRunInvoices_CalculatedAllocatedAmount DEFAULT(0);
IF COL_LENGTH(N'dbo.PaymentRunInvoices',N'CalculatedCurrentAllocatedAmount') IS NULL
    ALTER TABLE dbo.PaymentRunInvoices ADD CalculatedCurrentAllocatedAmount decimal(20,2) NOT NULL CONSTRAINT DF_PaymentRunInvoices_CalculatedCurrentAllocatedAmount DEFAULT(0);
IF COL_LENGTH(N'dbo.PaymentRunInvoices',N'CalculatedInitialClaimAllocatedAmount') IS NULL
    ALTER TABLE dbo.PaymentRunInvoices ADD CalculatedInitialClaimAllocatedAmount decimal(20,2) NOT NULL CONSTRAINT DF_PaymentRunInvoices_CalculatedInitialClaimAllocatedAmount DEFAULT(0);
IF COL_LENGTH(N'dbo.PaymentRunInvoices',N'AllocatedCurrentAmount') IS NULL
    ALTER TABLE dbo.PaymentRunInvoices ADD AllocatedCurrentAmount decimal(20,2) NOT NULL CONSTRAINT DF_PaymentRunInvoices_AllocatedCurrentAmount DEFAULT(0);
IF COL_LENGTH(N'dbo.PaymentRunInvoices',N'AllocatedInitialClaimAmount') IS NULL
    ALTER TABLE dbo.PaymentRunInvoices ADD AllocatedInitialClaimAmount decimal(20,2) NOT NULL CONSTRAINT DF_PaymentRunInvoices_AllocatedInitialClaimAmount DEFAULT(0);
IF COL_LENGTH(N'dbo.PaymentInvoiceScores',N'ParameterCodeSnapshot') IS NULL
    ALTER TABLE dbo.PaymentInvoiceScores ADD ParameterCodeSnapshot nvarchar(100) NOT NULL CONSTRAINT DF_PaymentInvoiceScores_ParameterCodeSnapshot DEFAULT('');
IF COL_LENGTH(N'dbo.PaymentInvoiceScores',N'ParameterTypeSnapshot') IS NULL
    ALTER TABLE dbo.PaymentInvoiceScores ADD ParameterTypeSnapshot int NOT NULL CONSTRAINT DF_PaymentInvoiceScores_ParameterTypeSnapshot DEFAULT(0);
IF COL_LENGTH(N'dbo.PaymentInvoiceScores',N'MaxScoreSnapshot') IS NULL
    ALTER TABLE dbo.PaymentInvoiceScores ADD MaxScoreSnapshot decimal(8,2) NOT NULL CONSTRAINT DF_PaymentInvoiceScores_MaxScoreSnapshot DEFAULT(5);
IF COL_LENGTH(N'dbo.PaymentInvoiceScores',N'ScoringGuideSnapshot') IS NULL
    ALTER TABLE dbo.PaymentInvoiceScores ADD ScoringGuideSnapshot nvarchar(max) NOT NULL CONSTRAINT DF_PaymentInvoiceScores_ScoringGuideSnapshot DEFAULT('');
IF COL_LENGTH(N'dbo.PaymentInvoiceScores',N'ScoringMethodSnapshot') IS NULL
    ALTER TABLE dbo.PaymentInvoiceScores ADD ScoringMethodSnapshot int NOT NULL CONSTRAINT DF_PaymentInvoiceScores_ScoringMethodSnapshot DEFAULT(0);
IF COL_LENGTH(N'dbo.SupplierPartEvaluations',N'IsActive') IS NULL
    ALTER TABLE dbo.SupplierPartEvaluations ADD IsActive bit NOT NULL CONSTRAINT DF_SupplierPartEvaluations_IsActive DEFAULT(1);
GO

/* Advanced payment schema */
IF OBJECT_ID(N'dbo.SystemParameters',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SystemParameters(
        Id int IDENTITY PRIMARY KEY,
        Code nvarchar(100) NOT NULL,
        Title nvarchar(300) NOT NULL,
        ValueType int NOT NULL,
        Value decimal(20,2) NOT NULL,
        Unit nvarchar(50) NOT NULL DEFAULT(N''),
        SortOrder int NOT NULL DEFAULT(1),
        IsActive bit NOT NULL DEFAULT(1),
        UpdatedAt datetime2 NOT NULL DEFAULT(sysutcdatetime())
    );
END;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_SystemParameters_Code' AND object_id=OBJECT_ID(N'dbo.SystemParameters'))
    CREATE UNIQUE INDEX UX_SystemParameters_Code ON dbo.SystemParameters(Code);
GO

IF OBJECT_ID(N'dbo.PaymentRunSystemParameterSnapshots',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PaymentRunSystemParameterSnapshots(
        Id bigint IDENTITY PRIMARY KEY,
        PaymentRunId int NOT NULL,
        Code nvarchar(100) NOT NULL,
        Title nvarchar(300) NOT NULL,
        ValueType int NOT NULL,
        Value decimal(20,2) NOT NULL,
        Unit nvarchar(50) NOT NULL DEFAULT(N''),
        CONSTRAINT FK_PaymentRunSystemParameterSnapshots_Run FOREIGN KEY(PaymentRunId)
            REFERENCES dbo.PaymentRuns(Id) ON DELETE CASCADE
    );
END;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_PaymentRunSystemParameterSnapshots_Key'
    AND object_id=OBJECT_ID(N'dbo.PaymentRunSystemParameterSnapshots'))
    CREATE UNIQUE INDEX UX_PaymentRunSystemParameterSnapshots_Key
    ON dbo.PaymentRunSystemParameterSnapshots(PaymentRunId,Code);
GO

IF OBJECT_ID(N'dbo.NonCalculatedPaymentLineSnapshots',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.NonCalculatedPaymentLineSnapshots(
        Id bigint IDENTITY PRIMARY KEY,
        PaymentRunId int NOT NULL,
        LineNumber int NOT NULL,
        SupplierId int NOT NULL,
        SupplierTitleSnapshot nvarchar(300) NOT NULL,
        PaymentType int NOT NULL,
        RequestedAmount decimal(20,2) NOT NULL,
        Description nvarchar(2000) NOT NULL DEFAULT(N''),
        SupplierInitialClaimBefore decimal(20,2) NOT NULL,
        SupplierCurrentDebtBefore decimal(20,2) NOT NULL,
        SupplierTotalDebtBefore decimal(20,2) NOT NULL,
        InitialClaimAllocatedAmount decimal(20,2) NOT NULL,
        CurrentClaimAllocatedAmount decimal(20,2) NOT NULL,
        AllocatedAmount decimal(20,2) NOT NULL,
        PaymentDateJalali nvarchar(20) NOT NULL,
        PaymentDate datetime2 NOT NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(sysutcdatetime()),
        CONSTRAINT FK_NonCalculatedPaymentLineSnapshots_Run FOREIGN KEY(PaymentRunId)
            REFERENCES dbo.PaymentRuns(Id) ON DELETE CASCADE,
        CONSTRAINT FK_NonCalculatedPaymentLineSnapshots_Supplier FOREIGN KEY(SupplierId)
            REFERENCES dbo.Suppliers(Id)
    );
END;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_NonCalculatedPaymentLineSnapshots_Run'
    AND object_id=OBJECT_ID(N'dbo.NonCalculatedPaymentLineSnapshots'))
    CREATE INDEX IX_NonCalculatedPaymentLineSnapshots_Run
    ON dbo.NonCalculatedPaymentLineSnapshots(PaymentRunId,LineNumber);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_NonCalculatedPaymentLineSnapshots_Supplier'
    AND object_id=OBJECT_ID(N'dbo.NonCalculatedPaymentLineSnapshots'))
    CREATE INDEX IX_NonCalculatedPaymentLineSnapshots_Supplier
    ON dbo.NonCalculatedPaymentLineSnapshots(SupplierId);
GO

IF OBJECT_ID(N'dbo.SupplierClaimHistories',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SupplierClaimHistories(
        Id bigint IDENTITY PRIMARY KEY,
        SupplierId int NOT NULL,
        ClaimType int NOT NULL,
        AmountBefore decimal(20,2) NOT NULL,
        AmountChange decimal(20,2) NOT NULL,
        AmountAfter decimal(20,2) NOT NULL,
        PaymentRunId int NULL,
        Reference nvarchar(500) NOT NULL DEFAULT(N''),
        EffectiveDateJalali nvarchar(20) NOT NULL DEFAULT(N''),
        UserId int NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(sysutcdatetime()),
        CONSTRAINT FK_SupplierClaimHistories_Supplier FOREIGN KEY(SupplierId)
            REFERENCES dbo.Suppliers(Id),
        CONSTRAINT FK_SupplierClaimHistories_Run FOREIGN KEY(PaymentRunId)
            REFERENCES dbo.PaymentRuns(Id) ON DELETE SET NULL,
        CONSTRAINT FK_SupplierClaimHistories_User FOREIGN KEY(UserId)
            REFERENCES dbo.AppUsers(Id)
    );
END;
GO

/* Payment run lifecycle: add first, then run the dependent backfill in a new batch. */
IF COL_LENGTH(N'dbo.PaymentRuns',N'RunType') IS NULL
    ALTER TABLE dbo.PaymentRuns ADD RunType int NOT NULL CONSTRAINT DF_PaymentRuns_RunType DEFAULT(1);
IF COL_LENGTH(N'dbo.PaymentRuns',N'FinancialEffectsAppliedAt') IS NULL
    ALTER TABLE dbo.PaymentRuns ADD FinancialEffectsAppliedAt datetime2 NULL;
IF COL_LENGTH(N'dbo.PaymentRuns',N'IsDeleted') IS NULL
    ALTER TABLE dbo.PaymentRuns ADD IsDeleted bit NOT NULL CONSTRAINT DF_PaymentRuns_IsDeleted DEFAULT(0);
IF COL_LENGTH(N'dbo.PaymentRuns',N'DeletedAt') IS NULL
    ALTER TABLE dbo.PaymentRuns ADD DeletedAt datetime2 NULL;
IF COL_LENGTH(N'dbo.PaymentRuns',N'DeletedBy') IS NULL
    ALTER TABLE dbo.PaymentRuns ADD DeletedBy int NULL;
IF COL_LENGTH(N'dbo.PaymentRuns',N'DeleteReason') IS NULL
    ALTER TABLE dbo.PaymentRuns ADD DeleteReason nvarchar(1000) NULL;
IF COL_LENGTH(N'dbo.PaymentRuns',N'PaymentOrderNumber') IS NULL
    ALTER TABLE dbo.PaymentRuns ADD PaymentOrderNumber nvarchar(100) NULL;
IF COL_LENGTH(N'dbo.PaymentRuns',N'PaymentOrderedAt') IS NULL
    ALTER TABLE dbo.PaymentRuns ADD PaymentOrderedAt datetime2 NULL;
IF COL_LENGTH(N'dbo.PaymentRuns',N'PaymentOrderedBy') IS NULL
    ALTER TABLE dbo.PaymentRuns ADD PaymentOrderedBy int NULL;
IF COL_LENGTH(N'dbo.PaymentRuns',N'PreparedByNameSnapshot') IS NULL
    ALTER TABLE dbo.PaymentRuns ADD PreparedByNameSnapshot nvarchar(300) NOT NULL CONSTRAINT DF_PaymentRuns_PreparedByNameSnapshot DEFAULT(N'');
IF COL_LENGTH(N'dbo.PaymentRuns',N'ConfirmedByNameSnapshot') IS NULL
    ALTER TABLE dbo.PaymentRuns ADD ConfirmedByNameSnapshot nvarchar(300) NOT NULL CONSTRAINT DF_PaymentRuns_ConfirmedByNameSnapshot DEFAULT(N'');
IF COL_LENGTH(N'dbo.PaymentRuns',N'PaymentOrderApproverNameSnapshot') IS NULL
    ALTER TABLE dbo.PaymentRuns ADD PaymentOrderApproverNameSnapshot nvarchar(300) NOT NULL CONSTRAINT DF_PaymentRuns_PaymentOrderApproverNameSnapshot DEFAULT(N'');
GO
UPDATE dbo.PaymentRuns
SET FinancialEffectsAppliedAt=COALESCE(PaymentOrderedAt,ApprovedAt,CreatedAt)
WHERE Status=4 AND IsDeleted=0 AND FinancialEffectsAppliedAt IS NULL;
GO

/* Supplier summary extensions */
IF COL_LENGTH(N'dbo.PaymentRunSupplierSummaries',N'InitialClaimAllocatedAmount') IS NULL
    ALTER TABLE dbo.PaymentRunSupplierSummaries ADD InitialClaimAllocatedAmount decimal(20,2) NOT NULL CONSTRAINT DF_PaymentRunSupplierSummaries_InitialClaimAllocatedAmount DEFAULT(0);
IF COL_LENGTH(N'dbo.PaymentRunSupplierSummaries',N'CurrentClaimAllocatedAmount') IS NULL
    ALTER TABLE dbo.PaymentRunSupplierSummaries ADD CurrentClaimAllocatedAmount decimal(20,2) NOT NULL CONSTRAINT DF_PaymentRunSupplierSummaries_CurrentClaimAllocatedAmount DEFAULT(0);
IF COL_LENGTH(N'dbo.PaymentRunSupplierSummaries',N'InitialClaimBefore') IS NULL
    ALTER TABLE dbo.PaymentRunSupplierSummaries ADD InitialClaimBefore decimal(20,2) NOT NULL CONSTRAINT DF_PaymentRunSupplierSummaries_InitialClaimBefore DEFAULT(0);
IF COL_LENGTH(N'dbo.PaymentRunSupplierSummaries',N'InitialClaimAfter') IS NULL
    ALTER TABLE dbo.PaymentRunSupplierSummaries ADD InitialClaimAfter decimal(20,2) NOT NULL CONSTRAINT DF_PaymentRunSupplierSummaries_InitialClaimAfter DEFAULT(0);
IF COL_LENGTH(N'dbo.PaymentRunSupplierSummaries',N'PaymentType') IS NULL
    ALTER TABLE dbo.PaymentRunSupplierSummaries ADD PaymentType int NULL;
IF COL_LENGTH(N'dbo.PaymentRunSupplierSummaries',N'RequestedAmount') IS NULL
    ALTER TABLE dbo.PaymentRunSupplierSummaries ADD RequestedAmount decimal(20,2) NOT NULL CONSTRAINT DF_PaymentRunSupplierSummaries_RequestedAmount DEFAULT(0);
GO

/* Security schema */
IF OBJECT_ID(N'dbo.AppRoles',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppRoles(
        Id int IDENTITY PRIMARY KEY, Code nvarchar(100) NOT NULL,
        Title nvarchar(200) NOT NULL, IsSystem bit NOT NULL DEFAULT(0),
        IsActive bit NOT NULL DEFAULT(1)
    );
END;
IF OBJECT_ID(N'dbo.AppPermissions',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppPermissions(
        Id int IDENTITY PRIMARY KEY, Code nvarchar(100) NOT NULL,
        Title nvarchar(200) NOT NULL, GroupTitle nvarchar(200) NOT NULL,
        SortOrder int NOT NULL DEFAULT(1)
    );
END;
GO
IF OBJECT_ID(N'dbo.AppUserRoles',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppUserRoles(
        UserId int NOT NULL, RoleId int NOT NULL,
        CONSTRAINT PK_AppUserRoles PRIMARY KEY(UserId,RoleId),
        CONSTRAINT FK_AppUserRoles_User FOREIGN KEY(UserId) REFERENCES dbo.AppUsers(Id) ON DELETE CASCADE,
        CONSTRAINT FK_AppUserRoles_Role FOREIGN KEY(RoleId) REFERENCES dbo.AppRoles(Id) ON DELETE CASCADE
    );
END;
IF OBJECT_ID(N'dbo.AppRolePermissions',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppRolePermissions(
        RoleId int NOT NULL, PermissionId int NOT NULL,
        CONSTRAINT PK_AppRolePermissions PRIMARY KEY(RoleId,PermissionId),
        CONSTRAINT FK_AppRolePermissions_Role FOREIGN KEY(RoleId) REFERENCES dbo.AppRoles(Id) ON DELETE CASCADE,
        CONSTRAINT FK_AppRolePermissions_Permission FOREIGN KEY(PermissionId) REFERENCES dbo.AppPermissions(Id) ON DELETE CASCADE
    );
END;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_AppRoles_Code' AND object_id=OBJECT_ID(N'dbo.AppRoles'))
    CREATE UNIQUE INDEX UX_AppRoles_Code ON dbo.AppRoles(Code);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_AppPermissions_Code' AND object_id=OBJECT_ID(N'dbo.AppPermissions'))
    CREATE UNIQUE INDEX UX_AppPermissions_Code ON dbo.AppPermissions(Code);
GO

PRINT N'ارتقای ساختار پایگاه داده با موفقیت تکمیل شد.';
GO
