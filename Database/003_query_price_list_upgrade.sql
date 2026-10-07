-- 003_query_price_list_upgrade.sql
-- Additive, idempotent upgrade. Does not drop or recreate existing tables/data.

IF OBJECT_ID(N'dbo.InputQueries',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.InputQueries(
  Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_InputQueries PRIMARY KEY,
  Title nvarchar(300) NOT NULL,
  SqlText nvarchar(max) NOT NULL,
  ServerInstance nvarchar(300) NOT NULL DEFAULT(N''),
  DatabaseName nvarchar(300) NOT NULL DEFAULT(N''),
  AuthenticationMode nvarchar(30) NOT NULL DEFAULT(N'sql'),
  Username nvarchar(200) NOT NULL DEFAULT(N''),
  PasswordProtected nvarchar(4000) NOT NULL DEFAULT(N''),
  Encrypt bit NOT NULL DEFAULT(0),
  TrustServerCertificate bit NOT NULL DEFAULT(1),
  Enabled bit NOT NULL DEFAULT(1),
  CommandTimeoutSeconds int NOT NULL DEFAULT(30),
  CreatedAt datetime2 NOT NULL DEFAULT(sysutcdatetime()),
  UpdatedAt datetime2 NOT NULL DEFAULT(sysutcdatetime())
 );
END
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_InputQueries_Title' AND object_id=OBJECT_ID(N'dbo.InputQueries'))
 CREATE UNIQUE INDEX UX_InputQueries_Title ON dbo.InputQueries(Title);

IF OBJECT_ID(N'dbo.SupplierPriceListItems',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.SupplierPriceListItems(
  Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupplierPriceListItems PRIMARY KEY,
  SupplierPartId int NOT NULL,
  PurchasePrice decimal(20,2) NOT NULL,
  ValidFrom date NOT NULL,
  ValidTo date NOT NULL,
  CreatedAt datetime2 NOT NULL DEFAULT(sysutcdatetime()),
  UpdatedAt datetime2 NOT NULL DEFAULT(sysutcdatetime()),
  CONSTRAINT FK_SupplierPriceListItems_SupplierPart FOREIGN KEY(SupplierPartId) REFERENCES dbo.SupplierParts(Id) ON DELETE NO ACTION
 );
END
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_SupplierPriceListItems_Lookup' AND object_id=OBJECT_ID(N'dbo.SupplierPriceListItems'))
 CREATE INDEX IX_SupplierPriceListItems_Lookup ON dbo.SupplierPriceListItems(SupplierPartId,ValidFrom,Id);

IF COL_LENGTH(N'dbo.PaymentRuns',N'ReceiptSource') IS NULL
 ALTER TABLE dbo.PaymentRuns ADD ReceiptSource int NOT NULL CONSTRAINT DF_PaymentRuns_ReceiptSource DEFAULT(1);

IF COL_LENGTH(N'dbo.PaymentRunInvoices',N'ReceiptQuantity') IS NULL
 ALTER TABLE dbo.PaymentRunInvoices ADD ReceiptQuantity decimal(20,6) NOT NULL CONSTRAINT DF_PaymentRunInvoices_ReceiptQuantity DEFAULT(0);
IF COL_LENGTH(N'dbo.PaymentRunInvoices',N'DebtCalculationMethod') IS NULL
 ALTER TABLE dbo.PaymentRunInvoices ADD DebtCalculationMethod int NOT NULL CONSTRAINT DF_PaymentRunInvoices_DebtCalculationMethod DEFAULT(1);
IF COL_LENGTH(N'dbo.PaymentRunInvoices',N'AppliedUnitPrice') IS NULL
 ALTER TABLE dbo.PaymentRunInvoices ADD AppliedUnitPrice decimal(20,2) NOT NULL CONSTRAINT DF_PaymentRunInvoices_AppliedUnitPrice DEFAULT(0);
IF COL_LENGTH(N'dbo.PaymentRunInvoices',N'PriceListItemId') IS NULL
 ALTER TABLE dbo.PaymentRunInvoices ADD PriceListItemId bigint NULL;
IF COL_LENGTH(N'dbo.PaymentRunInvoices',N'AppliedPriceValidFrom') IS NULL
 ALTER TABLE dbo.PaymentRunInvoices ADD AppliedPriceValidFrom date NULL;
IF COL_LENGTH(N'dbo.PaymentRunInvoices',N'AppliedPriceValidTo') IS NULL
 ALTER TABLE dbo.PaymentRunInvoices ADD AppliedPriceValidTo date NULL;

IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_PaymentRunInvoices_PriceListItem' AND parent_object_id=OBJECT_ID(N'dbo.PaymentRunInvoices'))
 ALTER TABLE dbo.PaymentRunInvoices ADD CONSTRAINT FK_PaymentRunInvoices_PriceListItem FOREIGN KEY(PriceListItemId) REFERENCES dbo.SupplierPriceListItems(Id) ON DELETE NO ACTION;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_PaymentRunInvoices_PriceListItemId' AND object_id=OBJECT_ID(N'dbo.PaymentRunInvoices'))
 CREATE INDEX IX_PaymentRunInvoices_PriceListItemId ON dbo.PaymentRunInvoices(PriceListItemId);

IF NOT EXISTS(SELECT 1 FROM dbo.SystemParameters WHERE Code=N'CURRENT_CLAIM_CALC_METHOD')
 INSERT INTO dbo.SystemParameters(Code,Title,ValueType,Value,Unit,SortOrder,IsActive,UpdatedAt)
 VALUES(N'CURRENT_CLAIM_CALC_METHOD',N'نحوه محاسبه مطالبات جاری',2,1,N'',8,1,sysutcdatetime());
