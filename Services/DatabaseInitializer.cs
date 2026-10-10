using Indamin.Payment.Data;using Microsoft.EntityFrameworkCore;
namespace Indamin.Payment.Services;
public static class DatabaseInitializer{
 const string Sql="""SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name IN ('CompanySettings','AppUsers','LookupValues','Parts','Suppliers','SupplierActivities','SupplierParts','PaymentParameters','SupplierPartEvaluations','PaymentRuns','PaymentRunSupplierSummaries','PaymentRunInvoices','PaymentInvoiceScores','AuditLogs')""";
 public static async Task InitializeAsync(AppDbContext db){var c=await db.Database.SqlQueryRaw<int>(Sql).SingleAsync();if(c<13)throw new InvalidOperationException("ساختار پایگاه داده کامل نیست. ابتدا Database/001_initial.sql را اجرا کنید.");}
 public static async Task EnsurePaymentSnapshotSchemaAsync(AppDbContext db){await db.Database.ExecuteSqlRawAsync("""IF OBJECT_ID(N'dbo.PaymentRunParameterSnapshots',N'U') IS NULL CREATE TABLE dbo.PaymentRunParameterSnapshots(Id bigint IDENTITY PRIMARY KEY,PaymentRunId int NOT NULL,PaymentParameterId int NOT NULL,Code nvarchar(100) NOT NULL,Title nvarchar(300) NOT NULL,Type int NOT NULL,Weight decimal(8,4) NOT NULL,MaxScore decimal(8,2) NOT NULL,ScoringGuide nvarchar(max) NOT NULL,ScoringMethod int NOT NULL,SortOrder int NOT NULL,CONSTRAINT FK_PaymentRunParameterSnapshots_Run FOREIGN KEY(PaymentRunId) REFERENCES dbo.PaymentRuns(Id) ON DELETE CASCADE); IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_PaymentRunParameterSnapshots_Key' AND object_id=OBJECT_ID(N'dbo.PaymentRunParameterSnapshots')) CREATE UNIQUE INDEX UX_PaymentRunParameterSnapshots_Key ON dbo.PaymentRunParameterSnapshots(PaymentRunId,PaymentParameterId); IF COL_LENGTH(N'dbo.PaymentRunInvoices',N'SupplyCapacity') IS NULL ALTER TABLE dbo.PaymentRunInvoices ADD SupplyCapacity decimal(18,2) NOT NULL CONSTRAINT DF_PaymentRunInvoices_SupplyCapacity DEFAULT(0); IF COL_LENGTH(N'dbo.PaymentRunInvoices',N'SupplierInitialClaimAmount') IS NULL ALTER TABLE dbo.PaymentRunInvoices ADD SupplierInitialClaimAmount decimal(20,2) NOT NULL CONSTRAINT DF_PaymentRunInvoices_SupplierInitialClaimAmount DEFAULT(0); IF COL_LENGTH(N'dbo.PaymentRunInvoices',N'SupplierOutstandingDebt') IS NULL ALTER TABLE dbo.PaymentRunInvoices ADD SupplierOutstandingDebt decimal(20,2) NOT NULL CONSTRAINT DF_PaymentRunInvoices_SupplierOutstandingDebt DEFAULT(0); IF COL_LENGTH(N'dbo.PaymentRunInvoices',N'CalculatedAllocatedAmount') IS NULL ALTER TABLE dbo.PaymentRunInvoices ADD CalculatedAllocatedAmount decimal(20,2) NOT NULL CONSTRAINT DF_PaymentRunInvoices_CalculatedAllocatedAmount DEFAULT(0); IF COL_LENGTH(N'dbo.PaymentInvoiceScores',N'ParameterCodeSnapshot') IS NULL ALTER TABLE dbo.PaymentInvoiceScores ADD ParameterCodeSnapshot nvarchar(100) NOT NULL CONSTRAINT DF_PaymentInvoiceScores_ParameterCodeSnapshot DEFAULT(''); IF COL_LENGTH(N'dbo.PaymentInvoiceScores',N'ParameterTypeSnapshot') IS NULL ALTER TABLE dbo.PaymentInvoiceScores ADD ParameterTypeSnapshot int NOT NULL CONSTRAINT DF_PaymentInvoiceScores_ParameterTypeSnapshot DEFAULT(0); IF COL_LENGTH(N'dbo.PaymentInvoiceScores',N'MaxScoreSnapshot') IS NULL ALTER TABLE dbo.PaymentInvoiceScores ADD MaxScoreSnapshot decimal(8,2) NOT NULL CONSTRAINT DF_PaymentInvoiceScores_MaxScoreSnapshot DEFAULT(5); IF COL_LENGTH(N'dbo.PaymentInvoiceScores',N'ScoringGuideSnapshot') IS NULL ALTER TABLE dbo.PaymentInvoiceScores ADD ScoringGuideSnapshot nvarchar(max) NOT NULL CONSTRAINT DF_PaymentInvoiceScores_ScoringGuideSnapshot DEFAULT(''); IF COL_LENGTH(N'dbo.PaymentInvoiceScores',N'ScoringMethodSnapshot') IS NULL ALTER TABLE dbo.PaymentInvoiceScores ADD ScoringMethodSnapshot int NOT NULL CONSTRAINT DF_PaymentInvoiceScores_ScoringMethodSnapshot DEFAULT(0); IF COL_LENGTH(N'dbo.SupplierPartEvaluations',N'IsActive') IS NULL ALTER TABLE dbo.SupplierPartEvaluations ADD IsActive bit NOT NULL CONSTRAINT DF_SupplierPartEvaluations_IsActive DEFAULT(1);""");}
 public static async Task EnsureAdvancedPaymentSchemaAsync(AppDbContext db){await db.Database.ExecuteSqlRawAsync("""IF OBJECT_ID(N'dbo.NonCalculatedPaymentLineSnapshots',N'U') IS NULL CREATE TABLE dbo.NonCalculatedPaymentLineSnapshots(Id bigint IDENTITY PRIMARY KEY,PaymentRunId int NOT NULL,LineNumber int NOT NULL,SupplierId int NOT NULL,SupplierTitleSnapshot nvarchar(300) NOT NULL,PaymentType int NOT NULL,RequestedAmount decimal(20,2) NOT NULL,Description nvarchar(2000) NOT NULL DEFAULT(N''),SupplierInitialClaimBefore decimal(20,2) NOT NULL,SupplierCurrentDebtBefore decimal(20,2) NOT NULL,SupplierTotalDebtBefore decimal(20,2) NOT NULL,InitialClaimAllocatedAmount decimal(20,2) NOT NULL,CurrentClaimAllocatedAmount decimal(20,2) NOT NULL,AllocatedAmount decimal(20,2) NOT NULL,PaymentDateJalali nvarchar(20) NOT NULL,PaymentDate datetime2 NOT NULL,CreatedAt datetime2 NOT NULL DEFAULT(sysutcdatetime()),CONSTRAINT FK_NonCalculatedPaymentLineSnapshots_Run FOREIGN KEY(PaymentRunId) REFERENCES dbo.PaymentRuns(Id) ON DELETE CASCADE,CONSTRAINT FK_NonCalculatedPaymentLineSnapshots_Supplier FOREIGN KEY(SupplierId) REFERENCES dbo.Suppliers(Id)); IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_NonCalculatedPaymentLineSnapshots_Run' AND object_id=OBJECT_ID(N'dbo.NonCalculatedPaymentLineSnapshots')) CREATE INDEX IX_NonCalculatedPaymentLineSnapshots_Run ON dbo.NonCalculatedPaymentLineSnapshots(PaymentRunId,LineNumber); IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_NonCalculatedPaymentLineSnapshots_Supplier' AND object_id=OBJECT_ID(N'dbo.NonCalculatedPaymentLineSnapshots')) CREATE INDEX IX_NonCalculatedPaymentLineSnapshots_Supplier ON dbo.NonCalculatedPaymentLineSnapshots(SupplierId);IF OBJECT_ID(N'dbo.SystemParameters',N'U') IS NULL CREATE TABLE dbo.SystemParameters(Id int IDENTITY PRIMARY KEY,Code nvarchar(100) NOT NULL,Title nvarchar(300) NOT NULL,ValueType int NOT NULL,Value decimal(20,2) NOT NULL,Unit nvarchar(50) NOT NULL DEFAULT(N''),SortOrder int NOT NULL DEFAULT(1),IsActive bit NOT NULL DEFAULT(1),UpdatedAt datetime2 NOT NULL DEFAULT(sysutcdatetime())); IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_SystemParameters_Code' AND object_id=OBJECT_ID(N'dbo.SystemParameters')) CREATE UNIQUE INDEX UX_SystemParameters_Code ON dbo.SystemParameters(Code); IF OBJECT_ID(N'dbo.PaymentRunSystemParameterSnapshots',N'U') IS NULL CREATE TABLE dbo.PaymentRunSystemParameterSnapshots(Id bigint IDENTITY PRIMARY KEY,PaymentRunId int NOT NULL,Code nvarchar(100) NOT NULL,Title nvarchar(300) NOT NULL,ValueType int NOT NULL,Value decimal(20,2) NOT NULL,Unit nvarchar(50) NOT NULL DEFAULT(N''),CONSTRAINT FK_PaymentRunSystemParameterSnapshots_Run FOREIGN KEY(PaymentRunId) REFERENCES dbo.PaymentRuns(Id) ON DELETE CASCADE); IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_PaymentRunSystemParameterSnapshots_Key' AND object_id=OBJECT_ID(N'dbo.PaymentRunSystemParameterSnapshots')) CREATE UNIQUE INDEX UX_PaymentRunSystemParameterSnapshots_Key ON dbo.PaymentRunSystemParameterSnapshots(PaymentRunId,Code); IF OBJECT_ID(N'dbo.SupplierClaimHistories',N'U') IS NULL CREATE TABLE dbo.SupplierClaimHistories(Id bigint IDENTITY PRIMARY KEY,SupplierId int NOT NULL,ClaimType int NOT NULL,AmountBefore decimal(20,2) NOT NULL,AmountChange decimal(20,2) NOT NULL,AmountAfter decimal(20,2) NOT NULL,PaymentRunId int NULL,Reference nvarchar(500) NOT NULL DEFAULT(N''),EffectiveDateJalali nvarchar(20) NOT NULL DEFAULT(N''),UserId int NULL,CreatedAt datetime2 NOT NULL DEFAULT(sysutcdatetime()),CONSTRAINT FK_SupplierClaimHistories_Supplier FOREIGN KEY(SupplierId) REFERENCES dbo.Suppliers(Id),CONSTRAINT FK_SupplierClaimHistories_Run FOREIGN KEY(PaymentRunId) REFERENCES dbo.PaymentRuns(Id) ON DELETE SET NULL,CONSTRAINT FK_SupplierClaimHistories_User FOREIGN KEY(UserId) REFERENCES dbo.AppUsers(Id)); IF COL_LENGTH(N'dbo.PaymentRuns',N'RunType') IS NULL ALTER TABLE dbo.PaymentRuns ADD RunType int NOT NULL CONSTRAINT DF_PaymentRuns_RunType DEFAULT(1); IF COL_LENGTH(N'dbo.PaymentRunSupplierSummaries',N'InitialClaimAllocatedAmount') IS NULL ALTER TABLE dbo.PaymentRunSupplierSummaries ADD InitialClaimAllocatedAmount decimal(20,2) NOT NULL CONSTRAINT DF_PaymentRunSupplierSummaries_InitialClaimAllocatedAmount DEFAULT(0); IF COL_LENGTH(N'dbo.PaymentRunSupplierSummaries',N'CurrentClaimAllocatedAmount') IS NULL ALTER TABLE dbo.PaymentRunSupplierSummaries ADD CurrentClaimAllocatedAmount decimal(20,2) NOT NULL CONSTRAINT DF_PaymentRunSupplierSummaries_CurrentClaimAllocatedAmount DEFAULT(0); IF COL_LENGTH(N'dbo.PaymentRunSupplierSummaries',N'InitialClaimBefore') IS NULL ALTER TABLE dbo.PaymentRunSupplierSummaries ADD InitialClaimBefore decimal(20,2) NOT NULL CONSTRAINT DF_PaymentRunSupplierSummaries_InitialClaimBefore DEFAULT(0); IF COL_LENGTH(N'dbo.PaymentRunSupplierSummaries',N'InitialClaimAfter') IS NULL ALTER TABLE dbo.PaymentRunSupplierSummaries ADD InitialClaimAfter decimal(20,2) NOT NULL CONSTRAINT DF_PaymentRunSupplierSummaries_InitialClaimAfter DEFAULT(0); IF COL_LENGTH(N'dbo.PaymentRunSupplierSummaries',N'PaymentType') IS NULL ALTER TABLE dbo.PaymentRunSupplierSummaries ADD PaymentType int NULL; IF COL_LENGTH(N'dbo.PaymentRunSupplierSummaries',N'RequestedAmount') IS NULL ALTER TABLE dbo.PaymentRunSupplierSummaries ADD RequestedAmount decimal(20,2) NOT NULL CONSTRAINT DF_PaymentRunSupplierSummaries_RequestedAmount DEFAULT(0); IF COL_LENGTH(N'dbo.PaymentRunInvoices',N'CalculatedCurrentAllocatedAmount') IS NULL ALTER TABLE dbo.PaymentRunInvoices ADD CalculatedCurrentAllocatedAmount decimal(20,2) NOT NULL CONSTRAINT DF_PaymentRunInvoices_CalculatedCurrentAllocatedAmount DEFAULT(0); IF COL_LENGTH(N'dbo.PaymentRunInvoices',N'CalculatedInitialClaimAllocatedAmount') IS NULL ALTER TABLE dbo.PaymentRunInvoices ADD CalculatedInitialClaimAllocatedAmount decimal(20,2) NOT NULL CONSTRAINT DF_PaymentRunInvoices_CalculatedInitialClaimAllocatedAmount DEFAULT(0); IF COL_LENGTH(N'dbo.PaymentRunInvoices',N'AllocatedCurrentAmount') IS NULL ALTER TABLE dbo.PaymentRunInvoices ADD AllocatedCurrentAmount decimal(20,2) NOT NULL CONSTRAINT DF_PaymentRunInvoices_AllocatedCurrentAmount DEFAULT(0); IF COL_LENGTH(N'dbo.PaymentRunInvoices',N'AllocatedInitialClaimAmount') IS NULL ALTER TABLE dbo.PaymentRunInvoices ADD AllocatedInitialClaimAmount decimal(20,2) NOT NULL CONSTRAINT DF_PaymentRunInvoices_AllocatedInitialClaimAmount DEFAULT(0);""");}
public static async Task SeedSystemParametersAsync(AppDbContext db){var defs=new[]{("CALC_INITIAL_SHARE","درصد سهم مطالبات استقراری (پرداخت محاسباتی)",SystemParameterValueType.Percentage,50m,"٪",1),("CALC_CURRENT_SHARE","درصد سهم مطالبات جاری (پرداخت محاسباتی)",SystemParameterValueType.Percentage,50m,"٪",2),("NONCALC_INITIAL_SHARE","درصد سهم مطالبات استقراری (پرداخت غیرمحاسباتی)",SystemParameterValueType.Percentage,50m,"٪",3),("NONCALC_CURRENT_SHARE","درصد سهم مطالبات جاری (پرداخت غیرمحاسباتی)",SystemParameterValueType.Percentage,50m,"٪",4),("MIN_EFFECTIVE_DEBT_AGE","حداقل سن بدهی موثر",SystemParameterValueType.Number,0m,"روز",5),("MIN_ALLOCATION_AMOUNT","حداقل مبلغ قابل تخصیص به هر رسید",SystemParameterValueType.Amount,1m,"ریال",6),("ALLOCATION_ROUNDING","ضریب مبلغ محاسباتی",SystemParameterValueType.Amount,100000m,"ریال",7),("CURRENT_CLAIM_CALC_METHOD","نحوه محاسبه مطالبات جاری",SystemParameterValueType.Number,1m,"",8)};foreach(var d in defs){var row=await db.SystemParameters.SingleOrDefaultAsync(x=>x.Code==d.Item1);if(row==null){db.SystemParameters.Add(new SystemParameter{Code=d.Item1,Title=d.Item2,ValueType=d.Item3,Value=d.Item4,Unit=d.Item5,SortOrder=d.Item6,IsActive=true});}else{row.Title=d.Item2;row.ValueType=d.Item3;row.Unit=d.Item5;row.SortOrder=d.Item6;if(d.Item1=="MIN_ALLOCATION_AMOUNT"&&row.Value==0m)row.Value=1m;if(d.Item1=="ALLOCATION_ROUNDING"&&row.Value==1000000m)row.Value=100000m;}}await db.SaveChangesAsync();}

 public static async Task EnsurePaymentLifecycleSchemaAsync(AppDbContext db){
  // Column additions are executed in their own batch. SQL Server can compile a later
  // statement against the pre-ALTER schema when DDL and the dependent UPDATE are sent
  // as one batch, which caused "Invalid column name" for FinancialEffectsAppliedAt.
  await db.Database.ExecuteSqlRawAsync("""IF COL_LENGTH(N'dbo.PaymentRuns',N'FinancialEffectsAppliedAt') IS NULL ALTER TABLE dbo.PaymentRuns ADD FinancialEffectsAppliedAt datetime2 NULL; IF COL_LENGTH(N'dbo.PaymentRuns',N'IsDeleted') IS NULL ALTER TABLE dbo.PaymentRuns ADD IsDeleted bit NOT NULL CONSTRAINT DF_PaymentRuns_IsDeleted DEFAULT(0); IF COL_LENGTH(N'dbo.PaymentRuns',N'DeletedAt') IS NULL ALTER TABLE dbo.PaymentRuns ADD DeletedAt datetime2 NULL; IF COL_LENGTH(N'dbo.PaymentRuns',N'DeletedBy') IS NULL ALTER TABLE dbo.PaymentRuns ADD DeletedBy int NULL; IF COL_LENGTH(N'dbo.PaymentRuns',N'DeleteReason') IS NULL ALTER TABLE dbo.PaymentRuns ADD DeleteReason nvarchar(1000) NULL; IF COL_LENGTH(N'dbo.PaymentRuns',N'PaymentOrderNumber') IS NULL ALTER TABLE dbo.PaymentRuns ADD PaymentOrderNumber nvarchar(100) NULL; IF COL_LENGTH(N'dbo.PaymentRuns',N'PaymentOrderedAt') IS NULL ALTER TABLE dbo.PaymentRuns ADD PaymentOrderedAt datetime2 NULL; IF COL_LENGTH(N'dbo.PaymentRuns',N'PaymentOrderedBy') IS NULL ALTER TABLE dbo.PaymentRuns ADD PaymentOrderedBy int NULL; IF COL_LENGTH(N'dbo.PaymentRuns',N'PreparedByNameSnapshot') IS NULL ALTER TABLE dbo.PaymentRuns ADD PreparedByNameSnapshot nvarchar(300) NOT NULL CONSTRAINT DF_PaymentRuns_PreparedByNameSnapshot DEFAULT(N''); IF COL_LENGTH(N'dbo.PaymentRuns',N'ConfirmedByNameSnapshot') IS NULL ALTER TABLE dbo.PaymentRuns ADD ConfirmedByNameSnapshot nvarchar(300) NOT NULL CONSTRAINT DF_PaymentRuns_ConfirmedByNameSnapshot DEFAULT(N''); IF COL_LENGTH(N'dbo.PaymentRuns',N'PaymentOrderApproverNameSnapshot') IS NULL ALTER TABLE dbo.PaymentRuns ADD PaymentOrderApproverNameSnapshot nvarchar(300) NOT NULL CONSTRAINT DF_PaymentRuns_PaymentOrderApproverNameSnapshot DEFAULT(N'');""");
  // Only a payment order is a definite evidence of financial effect application.
  // Approved-but-not-ordered calculated runs intentionally remain financially inactive.
  await db.Database.ExecuteSqlRawAsync("""UPDATE dbo.PaymentRuns SET FinancialEffectsAppliedAt=COALESCE(PaymentOrderedAt,ApprovedAt,CreatedAt) WHERE Status=4 AND IsDeleted=0 AND FinancialEffectsAppliedAt IS NULL;""");
 }
 public static async Task EnsureSecuritySchemaAsync(AppDbContext db){await db.Database.ExecuteSqlRawAsync("""IF OBJECT_ID(N'dbo.AppRoles',N'U') IS NULL CREATE TABLE dbo.AppRoles(Id int IDENTITY PRIMARY KEY,Code nvarchar(100) NOT NULL,Title nvarchar(200) NOT NULL,IsSystem bit NOT NULL DEFAULT(0),IsActive bit NOT NULL DEFAULT(1)); IF OBJECT_ID(N'dbo.AppPermissions',N'U') IS NULL CREATE TABLE dbo.AppPermissions(Id int IDENTITY PRIMARY KEY,Code nvarchar(100) NOT NULL,Title nvarchar(200) NOT NULL,GroupTitle nvarchar(200) NOT NULL,SortOrder int NOT NULL DEFAULT(1)); IF OBJECT_ID(N'dbo.AppUserRoles',N'U') IS NULL CREATE TABLE dbo.AppUserRoles(UserId int NOT NULL,RoleId int NOT NULL,CONSTRAINT PK_AppUserRoles PRIMARY KEY(UserId,RoleId),CONSTRAINT FK_AppUserRoles_User FOREIGN KEY(UserId) REFERENCES dbo.AppUsers(Id) ON DELETE CASCADE,CONSTRAINT FK_AppUserRoles_Role FOREIGN KEY(RoleId) REFERENCES dbo.AppRoles(Id) ON DELETE CASCADE); IF OBJECT_ID(N'dbo.AppRolePermissions',N'U') IS NULL CREATE TABLE dbo.AppRolePermissions(RoleId int NOT NULL,PermissionId int NOT NULL,CONSTRAINT PK_AppRolePermissions PRIMARY KEY(RoleId,PermissionId),CONSTRAINT FK_AppRolePermissions_Role FOREIGN KEY(RoleId) REFERENCES dbo.AppRoles(Id) ON DELETE CASCADE,CONSTRAINT FK_AppRolePermissions_Permission FOREIGN KEY(PermissionId) REFERENCES dbo.AppPermissions(Id) ON DELETE CASCADE); IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_AppRoles_Code' AND object_id=OBJECT_ID(N'dbo.AppRoles')) CREATE UNIQUE INDEX UX_AppRoles_Code ON dbo.AppRoles(Code); IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_AppPermissions_Code' AND object_id=OBJECT_ID(N'dbo.AppPermissions')) CREATE UNIQUE INDEX UX_AppPermissions_Code ON dbo.AppPermissions(Code);""");}
 public static async Task EnsureAdministratorRoleAssignmentAsync(AppDbContext db){var admin=await db.Users.FirstOrDefaultAsync(x=>x.IsAdmin);var role=await db.Roles.SingleOrDefaultAsync(x=>x.Code=="SYS_ADMIN");if(admin==null||role==null)return;if(!await db.UserRoles.AnyAsync(x=>x.UserId==admin.Id&&x.RoleId==role.Id)){db.UserRoles.Add(new AppUserRole{UserId=admin.Id,RoleId=role.Id});await db.SaveChangesAsync();}}
 public static async Task EnsureSupplierInitialClaimAsync(AppDbContext db){await db.Database.ExecuteSqlRawAsync("""IF COL_LENGTH(N'dbo.Suppliers',N'InitialClaimAmount') IS NULL ALTER TABLE dbo.Suppliers ADD InitialClaimAmount decimal(20,2) NOT NULL CONSTRAINT DF_Suppliers_InitialClaimAmount DEFAULT(0);""");}
 public static async Task EnsureCompanySettingsAsync(AppDbContext db){await db.Database.ExecuteSqlRawAsync("""IF OBJECT_ID(N'dbo.CompanySettings',N'U') IS NULL CREATE TABLE dbo.CompanySettings(Id int NOT NULL CONSTRAINT PK_CompanySettings PRIMARY KEY,CompanyName nvarchar(300) NOT NULL,ShortName nvarchar(100) NOT NULL,SystemName nvarchar(300) NOT NULL,Slogan nvarchar(500) NOT NULL,Website nvarchar(500) NULL,Phone nvarchar(100) NULL,Email nvarchar(200) NULL,Address nvarchar(1000) NULL,EconomicCode nvarchar(100) NULL,NationalId nvarchar(100) NULL,FooterText nvarchar(1000) NULL,PrimaryColor nvarchar(20) NOT NULL,SecondaryColor nvarchar(20) NOT NULL,LogoBytes varbinary(max) NULL,LogoContentType nvarchar(100) NULL,FaviconBytes varbinary(max) NULL,FaviconContentType nvarchar(100) NULL,UpdatedAt datetime2 NOT NULL DEFAULT(sysutcdatetime()))""");if(!await db.CompanySettings.AnyAsync()){db.CompanySettings.Add(new CompanySettings());await db.SaveChangesAsync();}}
 public static async Task EnsureQueryAndPriceListSchemaAsync(AppDbContext db)
 {
  await db.Database.ExecuteSqlRawAsync("""
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
  IsActive bit NOT NULL CONSTRAINT DF_SupplierPriceListItems_IsActive DEFAULT(1),
  CONSTRAINT FK_SupplierPriceListItems_SupplierPart FOREIGN KEY(SupplierPartId) REFERENCES dbo.SupplierParts(Id) ON DELETE NO ACTION
 );
END
IF COL_LENGTH(N'dbo.SupplierPriceListItems',N'IsActive') IS NULL
 ALTER TABLE dbo.SupplierPriceListItems ADD IsActive bit NOT NULL CONSTRAINT DF_SupplierPriceListItems_IsActive DEFAULT(1);
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
""");
  // The FK is deliberately added after the column/table DDL so SQL Server does not compile
  // against the old PaymentRunInvoices schema.
  await db.Database.ExecuteSqlRawAsync("""
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_PaymentRunInvoices_PriceListItem' AND parent_object_id=OBJECT_ID(N'dbo.PaymentRunInvoices'))
 ALTER TABLE dbo.PaymentRunInvoices ADD CONSTRAINT FK_PaymentRunInvoices_PriceListItem FOREIGN KEY(PriceListItemId) REFERENCES dbo.SupplierPriceListItems(Id) ON DELETE NO ACTION;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_PaymentRunInvoices_PriceListItemId' AND object_id=OBJECT_ID(N'dbo.PaymentRunInvoices'))
 CREATE INDEX IX_PaymentRunInvoices_PriceListItemId ON dbo.PaymentRunInvoices(PriceListItemId);
""");
 }
 public static async Task EnsurePriceDebtAdjustmentSchemaAsync(AppDbContext db)
 {
  await db.Database.ExecuteSqlRawAsync("""
IF OBJECT_ID(N'dbo.SupplierPriceListChangeBatches',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.SupplierPriceListChangeBatches(
  BatchKey nvarchar(32) NOT NULL CONSTRAINT PK_SupplierPriceListChangeBatches PRIMARY KEY,
  ChangeType nvarchar(100) NOT NULL,
  Description nvarchar(1000) NOT NULL,
  SupplierId int NULL,
  SupplierTitleSnapshot nvarchar(300) NOT NULL DEFAULT(N''),
  AppliedBy int NOT NULL,
  AppliedByNameSnapshot nvarchar(300) NOT NULL DEFAULT(N''),
  AppliedAt datetime2 NOT NULL DEFAULT(sysutcdatetime()),
  AffectedReceiptCount int NOT NULL DEFAULT(0),
  NetDebtChange decimal(20,2) NOT NULL DEFAULT(0)
 );
END
IF OBJECT_ID(N'dbo.SupplierPriceDebtAdjustments',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.SupplierPriceDebtAdjustments(
  Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupplierPriceDebtAdjustments PRIMARY KEY,
  BatchKey nvarchar(32) NOT NULL,
  PaymentKeyHash nvarchar(64) NOT NULL DEFAULT(N''),
  PaymentRunInvoiceId bigint NULL,
  ReceiptNo nvarchar(150) NOT NULL,
  Warehouse nvarchar(300) NOT NULL,
  PartTitle nvarchar(300) NOT NULL,
  SupplierTitle nvarchar(300) NOT NULL,
  SupplierId int NOT NULL,
  PartId int NOT NULL,
  ReceiptDate date NOT NULL,
  ReceiptQuantity decimal(20,6) NOT NULL,
  PreviousPriceListItemId bigint NULL,
  NewPriceListItemId bigint NULL,
  PreviousUnitPrice decimal(20,2) NOT NULL,
  NewUnitPrice decimal(20,2) NOT NULL,
  PreviousDebtAmount decimal(20,2) NOT NULL,
  NewDebtAmount decimal(20,2) NOT NULL,
  AmountChange decimal(20,2) NOT NULL,
  AppliedBy int NOT NULL,
  AppliedAt datetime2 NOT NULL DEFAULT(sysutcdatetime()),
  Reason nvarchar(1000) NOT NULL DEFAULT(N''),
  CONSTRAINT FK_SupplierPriceDebtAdjustments_Batch FOREIGN KEY(BatchKey)
   REFERENCES dbo.SupplierPriceListChangeBatches(BatchKey) ON DELETE CASCADE,
  CONSTRAINT FK_SupplierPriceDebtAdjustments_Invoice FOREIGN KEY(PaymentRunInvoiceId)
   REFERENCES dbo.PaymentRunInvoices(Id) ON DELETE SET NULL
 );
END
IF COL_LENGTH(N'dbo.SupplierPriceDebtAdjustments',N'PaymentKeyHash') IS NULL
 ALTER TABLE dbo.SupplierPriceDebtAdjustments ADD PaymentKeyHash nvarchar(64) NOT NULL CONSTRAINT DF_SupplierPriceDebtAdjustments_PaymentKeyHash DEFAULT(N'');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_SupplierPriceDebtAdjustments_PaymentKeyHash' AND object_id=OBJECT_ID(N'dbo.SupplierPriceDebtAdjustments'))
 CREATE INDEX IX_SupplierPriceDebtAdjustments_PaymentKeyHash ON dbo.SupplierPriceDebtAdjustments(PaymentKeyHash);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_SupplierPriceDebtAdjustments_Invoice' AND object_id=OBJECT_ID(N'dbo.SupplierPriceDebtAdjustments'))
 CREATE INDEX IX_SupplierPriceDebtAdjustments_Invoice ON dbo.SupplierPriceDebtAdjustments(PaymentRunInvoiceId);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_SupplierPriceDebtAdjustments_BatchInvoice' AND object_id=OBJECT_ID(N'dbo.SupplierPriceDebtAdjustments'))
 CREATE UNIQUE INDEX UX_SupplierPriceDebtAdjustments_BatchInvoice ON dbo.SupplierPriceDebtAdjustments(BatchKey,PaymentRunInvoiceId);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_SupplierPriceListChangeBatches_AppliedAt' AND object_id=OBJECT_ID(N'dbo.SupplierPriceListChangeBatches'))
 CREATE INDEX IX_SupplierPriceListChangeBatches_AppliedAt ON dbo.SupplierPriceListChangeBatches(AppliedAt DESC);
""");
 }
 public static async Task EnsureSupplierPartAssessmentSchemaAsync(AppDbContext db){
  await db.Database.ExecuteSqlRawAsync("""
IF OBJECT_ID(N'dbo.SupplierPartAssessorEvaluations',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.SupplierPartAssessorEvaluations(
  Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupplierPartAssessorEvaluations PRIMARY KEY,
  SupplierPartId int NOT NULL,
  PaymentParameterId int NOT NULL,
  AssessorUserId int NOT NULL,
  Score decimal(8,2) NOT NULL,
  CreatedAt datetime2 NOT NULL DEFAULT(sysutcdatetime()),
  UpdatedAt datetime2 NOT NULL DEFAULT(sysutcdatetime()),
  CONSTRAINT FK_SupplierPartAssessorEvaluations_SupplierPart FOREIGN KEY(SupplierPartId) REFERENCES dbo.SupplierParts(Id) ON DELETE CASCADE,
  CONSTRAINT FK_SupplierPartAssessorEvaluations_Parameter FOREIGN KEY(PaymentParameterId) REFERENCES dbo.PaymentParameters(Id) ON DELETE NO ACTION,
  CONSTRAINT FK_SupplierPartAssessorEvaluations_User FOREIGN KEY(AssessorUserId) REFERENCES dbo.AppUsers(Id) ON DELETE NO ACTION
 );
END
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_SupplierPartAssessorEvaluations_Key' AND object_id=OBJECT_ID(N'dbo.SupplierPartAssessorEvaluations'))
 CREATE UNIQUE INDEX UX_SupplierPartAssessorEvaluations_Key ON dbo.SupplierPartAssessorEvaluations(SupplierPartId,PaymentParameterId,AssessorUserId);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_SupplierPartAssessorEvaluations_User' AND object_id=OBJECT_ID(N'dbo.SupplierPartAssessorEvaluations'))
 CREATE INDEX IX_SupplierPartAssessorEvaluations_User ON dbo.SupplierPartAssessorEvaluations(AssessorUserId,UpdatedAt);

IF OBJECT_ID(N'dbo.UserActivitySessions',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.UserActivitySessions(
  Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserActivitySessions PRIMARY KEY,
  UserId int NOT NULL,
  RoleCode nvarchar(100) NOT NULL,
  LoginAtUtc datetime2 NOT NULL,
  LastSeenAtUtc datetime2 NOT NULL,
  LogoutAtUtc datetime2 NULL,
  DurationSeconds int NOT NULL DEFAULT(0),
  CONSTRAINT FK_UserActivitySessions_User FOREIGN KEY(UserId) REFERENCES dbo.AppUsers(Id) ON DELETE CASCADE
 );
END
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_UserActivitySessions_UserLogin' AND object_id=OBJECT_ID(N'dbo.UserActivitySessions'))
 CREATE INDEX IX_UserActivitySessions_UserLogin ON dbo.UserActivitySessions(UserId,LoginAtUtc DESC);
""");
 }
 public static async Task SeedSecurityAsync(AppDbContext db){
  foreach(var d in SecurityPermissions.Definitions)
  {
   if(!await db.Permissions.AnyAsync(x=>x.Code==d.Code))
    db.Permissions.Add(new AppPermission{Code=d.Code,Title=d.Title,GroupTitle=d.GroupTitle,SortOrder=d.SortOrder});
  }
  await db.SaveChangesAsync();

  var manualParameters=await db.PaymentParameters
      .Where(x=>x.ScoringMethod==ParameterScoringMethod.Manual)
      .OrderBy(x=>x.SortOrder).ThenBy(x=>x.Title)
      .ToListAsync();

  foreach(var p in manualParameters)
  {
   var code=$"SupplierPartEvaluation.Parameter.{p.Id}";
   if(!await db.Permissions.AnyAsync(x=>x.Code==code))
    db.Permissions.Add(new AppPermission{Code=code,Title=p.Title,GroupTitle="ارزیابی قطعه–تامین‌کننده",SortOrder=60+p.SortOrder});
  }
  await db.SaveChangesAsync();

  foreach(var def in SecurityPermissions.DefaultRolePermissions)
  {
   var role=await db.Roles.SingleOrDefaultAsync(x=>x.Code==def.Key);
   if(role==null)
   {
    role=new AppRole{
      Code=def.Key,
      Title=def.Key=="SYS_ADMIN"?"مدیر سامانه":
            def.Key=="FINANCE_OPERATOR"?"کارشناس مالی":
            def.Key=="FINANCE_VIEWER"?"ناظر مالی":
            def.Key=="MASTER_DATA"?"مدیر اطلاعات پایه":
            def.Key=="SUPPLIER_PART_ASSESSOR"?"ارزیاب قطعه–تامین‌کننده":def.Key,
      IsSystem=def.Key=="SYS_ADMIN",
      IsActive=true
    };
    db.Roles.Add(role);
    await db.SaveChangesAsync();
   }

   var wantedCodes=def.Value.ToList();
   if(def.Key=="SUPPLIER_PART_ASSESSOR")
     wantedCodes.AddRange(manualParameters.Select(p=>$"SupplierPartEvaluation.Parameter.{p.Id}"));

   var permissionIds=await db.Permissions.Where(x=>wantedCodes.Contains(x.Code)).Select(x=>x.Id).ToListAsync();
   var existing=await db.RolePermissions.Where(x=>x.RoleId==role.Id).Select(x=>x.PermissionId).ToListAsync();
   var missing=permissionIds.Where(x=>!existing.Contains(x)).Select(permissionId=>new AppRolePermission{RoleId=role.Id,PermissionId=permissionId}).ToList();
   if(missing.Count>0)
   {
    db.RolePermissions.AddRange(missing);
    await db.SaveChangesAsync();
   }

  }

  // One-time migration: remove the historical default dashboard grant from the evaluator role.
  // A marker ensures a future administrator can deliberately re-grant it without startup undoing that choice.
  await db.Database.ExecuteSqlRawAsync("""
IF OBJECT_ID(N'dbo.SecurityPermissionMigrations',N'U') IS NULL
 CREATE TABLE dbo.SecurityPermissionMigrations(Version nvarchar(100) NOT NULL CONSTRAINT PK_SecurityPermissionMigrations PRIMARY KEY, AppliedAtUtc datetime2 NOT NULL DEFAULT(sysutcdatetime()));
IF NOT EXISTS(SELECT 1 FROM dbo.SecurityPermissionMigrations WHERE Version=N'RevokeEvaluatorDashboardDefaultV1')
BEGIN
 DELETE rp
 FROM dbo.AppRolePermissions rp
 INNER JOIN dbo.AppRoles r ON r.Id=rp.RoleId
 INNER JOIN dbo.AppPermissions p ON p.Id=rp.PermissionId
 WHERE r.Code=N'SUPPLIER_PART_ASSESSOR' AND p.Code=N'Dashboard.View';
 INSERT INTO dbo.SecurityPermissionMigrations(Version) VALUES(N'RevokeEvaluatorDashboardDefaultV1');
END
""");
 }
 public static async Task SeedAsync(AppDbContext db){
  if(!await db.LookupValues.AnyAsync()){db.LookupValues.AddRange(
  new LookupValue{GroupCode="PART_TYPE",Code="RAW",Title="مواد اولیه",SortOrder=1},new LookupValue{GroupCode="PART_TYPE",Code="SEMIFINISHED",Title="نیمه‌ساخته",SortOrder=2},new LookupValue{GroupCode="PART_TYPE",Code="FINISHED",Title="کالای نهایی",SortOrder=3},new LookupValue{GroupCode="PART_TYPE",Code="PACKAGING",Title="بسته‌بندی",SortOrder=4},
  new LookupValue{GroupCode="SUPPLIER_ACTIVITY",Code="METAL",Title="فلزی",SortOrder=1},new LookupValue{GroupCode="SUPPLIER_ACTIVITY",Code="POLYMER",Title="پلیمری",SortOrder=2},new LookupValue{GroupCode="SUPPLIER_ACTIVITY",Code="ELECTRICAL",Title="برقی/الکترونیکی",SortOrder=3},new LookupValue{GroupCode="SUPPLIER_ACTIVITY",Code="SERVICE",Title="خدماتی",SortOrder=4});await db.SaveChangesAsync();}
  if(!await db.PaymentParameters.AnyAsync()){db.PaymentParameters.AddRange(
  new PaymentParameter{Code="DEBT_AGE",Title="سن بدهی موثر",Type=PaymentParameterType.Time,Weight=25,MaxScore=5,ScoringMethod=ParameterScoringMethod.DebtAgeEffective,SortOrder=1,ScoringGuide="کمتر از 25%: 1 | 25% تا 50%: 2 | 50% تا 75%: 1 | 75% تا مهلت قراردادی و تا 30 روز پس از آن: 4 | بیش از 30 روز پس از مهلت: 5"},
  new PaymentParameter{Code="DEBT_AMOUNT",Title="مبلغ بدهی",Type=PaymentParameterType.Financial,Weight=25,MaxScore=5,ScoringMethod=ParameterScoringMethod.DebtAmount,SortOrder=2,ScoringGuide="کمتر از 3%: 1 | بیش از 3% تا 10%: 2 | بیش از 10% تا 20%: 3 | بیش از 20% تا 35%: 4 | بیش از 35%: 5"},
  new PaymentParameter{Code="PART_PRIORITY",Title="اهمیت قطعه",Type=PaymentParameterType.Part,Weight=25,MaxScore=5,ScoringMethod=ParameterScoringMethod.Manual,SortOrder=3,ScoringGuide="1=بسیار کم، 2=کم، 3=متوسط، 4=زیاد، 5=بسیار حیاتی"},
  new PaymentParameter{Code="SUPPLIER_RELIABILITY",Title="قابلیت اتکای تامین‌کننده",Type=PaymentParameterType.Supplier,Weight=25,MaxScore=5,ScoringMethod=ParameterScoringMethod.Manual,SortOrder=4,ScoringGuide="1=ضعیف، 2=کم، 3=متوسط، 4=خوب، 5=عالی"});await db.SaveChangesAsync();}
 }}
