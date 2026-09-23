using Indamin.Payment.Data;using Microsoft.EntityFrameworkCore;
namespace Indamin.Payment.Services;
public class CompanySettingsService(AppDbContext db){
 public async Task<CompanySettings> GetAsync(){var x=await db.CompanySettings.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==1);return x??new CompanySettings();}
}