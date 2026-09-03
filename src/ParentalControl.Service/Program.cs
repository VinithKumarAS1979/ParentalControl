using ParentalControl.Common;
using ParentalControl.Common.Dns;
using ParentalControl.Service;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "ParentalControlService");
builder.Services.AddSingleton<BrowserHistoryReader>();
builder.Services.AddSingleton<VisitLogWriter>();
builder.Services.AddSingleton<NetworkDnsConfigurator>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
