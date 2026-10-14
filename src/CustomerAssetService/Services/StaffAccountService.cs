using CustomerAssetService.DTOs;
using CustomerAssetService.Models;
using CustomerAssetService.Repositories;
using CustomerAssetService.Security;
using MySqlConnector;

namespace CustomerAssetService.Services;

public sealed class StaffAccountService
{
    private readonly IStaffAccountRepository _repository;
    private readonly ITechnicianAccountRepository _technicianRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IDispatchTechnicianVerifier _verifier;

    public StaffAccountService(
        IStaffAccountRepository repository,
        ITechnicianAccountRepository technicianRepository,
        IPasswordHasher passwordHasher,
        IDispatchTechnicianVerifier verifier)
    {
        _repository = repository;
        _technicianRepository = technicianRepository;
        _passwordHasher = passwordHasher;
        _verifier = verifier;
    }

    private static readonly HashSet<string> SupportedRoles =
        [StaffRoles.Agent, StaffRoles.Dispatcher, StaffRoles.Technician, StaffRoles.Manager];

    /// <summary>Creates a new staff account. Returns a Result indicating success or a typed failure.</summary>
    public async Task<Result<StaffAccount>> CreateAsync(
        CreateStaffAccountRequest request, string creatingManagerId, string? authorization, CancellationToken cancellationToken)
    {
        if (!SupportedRoles.Contains(request.Role))
            return Result<StaffAccount>.Failure(ServiceError.ValidationFailed);

        // When creating a Technician, verify the Dispatch technician record exists and is not already linked.
        string? technicianId = null;
        if (request.Role == StaffRoles.Technician)
        {
            if (string.IsNullOrWhiteSpace(request.TechnicianId))
                return Result<StaffAccount>.Failure(ServiceError.TechnicianRequired);

            technicianId = request.TechnicianId.Trim();

            // Verify technician exists in Dispatch service
            if (!string.IsNullOrEmpty(authorization))
            {
                var status = await _verifier.VerifyAsync(technicianId, authorization, cancellationToken);
                if (status == 404) return Result<StaffAccount>.Failure(ServiceError.TechnicianNotFound);
                if (status != 200) return Result<StaffAccount>.Failure(ServiceError.DispatchUnavailable);
            }

            // Verify technician is not already linked to another staff account
            if (await _technicianRepository.FindByTechnicianIdAsync(technicianId) is not null)
                return Result<StaffAccount>.Failure(ServiceError.DuplicateIdentifier);
        }

        // Check uniqueness of username and email
        if (await _repository.FindByIdentifierAsync(request.Username) is not null)
            return Result<StaffAccount>.Failure(ServiceError.DuplicateIdentifier);
        if (await _repository.FindByIdentifierAsync(request.Email) is not null)
            return Result<StaffAccount>.Failure(ServiceError.DuplicateIdentifier);

        var account = new StaffAccount
        {
            Id = Guid.NewGuid().ToString(),
            FullName = request.FullName.Trim(),
            Username = request.Username.Trim(),
            Email = request.Email.Trim(),
            PasswordHash = _passwordHasher.Hash(request.Password),
            Role = request.Role,
            Status = "ACTIVE",
            TechnicianId = technicianId,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = creatingManagerId
        };

        try
        {
            await _repository.CreateAsync(account);
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            return Result<StaffAccount>.Failure(ServiceError.DuplicateIdentifier);
        }

        return Result<StaffAccount>.Success(account);
    }

    public async Task<IReadOnlyList<StaffAccount>> GetAllActiveAsync() =>
        await _repository.GetAllActiveAsync();

    public async Task<IReadOnlyList<StaffAccount>> GetAllAsync(string? status = null) =>
        await _repository.GetAllAsync(status);

    public async Task<StaffAccount?> GetByIdAsync(string id) =>
        await _repository.FindByIdAsync(id);

    public async Task<Result<StaffAccount>> DeactivateAsync(
        string accountId, string currentManagerId, string? authorization = null, CancellationToken cancellationToken = default)
    {
        var account = await _repository.FindByIdAsync(accountId);
        if (account is null)
            return Result<StaffAccount>.Failure(ServiceError.NotFound);

        // Self-deactivation safeguard: A Manager cannot deactivate their own currently authenticated account
        if (string.Equals(account.Id, currentManagerId, StringComparison.OrdinalIgnoreCase))
            return Result<StaffAccount>.Failure(ServiceError.SelfDeactivationNotAllowed);

        // Repeated request safety for non-technicians or already synchronized technicians
        if (string.Equals(account.Status, "INACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            if (account.Role != StaffRoles.Technician || string.IsNullOrEmpty(account.TechnicianId) ||
                string.Equals(account.DispatchSyncStatus, "SYNCHRONIZED", StringComparison.OrdinalIgnoreCase))
            {
                return Result<StaffAccount>.Success(account);
            }
        }

        // Technician-Dispatch synchronization safeguard
        string? syncStatus = null;
        if (string.Equals(account.Role, StaffRoles.Technician, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(account.TechnicianId))
        {
            var outcome = await _verifier.DeactivateTechnicianAsync(
                account.TechnicianId.Trim(), authorization ?? string.Empty, cancellationToken);

            if (outcome == DispatchDeactivationOutcome.HasOpenAssignments)
            {
                // Open work blocks deactivation to preserve existing assignments and history
                return Result<StaffAccount>.Failure(ServiceError.TechnicianHasOpenAssignments);
            }

            if (outcome == DispatchDeactivationOutcome.Success)
            {
                syncStatus = "SYNCHRONIZED";
            }
            else if (outcome == DispatchDeactivationOutcome.Unavailable)
            {
                // Dispatch outage: revoke staff login access immediately, but expose pending synchronization state with safe retry
                syncStatus = "PENDING_RETRY";
            }
            else
            {
                syncStatus = "SYNCHRONIZED";
            }
        }

        // If account was already inactive (recovery retry flow when Dispatch is restored):
        if (string.Equals(account.Status, "INACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            if (syncStatus != null && syncStatus != account.DispatchSyncStatus)
            {
                account.DispatchSyncStatus = syncStatus;
                await _repository.UpdateDispatchSyncStatusAsync(accountId, syncStatus);
            }
            return Result<StaffAccount>.Success(account);
        }

        // Last-Manager safeguard: The system must not allow deactivation of the last active Manager account
        if (string.Equals(account.Role, StaffRoles.Manager, StringComparison.OrdinalIgnoreCase))
        {
            var activeManagers = await _repository.CountActiveManagersAsync();
            if (activeManagers <= 1)
                return Result<StaffAccount>.Failure(ServiceError.LastActiveManager);
        }

        var deactivationResult = await _repository.DeactivateAsync(accountId, syncStatus);
        if (deactivationResult == DeactivateStaffAccountResult.NotFound)
            return Result<StaffAccount>.Failure(ServiceError.NotFound);

        if (deactivationResult == DeactivateStaffAccountResult.LastActiveManager)
            return Result<StaffAccount>.Failure(ServiceError.LastActiveManager);

        account.Status = "INACTIVE";
        account.DispatchSyncStatus = syncStatus;
        return Result<StaffAccount>.Success(account);
    }
}
