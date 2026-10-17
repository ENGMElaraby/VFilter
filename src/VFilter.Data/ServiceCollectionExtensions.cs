using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VFilter.Core.Commands;
using VFilter.Core.Config;
using VFilter.Core.Logging;
using VFilter.Core.Sessions;
using VFilter.Core.Status;

namespace VFilter.Data;

public static class ServiceCollectionExtensions
{
    /// <summary>SQL Server implementations of the VFILTER repositories (section "VFilter").</summary>
    public static IServiceCollection AddVFilterDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.Section));
        services.AddSingleton<SqlConnectionFactory>();
        services.AddSingleton<IConfigRepository, ConfigRepository>();
        services.AddSingleton<ICommandRepository, CommandRepository>();
        services.AddSingleton<IStatusRepository, StatusRepository>();
        services.AddSingleton<ISessionRepository, SessionRepository>();
        services.AddSingleton<ILogRepository, LogRepository>();

        return services;
    }
}
