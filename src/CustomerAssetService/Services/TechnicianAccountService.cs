using CustomerAssetService.DTOs;
using CustomerAssetService.Models;
using CustomerAssetService.Repositories;
using CustomerAssetService.Security;
using MySqlConnector;

namespace CustomerAssetService.Services;

public sealed class TechnicianAccountService(ITechnicianAccountRepository repository, IPasswordHasher hasher)
{
    public Task<StaffAccount?> GetAsync(string technicianId) => repository.FindByTechnicianIdAsync(technicianId);

    public async Task<StaffAccount?> CreateAsync(string technicianId, CreateTechnicianAccountRequest request)
    {
        // Repeated requests cannot reset a password or silently link a different login.
        if (await repository.FindByTechnicianIdAsync(technicianId) is not null ||
            await repository.FindByIdentifierAsync(request.Username) is not null ||
            await repository.FindByIdentifierAsync(request.Email) is not null) return null;
        var account = new StaffAccount
        {
            Id = Guid.NewGuid().ToString(), Username = request.Username.Trim(), Email = request.Email.Trim(),
            PasswordHash = hasher.Hash(request.Password), Role = StaffRoles.Technician, Status = "ACTIVE",
            TechnicianId = technicianId
        };
        try { await repository.CreateAsync(account); }
        catch (MySqlException ex) when (ex.Number == 1062) { return null; }
        return account;
    }

    public async Task<StaffAccount?> LinkAsync(string technicianId, string identifier)
    {
        var account = await repository.FindByIdentifierAsync(identifier);
        if (account is null || account.Role != StaffRoles.Technician || account.Status != "ACTIVE" ||
            (account.TechnicianId is not null && account.TechnicianId != technicianId)) return null;
        var existing = await repository.FindByTechnicianIdAsync(technicianId);
        if (existing is not null) return existing.Id == account.Id ? existing : null;
        try
        {
            if (!await repository.LinkTechnicianAsync(account.Id, technicianId)) return null;
        }
        catch (MySqlException ex) when (ex.Number == 1062) { return null; }
        return await repository.FindByTechnicianIdAsync(technicianId);
    }
}
