using Aegis.Application.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace Aegis.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAegisApplication(this IServiceCollection services)
    {
        services.AddScoped<RegisterUseCase>();
        services.AddScoped<LoginUseCase>();
        services.AddScoped<RefreshUseCase>();
        services.AddScoped<LogoutUseCase>();
        services.AddScoped<GetMeUseCase>();
        services.AddScoped<ChangePasswordUseCase>();
        services.AddScoped<DeactivateUserUseCase>();
        return services;
    }
}
