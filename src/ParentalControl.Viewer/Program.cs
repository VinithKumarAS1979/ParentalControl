using ParentalControl.Viewer;
using ParentalControl.Common;
using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder()
	.SetBasePath(AppContext.BaseDirectory)
	.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
	.AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true, reloadOnChange: false)
	.Build();

var options = config.GetSection("ParentalControl").Get<ParentalControlOptions>() ?? new ParentalControlOptions();
PathsConfig.Initialize(options);
ApplicationConfiguration.Initialize();
Application.Run(new MainForm());

