using CustomerAssetService.DTOs;
using CustomerAssetService.Repositories;
using CustomerAssetService.Security;

namespace CustomerAssetService.Services;

public sealed class AuthenticationService
{
    private readonly IStaffAccountRepository _repository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;

    public AuthenticationService(IStaffAccountRepository repository, IPasswordHasher passwordHasher, ITokenService tokenService)
    {
        _repository = repository;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
    }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request)
    {
        var account = await _repository.FindByIdentifierAsync(request.Identifier);
        if (account is null || !string.Equals(account.Status, "ACTIVE", StringComparison.Ordinal) ||
            !_passwordHasher.Verify(request.Password, account.PasswordHash))
        {
            return null;
        }

        return _tokenService.Issue(account);
    }
}
