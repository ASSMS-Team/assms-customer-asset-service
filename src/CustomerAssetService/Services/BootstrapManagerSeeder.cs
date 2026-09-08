using CustomerAssetService.Models;
using CustomerAssetService.Repositories;
using CustomerAssetService.Security;

namespace CustomerAssetService.Services;

public sealed class BootstrapManagerSeeder
{
    private readonly IStaffAccountRepository _repository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<BootstrapManagerSeeder> _logger;

    public BootstrapManagerSeeder(IStaffAccountRepository repository, IPasswordHasher passwordHasher,
        IConfiguration configuration, IHostEnvironment environment, ILogger<BootstrapManagerSeeder> logger)
    {
        _repository = repository;
        _passwordHasher = passwordHasher;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public async Task EnsureCreatedAsync()
    {
        if (!await _repository.AnyManagerAsync())
        {
            await CreateBootstrapManagerAsync();
        }

        if (_environment.IsDevelopment() || _environment.IsEnvironment("Staging"))
        {
            await CreateApprovedSeedAccountsAsync();
        }
    }

    private async Task CreateBootstrapManagerAsync()
    {

        var username = _configuration["Authentication:BootstrapManager:Username"];
        var email = _configuration["Authentication:BootstrapManager:Email"];
        var password = _configuration["Authentication:BootstrapManager:Password"];
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning("No Manager exists and bootstrap Manager configuration is incomplete.");
            return;
        }

        await _repository.CreateAsync(new StaffAccount
        {
            Id = Guid.NewGuid().ToString(),
            Username = username.Trim(),
            Email = email.Trim(),
            PasswordHash = _passwordHasher.Hash(password),
            Role = StaffRoles.Manager,
            Status = "ACTIVE"
        });
        _logger.LogInformation("Bootstrap Manager account created for {Username}. Password and hash were not logged.", username);
    }

    private async Task CreateApprovedSeedAccountsAsync()
    {
        foreach (var seed in _configuration.GetSection("Authentication:SeedAccounts").GetChildren())
        {
            var username = seed["Username"]?.Trim();
            var email = seed["Email"]?.Trim();
            var password = seed["Password"];
            var role = seed["Role"]?.Trim();
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(password) || role is null ||
                !new[] { StaffRoles.Agent, StaffRoles.Dispatcher, StaffRoles.Technician, StaffRoles.Manager }.Contains(role))
            {
                _logger.LogWarning("An approved seed StaffAccount entry is incomplete or has an unsupported role; it was skipped.");
                continue;
            }

            if (await _repository.FindByIdentifierAsync(email) is not null) continue;
            await _repository.CreateAsync(new StaffAccount
            {
                Id = Guid.NewGuid().ToString(), Username = username, Email = email,
                PasswordHash = _passwordHasher.Hash(password), Role = role, Status = "ACTIVE"
            });
            _logger.LogInformation("Approved {Role} seed StaffAccount created for {Username}.", role, username);
        }
    }
}
