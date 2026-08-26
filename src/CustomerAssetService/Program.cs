using System.Text.Json;

using CustomerAssetService.Repositories;
using CustomerAssetService.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("default")
    ?? throw new InvalidOperationException("Connection string 'default' was not found.");

// Add services to the container.

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

builder.Services.AddSingleton<IDbConnectionFactory>(new MySqlConnectionFactory(connectionString));
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<CustomerService>();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Property names are already camelCased; this does the same for
        // dictionary keys, which is what ValidationProblemDetails.Errors is.
        // Without it DataAnnotations returns "Name" while a hand-built
        // problem returns "phone", and the frontend has two rules to follow.
        options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    });
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors(FrontendCorsPolicy);

app.UseAuthorization();

app.MapControllers();

app.Run();

partial class Program
{
    private const string FrontendCorsPolicy = "Frontend";
}
