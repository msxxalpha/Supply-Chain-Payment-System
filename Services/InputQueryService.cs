using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Indamin.Payment.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Indamin.Payment.Services;

public sealed record WarehouseQueryResult(bool Success, List<ImportedPaymentInvoice> Rows, List<string> Errors, string Message);

public class InputQueryService(AppDbContext db, ReportCredentialProtector credentialProtector)
{
    public async Task<WarehouseQueryResult> ExecuteInventoryReceiptsAsync(DateTime fromDate, DateTime toDate)
    {
        var query = await db.InputQueries.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Title == "اطلاعات رسیدهای خرید انبار" && x.Enabled);

        if (query is null)
            return new(false, [], ["کوئری فعال با عنوان دقیقاً «اطلاعات رسیدهای خرید انبار» در بخش مدیریت کوئری‌ها ثبت نشده است."], "");

        if (!IsReadOnlyQuery(query.SqlText))
            return new(false, [], ["کوئری اطلاعات رسیدهای خرید انبار فقط باید یک SELECT یا CTE خواندنی باشد؛ دستورات تغییر داده مجاز نیستند."], "");

        try
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = query.ServerInstance.Trim(),
                InitialCatalog = query.DatabaseName.Trim(),
                Encrypt = query.Encrypt,
                TrustServerCertificate = query.TrustServerCertificate,
                ConnectTimeout = 15,
                ApplicationName = "Indamin.Payment-InputQuery"
            };

            var mode = (query.AuthenticationMode ?? "sql").Trim().ToLowerInvariant();
            if (mode is "windows" or "integrated")
            {
                builder.IntegratedSecurity = true;
            }
            else
            {
                var password = credentialProtector.Unprotect(query.PasswordProtected);
                if (string.IsNullOrWhiteSpace(query.Username) || string.IsNullOrEmpty(password))
                    return new(false, [], [$"اطلاعات کاربری SQL برای کوئری «{query.Title}» کامل نیست."], "");

                builder.UserID = query.Username.Trim();
                builder.Password = password;
                builder.IntegratedSecurity = false;
            }

            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = query.SqlText.Trim();
            command.CommandType = CommandType.Text;
            command.CommandTimeout = Math.Clamp(query.CommandTimeoutSeconds <= 0 ? 30 : query.CommandTimeoutSeconds, 5, 300);

            command.Parameters.Add(new SqlParameter("@FromDate", SqlDbType.DateTime2) { Value = fromDate.Date });
            command.Parameters.Add(new SqlParameter("@ToDate", SqlDbType.DateTime2) { Value = toDate.Date.AddDays(1).AddTicks(-1) });

            await using var reader = await command.ExecuteReaderAsync();
            var ordinals = BuildOrdinals(reader);
            var required = new[] { "شماره رسید", "انبار", "نام کالا", "نام تامین کننده", "مقدار رسید", "تاریخ رسید" };
            var errors = required
                .Where(x => !ordinals.ContainsKey(NormalizeHeader(x)))
                .Select(x => $"ستون خروجی «{x}» در نتیجه کوئری «{query.Title}» وجود ندارد.")
                .ToList();

            if (errors.Count > 0)
                return new(false, [], errors, "");

            var rows = new List<ImportedPaymentInvoice>();
            var rowNumber = 1;
            while (await reader.ReadAsync())
            {
                try
                {
                    var receipt = ReadString(reader, ordinals[NormalizeHeader("شماره رسید")]);
                    if (string.IsNullOrWhiteSpace(receipt))
                    {
                        rowNumber++;
                        continue;
                    }

                    var warehouse = ReadString(reader, ordinals[NormalizeHeader("انبار")]);
                    var part = ReadString(reader, ordinals[NormalizeHeader("نام کالا")]);
                    var supplier = ReadString(reader, ordinals[NormalizeHeader("نام تامین کننده")]);
                    var quantity = ReadDecimal(reader, ordinals[NormalizeHeader("مقدار رسید")], "مقدار رسید");
                    var receiptDate = ReadDate(reader, ordinals[NormalizeHeader("تاریخ رسید")], "تاریخ رسید");

                    decimal debt = 0;
                    var debtKey = NormalizeHeader("مبلغ بدهی");
                    if (ordinals.TryGetValue(debtKey, out var debtOrdinal) && !reader.IsDBNull(debtOrdinal))
                        debt = ReadDecimal(reader, debtOrdinal, "مبلغ بدهی");

                    if (string.IsNullOrWhiteSpace(warehouse) || string.IsNullOrWhiteSpace(part) || string.IsNullOrWhiteSpace(supplier))
                    {
                        errors.Add($"سطر {rowNumber}: انبار، نام کالا و نام تامین‌کننده نمی‌تواند خالی باشد.");
                    }
                    else if (quantity <= 0)
                    {
                        errors.Add($"سطر {rowNumber}: مقدار رسید باید بزرگ‌تر از صفر باشد.");
                    }
                    else if (receiptDate.Date < fromDate.Date || receiptDate.Date > toDate.Date)
                    {
                        errors.Add($"سطر {rowNumber}: تاریخ رسید خارج از بازه انتخاب‌شده است.");
                    }
                    else
                    {
                        rows.Add(new ImportedPaymentInvoice(
                            rowNumber, receipt, warehouse, part, supplier,
                            Math.Round(quantity, 6), Math.Round(debt, 2), receiptDate.Date));
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"سطر {rowNumber}: {ex.Message}");
                }

                rowNumber++;
            }

            return errors.Count == 0
                ? new(true, rows, [], $"تعداد {rows.Count:N0} رسید از زیرسیستم انبار دریافت شد.")
                : new(false, [], errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), "");
        }
        catch (SqlException ex)
        {
            return new(false, [], [$"اجرای کوئری «{query.Title}» ناموفق بود: {ex.Message}"], "");
        }
        catch (Exception ex)
        {
            return new(false, [], [$"خطا در اجرای کوئری «{query.Title}»: {ex.Message}"], "");
        }
    }

    public static bool IsReadOnlyQuery(string sql)
    {
        // Remove SQL single-line and block comments before validating the first statement.
        var text = Regex.Replace(
            sql ?? "",
            @"--.*?$|/\*.*?\*/",
            "",
            RegexOptions.Multiline | RegexOptions.Singleline).Trim();

        // Allow one optional trailing semicolon; reject any other statement separator.
        text = Regex.Replace(text, @";\s*$", "").Trim();

        if (text.Length == 0 || text.Contains(';') ||
            !Regex.IsMatch(text, @"^(SELECT|WITH)\b", RegexOptions.IgnoreCase))
            return false;

        // Only read-only SELECT/CTE statements are allowed for managed input queries.
        return !Regex.IsMatch(
            text,
            @"\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|TRUNCATE|CREATE|EXEC|EXECUTE|GRANT|REVOKE|DENY)\b",
            RegexOptions.IgnoreCase);
    }

    static Dictionary<string, int> BuildOrdinals(IDataRecord reader)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
            result[NormalizeHeader(reader.GetName(i))] = i;
        return result;
    }

    static string ReadString(IDataRecord reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? "" : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture)?.Trim() ?? "";

    static decimal ReadDecimal(IDataRecord reader, int ordinal, string title)
    {
        if (reader.IsDBNull(ordinal)) return 0;
        var value = reader.GetValue(ordinal);
        if (value is decimal d) return d;
        if (value is double db) return Convert.ToDecimal(db);
        if (value is float f) return Convert.ToDecimal(f);
        var s = PersianDateService.ToLatinDigits(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "")
            .Replace(",", "").Replace("٬", "").Trim();
        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
            return result;
        throw new FormatException($"{title} نامعتبر است.");
    }

    static DateTime ReadDate(IDataRecord reader, int ordinal, string title)
    {
        if (reader.IsDBNull(ordinal)) throw new FormatException($"{title} خالی است.");

        var value = reader.GetValue(ordinal);
        if (value is DateTime dt) return dt.Date;
        if (value is DateTimeOffset dto) return dto.Date;

        try
        {
            return ParseQueryResultDate(
                Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
        }
        catch
        {
            throw new FormatException($"{title} نامعتبر است.");
        }
    }

    public static DateTime ParseQueryResultDate(string value)
    {
        var s = PersianDateService.ToLatinDigits(value?.Trim() ?? "");
        if (string.IsNullOrWhiteSpace(s))
            throw new FormatException("تاریخ خالی است.");

        var datePart = s.Split(new[] { ' ', 'T' }, 2, StringSplitOptions.RemoveEmptyEntries)[0];

        // SQL warehouse queries commonly return Jalali text, e.g. 1405/07/15.
        // Parse 13xx/14xx as Jalali before generic DateTime parsing so .NET
        // does not interpret 1405 as a Gregorian year.
        if (Regex.IsMatch(datePart, @"^1[34]\d{2}[-/]\d{1,2}[-/]\d{1,2}$"))
        {
            try
            {
                return PersianDateService.Parse(datePart.Replace('-', '/')).Date;
            }
            catch (ArgumentException)
            {
                throw new FormatException("تاریخ شمسی نامعتبر است.");
            }
        }

        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var gregorian))
            return gregorian.Date;

        try
        {
            return PersianDateService.Parse(datePart).Date;
        }
        catch
        {
            throw new FormatException("تاریخ نامعتبر است.");
        }
    }

    static string NormalizeHeader(string value) =>
        (value ?? "").Trim().Replace("ي", "ی").Replace("ك", "ک").ToLowerInvariant();
}
