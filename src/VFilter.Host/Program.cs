using VFilter.Core;
using VFilter.Core.Commands;
using VFilter.Core.Config;
using VFilter.Core.Logging;
using VFilter.Core.Modules;
using VFilter.Core.Sessions;
using VFilter.Core.Status;
using VFilter.Data;

var builder = Host.CreateApplicationBuilder(args);

// Windows Service on Windows, console elsewhere. The connection string comes from appsettings, user
// secrets (development) or the environment variable VFilter__ConnectionString; it is never logged.
builder.Services.AddWindowsService(options => options.ServiceName = "VFilter");
builder.Services.AddVFilterDatabase(builder.Configuration);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ConfigStore>();
builder.Services.AddSingleton<IModuleRunner, StandbyModuleRunner>();
builder.Services.AddSingleton<ModuleController>();
builder.Services.AddSingleton<GatewayModeState>();
builder.Services.AddSingleton<DatabaseClockOffset>();
builder.Services.AddSingleton<SessionRegistry>();
builder.Services.AddSingleton<FilterLog>();
builder.Services.AddSingleton<HeartbeatService>();

// Lifecycle first (loads config, starts modules), then the periodic services.
builder.Services.AddHostedService<FilterLifecycle>();
builder.Services.AddHostedService<ConfigPoller>();
builder.Services.AddHostedService<CommandProcessor>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<HeartbeatService>());
builder.Services.AddHostedService<SessionMirror>();
builder.Services.AddHostedService<LogWriter>();

await builder.Build().RunAsync().ConfigureAwait(false);
