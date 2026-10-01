using CustomerAssetService.Models;

namespace CustomerAssetService.Repositories;

public interface ITechnicianAccountRepository : IStaffAccountRepository
{
    Task<StaffAccount?> FindByTechnicianIdAsync(string technicianId);
    Task<bool> LinkTechnicianAsync(string accountId, string technicianId);
}
