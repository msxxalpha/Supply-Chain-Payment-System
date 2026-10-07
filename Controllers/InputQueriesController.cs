using Indamin.Payment.Data;
using Indamin.Payment.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Indamin.Payment.Controllers;

[Authorize(Policy = SecurityPermissions.SetupQueries)]
public class InputQueriesController(AppDbContext db, ReportCredentialProtector protector) : Controller
{
    public async Task<IActionResult> Index()
        => View(await db.InputQueries.AsNoTracking().OrderBy(x => x.Title).ToListAsync());

    public IActionResult Create()
        => View("Form", new InputQuery { AuthenticationMode = "sql", TrustServerCertificate = true, CommandTimeoutSeconds = 30 });

    public async Task<IActionResult> Edit(int id)
    {
        var entity = await db.InputQueries.FindAsync(id);
        return entity is null ? NotFound() : View("Form", entity);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(InputQuery model, string? password)
    {
        ModelState.Clear();
        model.Title = model.Title?.Trim() ?? "";
        model.SqlText = model.SqlText?.Trim() ?? "";
        model.ServerInstance = model.ServerInstance?.Trim() ?? "";
        model.DatabaseName = model.DatabaseName?.Trim() ?? "";
        model.AuthenticationMode = model.AuthenticationMode?.Trim().ToLowerInvariant() == "windows" ? "windows" : "sql";
        model.Username = model.Username?.Trim() ?? "";
        model.CommandTimeoutSeconds = Math.Clamp(model.CommandTimeoutSeconds <= 0 ? 30 : model.CommandTimeoutSeconds, 5, 300);

        if (string.IsNullOrWhiteSpace(model.Title))
            ModelState.AddModelError(nameof(model.Title), "عنوان کوئری الزامی است.");
        if (string.IsNullOrWhiteSpace(model.SqlText))
            ModelState.AddModelError(nameof(model.SqlText), "متن SQL الزامی است.");
        if (!InputQueryService.IsReadOnlyQuery(model.SqlText))
            ModelState.AddModelError(nameof(model.SqlText), "کوئری فقط باید SELECT یا WITH خواندنی باشد و نباید شامل دستورات تغییر داده یا چند دستور SQL باشد.");
        if (string.IsNullOrWhiteSpace(model.ServerInstance))
            ModelState.AddModelError(nameof(model.ServerInstance), "نام سرور/اینستنس الزامی است.");
        if (string.IsNullOrWhiteSpace(model.DatabaseName))
            ModelState.AddModelError(nameof(model.DatabaseName), "نام پایگاه داده الزامی است.");

        if (model.AuthenticationMode == "sql")
        {
            if (string.IsNullOrWhiteSpace(model.Username))
                ModelState.AddModelError(nameof(model.Username), "برای احراز هویت SQL، نام کاربری الزامی است.");

            var hasStoredPassword = model.Id > 0 && await db.InputQueries.AsNoTracking()
                .Where(x => x.Id == model.Id)
                .Select(x => x.PasswordProtected)
                .AnyAsync(x => !string.IsNullOrEmpty(x));

            if (string.IsNullOrWhiteSpace(password) && !hasStoredPassword)
                ModelState.AddModelError("password", "برای احراز هویت SQL، کلمه عبور الزامی است.");
        }

        if (await db.InputQueries.AnyAsync(x => x.Id != model.Id && x.Title == model.Title))
            ModelState.AddModelError(nameof(model.Title), "این عنوان قبلاً ثبت شده است.");

        if (!ModelState.IsValid)
            return View("Form", model);

        try
        {
            InputQuery entity;
            if (model.Id == 0)
            {
                entity = new InputQuery
                {
                    Title = model.Title,
                    SqlText = model.SqlText,
                    ServerInstance = model.ServerInstance,
                    DatabaseName = model.DatabaseName,
                    AuthenticationMode = model.AuthenticationMode,
                    Username = model.Username,
                    PasswordProtected = string.IsNullOrWhiteSpace(password) ? "" : protector.Protect(password),
                    Encrypt = model.Encrypt,
                    TrustServerCertificate = model.TrustServerCertificate,
                    Enabled = model.Enabled,
                    CommandTimeoutSeconds = model.CommandTimeoutSeconds,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                db.InputQueries.Add(entity);
            }
            else
            {
                entity = await db.InputQueries.FindAsync(model.Id)
                    ?? throw new InvalidOperationException("کوئری یافت نشد.");

                entity.Title = model.Title;
                entity.SqlText = model.SqlText;
                entity.ServerInstance = model.ServerInstance;
                entity.DatabaseName = model.DatabaseName;
                entity.AuthenticationMode = model.AuthenticationMode;
                entity.Username = model.Username;
                entity.Encrypt = model.Encrypt;
                entity.TrustServerCertificate = model.TrustServerCertificate;
                entity.Enabled = model.Enabled;
                entity.CommandTimeoutSeconds = model.CommandTimeoutSeconds;
                if (!string.IsNullOrWhiteSpace(password))
                    entity.PasswordProtected = protector.Protect(password);
                if (model.AuthenticationMode == "windows")
                {
                    entity.Username = "";
                    entity.PasswordProtected = "";
                }
                entity.UpdatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync();
            db.AuditLogs.Add(new AuditLog
            {
                Action = model.Id == 0 ? "CREATE" : "UPDATE",
                Entity = "InputQuery",
                EntityId = entity.Id.ToString(),
                Details = $"کوئری ورودی: {entity.Title}",
                UserId = int.TryParse(User.FindFirst("UserId")?.Value, out var uid) ? uid : 0
            });
            await db.SaveChangesAsync();
            TempData["Result"] = "کوئری با موفقیت ذخیره شد.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", "ذخیره کوئری انجام نشد: " + ex.Message);
            return View("Form", model);
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await db.InputQueries.FindAsync(id);
        if (entity is not null)
        {
            db.InputQueries.Remove(entity);
            await db.SaveChangesAsync();
            TempData["Result"] = "کوئری حذف شد.";
        }
        return RedirectToAction(nameof(Index));
    }
}
