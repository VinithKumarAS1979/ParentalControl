using ParentalControl.Common;
using ParentalControl.Common.WebBlocking;
using ParentalControl.Service;
using Microsoft.Extensions.Configuration;

var builder = Host.CreateApplicationBuilder(args);
var parentalControlOptions = builder.Configuration.GetSection("ParentalControl").Get<ParentalControlOptions>() ?? new ParentalControlOptions();
PathsConfig.Initialize(parentalControlOptions);
builder.Services.AddWindowsService(options => options.ServiceName = "ParentalControlService");
builder.Services.AddSingleton<BrowserHistoryReader>();
builder.Services.AddSingleton<BlocklistManager>();
builder.Services.AddSingleton<AppBlockManager>();
builder.Services.AddSingleton<VisitLogWriter>();
builder.Services.AddSingleton<BlockPageCertificateStore>();
builder.Services.AddSingleton<BlockPageServer>();
builder.Services.AddSingleton<BlocklistApiServer>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
