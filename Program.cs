using System.Security.Cryptography;using System.Security.Claims;using Microsoft.AspNetCore.Authentication;using Microsoft.AspNetCore.Authentication.Cookies;using Microsoft.EntityFrameworkCore;using Indamin.Payment.Data;using Indamin.Payment.Services;
var b=WebApplication.CreateBuilder(args);
var connectionString=b.Configuration.GetConnectionString("Default");
if(string.IsNullOrWhiteSpace(connectionString))
    connectionString=Environment.GetEnvironmentVariable("SQLSERVER_CONNECTION_STRING");
if(string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Connection string is not configured. Set ConnectionStrings:Default (or ConnectionStrings__Default in IIS/environment) to a SQL Server connection string. For SQL authentication use User ID and Password; do not use Trusted_Connection/Integrated Security unless Windows authentication is intentionally configured for the application identity.");
b.Services.AddControllersWithViews();b.Services.AddDataProtection();b.Services.AddDbContext<AppDbContext>(o=>o.UseSqlServer(connectionString));b.Services.AddScoped<CompanySettingsService>();b.Services.AddScoped<ExcelService>();b.Services.AddScoped<PaymentCalculationService>();b.Services.AddScoped<SupplierPriceDebtAdjustmentService>();b.Services.AddScoped<PaymentOrderPdfService>();b.Services.AddScoped<SupplierPriorityService>();b.Services.AddScoped<FinancialReportingService>();b.Services.AddScoped<ReportCredentialProtector>();b.Services.AddScoped<InputQueryService>();b.Services.AddDistributedMemoryCache();b.Services.AddSession(o=>{o.Cookie.HttpOnly=true;o.Cookie.IsEssential=true;o.IdleTimeout=TimeSpan.FromHours(4);});b.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.AccessDeniedPath = "/Account/Denied";
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;
    // Rebuild role and permission claims from the database for each authenticated request.
    // This keeps active role/user changes from being hidden behind an old authentication cookie.
    o.Events.OnValidatePrincipal = async context =>
    {
        var identity = context.Principal?.Identities.FirstOrDefault(x => x.IsAuthenticated);
        var userIdValue = identity?.FindFirst("UserId")?.Value;
        if (identity is null || !int.TryParse(userIdValue, out var userId))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && x.IsActive);
        if (user is null)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        var roleCodes = await db.UserRoles
            .Where(x => x.UserId == user.Id && x.Role != null && x.Role.IsActive)
            .Select(x => x.Role!.Code)
            .Distinct()
            .ToListAsync();

        var permissionCodes = await (
            from userRole in db.UserRoles
            join role in db.Roles on userRole.RoleId equals role.Id
            join rolePermission in db.RolePermissions on role.Id equals rolePermission.RoleId
            join permission in db.Permissions on rolePermission.PermissionId equals permission.Id
            where userRole.UserId == user.Id && role.IsActive && permission.Code != ""
            select permission.Code)
            .Distinct()
            .ToListAsync();

        if (user.IsAdmin)
        {
            var parameterCodes = await db.PaymentParameters
                .Where(x => x.IsActive && x.Weight > 0 && x.ScoringMethod == ParameterScoringMethod.Manual)
                .Select(x => "SupplierPartEvaluation.Parameter." + x.Id)
                .ToListAsync();
            permissionCodes = SecurityPermissions.AllCodes
                .Concat(parameterCodes)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        var oldRoles = identity.FindAll(ClaimTypes.Role).Select(x => x.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var oldPermissions = identity.FindAll(SecurityPermissions.Claim).Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
        var oldAdmin = identity.FindFirst("IsAdmin")?.Value;
        var oldName = identity.FindFirst(ClaimTypes.Name)?.Value;
        var changed = !oldRoles.SetEquals(roleCodes)
            || !oldPermissions.SetEquals(permissionCodes)
            || oldAdmin != (user.IsAdmin ? "1" : "0")
            || oldName != user.DisplayName;

        foreach (var claim in identity.FindAll(ClaimTypes.Role).ToList())
            identity.RemoveClaim(claim);
        foreach (var claim in identity.FindAll(SecurityPermissions.Claim).ToList())
            identity.RemoveClaim(claim);
        foreach (var claim in identity.FindAll("IsAdmin").ToList())
            identity.RemoveClaim(claim);
        foreach (var claim in identity.FindAll(ClaimTypes.Name).ToList())
            identity.RemoveClaim(claim);

        identity.AddClaim(new Claim(ClaimTypes.Name, user.DisplayName));
        identity.AddClaim(new Claim("IsAdmin", user.IsAdmin ? "1" : "0"));
        identity.AddClaims(roleCodes.Select(x => new Claim(ClaimTypes.Role, x)));
        identity.AddClaims(permissionCodes.Select(x => new Claim(SecurityPermissions.Claim, x)));
        if (changed)
            context.ShouldRenew = true;
    };
});b.Services.AddAuthorization(o=>{o.AddPolicy("AdminOnly",p=>p.RequireClaim("IsAdmin","1"));foreach(var d in SecurityPermissions.Definitions)o.AddPolicy("Permission:"+d.Code,p=>p.RequireClaim(SecurityPermissions.Claim,d.Code));});
var app=b.Build();using(var sc=app.Services.CreateScope()){var db=sc.ServiceProvider.GetRequiredService<AppDbContext>();await DatabaseInitializer.InitializeAsync(db);await DatabaseInitializer.EnsureSupplierInitialClaimAsync(db);await DatabaseInitializer.EnsurePaymentSnapshotSchemaAsync(db);await DatabaseInitializer.EnsurePaymentLifecycleSchemaAsync(db);await DatabaseInitializer.EnsureAdvancedPaymentSchemaAsync(db);await DatabaseInitializer.EnsureSecuritySchemaAsync(db);await DatabaseInitializer.EnsureCompanySettingsAsync(db);await DatabaseInitializer.EnsureQueryAndPriceListSchemaAsync(db);await DatabaseInitializer.EnsurePriceDebtAdjustmentSchemaAsync(db);await DatabaseInitializer.EnsureSupplierPartAssessmentSchemaAsync(db);await DatabaseInitializer.SeedAsync(db);await DatabaseInitializer.SeedSecurityAsync(db);await DatabaseInitializer.SeedSystemParametersAsync(db);var pwd=app.Configuration["InitialAdminPassword"];if(string.IsNullOrWhiteSpace(pwd))throw new InvalidOperationException("InitialAdminPassword باید تنظیم شود.");if(!await db.Users.AnyAsync(x=>x.IsAdmin)){db.Users.Add(new AppUser{UserName="admin",DisplayName="مدیر سامانه پرداخت",IsAdmin=true,IsActive=true,PasswordHash=PasswordHasher.Hash(pwd)});await db.SaveChangesAsync();}await DatabaseInitializer.EnsureAdministratorRoleAssignmentAsync(db);await DatabaseInitializer.EnsureEvaluatorDefaultScoresAsync(db);}
if(!app.Environment.IsDevelopment())app.UseExceptionHandler("/Home/Error");app.UseStaticFiles();app.UseRouting();app.UseSession();app.UseAuthentication();app.UseAuthorization();
app.Use(async (context,next)=>{
    await next();
    if(context.User.Identity?.IsAuthenticated==true && context.User.IsInRole("SUPPLIER_PART_ASSESSOR"))
    {
        var userIdValue=context.User.FindFirst("UserId")?.Value;
        if(int.TryParse(userIdValue,out var userId))
        {
            try
            {
                var db=context.RequestServices.GetRequiredService<AppDbContext>();
                var now=DateTime.UtcNow;
                var session=await db.UserActivitySessions
                    .Where(x=>x.UserId==userId && x.LogoutAtUtc==null)
                    .OrderByDescending(x=>x.LoginAtUtc)
                    .FirstOrDefaultAsync();
                if(session!=null)
                {
                    session.LastSeenAtUtc=now;
                    session.DurationSeconds=Math.Max(0,(int)Math.Min(int.MaxValue,(now-session.LoginAtUtc).TotalSeconds));
                    await db.SaveChangesAsync();
                }
            }
            catch
            {
                // Activity logging must never break the user's request.
            }
        }
    }
});
app.MapControllerRoute("default","{controller=Home}/{action=Index}/{id?}");app.Run();
public static class PasswordHasher{public static string Hash(string v){var s=RandomNumberGenerator.GetBytes(16);var k=Rfc2898DeriveBytes.Pbkdf2(v,s,100000,System.Security.Cryptography.HashAlgorithmName.SHA256,32);return $"100000.{Convert.ToBase64String(s)}.{Convert.ToBase64String(k)}";}public static bool Verify(string v,string st){try{var p=st.Split('.');if(p.Length!=3)return false;var s=Convert.FromBase64String(p[1]);var e=Convert.FromBase64String(p[2]);var k=Rfc2898DeriveBytes.Pbkdf2(v,s,int.Parse(p[0]),System.Security.Cryptography.HashAlgorithmName.SHA256,e.Length);return CryptographicOperations.FixedTimeEquals(k,e);}catch{return false;}}}
