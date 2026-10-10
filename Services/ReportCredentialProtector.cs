using Microsoft.AspNetCore.DataProtection;

namespace Indamin.Payment.Services;

public sealed class ReportCredentialProtector(IDataProtectionProvider provider)
{
    readonly IDataProtector protector = provider.CreateProtector("Indamin.Payment.InputQueries.SqlCredential.v1");

    public string Protect(string value) => string.IsNullOrWhiteSpace(value) ? "" : protector.Protect(value);

    public string Unprotect(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        try { return protector.Unprotect(value); }
        catch { return ""; }
    }
}
