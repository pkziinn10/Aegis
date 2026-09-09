using Aegis.Application.Abstractions;
using Aegis.Domain.Entities;
using Aegis.Domain.Enums;
using Aegis.Domain.Repositories;
using Aegis.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aegis.Infrastructure;

public static class DevelopmentUserSeeder
{
    public static async Task SeedAsync(
        IServiceProvider services,
        IConfiguration configuration,
        IHostEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment() || !configuration.GetValue<bool>("DevelopmentSeed:Enabled"))
            return;

        var settings = ReadSettings(configuration);
        using var scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        await users.AddIfNotExistsAtomicallyAsync(
            new User(Guid.NewGuid(), settings.UserEmail, hasher.Hash(settings.UserPassword), UserRole.User),
            cancellationToken);
        await users.AddIfNotExistsAtomicallyAsync(
            new User(Guid.NewGuid(), settings.AdminEmail, hasher.Hash(settings.AdminPassword), UserRole.Admin),
            cancellationToken);
    }

    private static SeedSettings ReadSettings(IConfiguration configuration)
    {
        var userEmail = configuration["DevelopmentSeed:UserEmail"];
        var userPassword = configuration["DevelopmentSeed:UserPassword"];
        var adminEmail = configuration["DevelopmentSeed:AdminEmail"];
        var adminPassword = configuration["DevelopmentSeed:AdminPassword"];

        var errors = new List<string>();
        var parsedUserEmail = ParseEmail(userEmail, "UserEmail", errors);
        var parsedAdminEmail = ParseEmail(adminEmail, "AdminEmail", errors);
        ValidatePassword(userPassword, "UserPassword", errors);
        ValidatePassword(adminPassword, "AdminPassword", errors);
        if (parsedUserEmail is not null && parsedAdminEmail is not null && parsedUserEmail == parsedAdminEmail)
            errors.Add("DevelopmentSeed:UserEmail and DevelopmentSeed:AdminEmail must be different.");

        if (errors.Count > 0)
            throw new InvalidOperationException($"Development seed configuration is invalid: {string.Join(" ", errors)}");

        return new(parsedUserEmail!, userPassword!, parsedAdminEmail!, adminPassword!);
    }

    private static Email? ParseEmail(string? value, string name, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"DevelopmentSeed:{name} is required.");
            return null;
        }

        try { return new Email(value); }
        catch (ArgumentException)
        {
            errors.Add($"DevelopmentSeed:{name} must be a valid email.");
            return null;
        }
    }

    private static void ValidatePassword(string? value, string name, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 12)
            errors.Add($"DevelopmentSeed:{name} is required and must contain at least 12 characters.");
    }

    private sealed record SeedSettings(Email UserEmail, string UserPassword, Email AdminEmail, string AdminPassword);
}
