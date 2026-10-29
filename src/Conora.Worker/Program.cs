using Conora.Infrastructure;
using Conora.Repository;
using Conora.Services;
using Conora.Worker.Jobs;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddRepositories();
builder.Services.AddServices();
builder.Services.AddHostedService<LgpdRetentionWorker>();
builder.Services.AddHostedService<LgpdExportNightlyWorker>();
builder.Services.AddHostedService<LgpdExportPurgeWorker>();
builder.Services.AddHostedService<PreRegistrationPurgeWorker>();
builder.Services.AddHostedService<ReceiptOcrWorker>();
builder.Services.AddHostedService<ReceiptImagePurgeWorker>();
builder.Services.AddHostedService<MilestoneNightlyWorker>();

var host = builder.Build();
await host.RunAsync();
