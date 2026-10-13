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

    public async Task<Result<StaffAccount>> DeactivateAsync(string accountId, string currentManagerId)
    {
        var account = await _repository.FindByIdAsync(accountId);
        if (account is null)
            return Result<StaffAccount>.Failure(ServiceError.NotFound);

        // Self-deactivation safeguard: A Manager cannot deactivate their own currently authenticated account
        if (string.Equals(account.Id, currentManagerId, StringComparison.OrdinalIgnoreCase))
            return Result<StaffAccount>.Failure(ServiceError.SelfDeactivationNotAllowed);

        // Repeated request safety: Already inactive is handled idempotently without side effects
        if (string.Equals(account.Status, "INACTIVE", StringComparison.OrdinalIgnoreCase))
            return Result<StaffAccount>.Success(account);

        // Last-Manager safeguard: The system must not allow deactivation of the last active Manager account
        if (string.Equals(account.Role, StaffRoles.Manager, StringComparison.OrdinalIgnoreCase))
        {
            var activeManagers = await _repository.CountActiveManagersAsync();
            if (activeManagers <= 1)
                return Result<StaffAccount>.Failure(ServiceError.LastActiveManager);
        }

        var deactivationResult = await _repository.DeactivateAsync(accountId);
        if (deactivationResult == DeactivateStaffAccountResult.NotFound)
            return Result<StaffAccount>.Failure(ServiceError.NotFound);

        if (deactivationResult == DeactivateStaffAccountResult.LastActiveManager)
            return Result<StaffAccount>.Failure(ServiceError.LastActiveManager);

        account.Status = "INACTIVE";
        return Result<StaffAccount>.Success(account);
    }
}
