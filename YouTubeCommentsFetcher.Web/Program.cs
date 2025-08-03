using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.FileProviders;
using Quartz;
using Quartz.AspNetCore;
using Serilog;
using Serilog.Events;
using System.IO.Compression;
using YouTubeCommentsFetcher.Web.Authentication;
using YouTubeCommentsFetcher.Web.Configuration;
using YouTubeCommentsFetcher.Web.Services;
using YouTubeService = Google.Apis.YouTube.v3.YouTubeService;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .MinimumLevel.Override("Microsoft.AspNetCore.Hosting", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.Mvc", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.Routing", LogEventLevel.Warning)
    .CreateLogger();

try
{
    Log.Information("Starting web application");

    var builder = WebApplication.CreateBuilder(args);
    builder.Services.AddSerilog();

    builder.Services.AddControllersWithViews()
        .AddJsonOptions(options =>
        {
            var defaultOptions = JsonConfiguration.Default;
            options.JsonSerializerOptions.WriteIndented = defaultOptions.WriteIndented;
            options.JsonSerializerOptions.PropertyNamingPolicy = defaultOptions.PropertyNamingPolicy;
            options.JsonSerializerOptions.PropertyNameCaseInsensitive = defaultOptions.PropertyNameCaseInsensitive;
            options.JsonSerializerOptions.DefaultIgnoreCondition = defaultOptions.DefaultIgnoreCondition;
        });

    builder.Services.AddAuthentication("ApiKey")
        .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>("ApiKey", null);

    builder.Services.AddAuthorization();

    builder.Services.AddScoped<YouTubeService>(_ => new(new()
    {
        ApiKey = builder.Configuration["YouTubeApiKey"],
        ApplicationName = "YouTubeCommentsFetcher",
    }));

    builder.Services.AddScoped<IYouTubeService, YouTubeCommentsFetcher.Web.Services.YouTubeService>();
    builder.Services.AddSingleton<IJobStatusService, InMemoryJobStatusService>();

    builder.Services.Configure<DataPathOptions>(builder.Configuration.GetSection(DataPathOptions.SectionName));
    builder.Services.AddSingleton<IDataPathService, DataPathService>();
    builder.Services.AddSingleton<IFetchResultsService, FetchResultsService>();
    builder.Services.AddSingleton<IApiAuthService, JsonApiAuthService>();

    builder.Services.AddQuartz(q =>
    {
        //q.AddJob<FetchCommentsJob>(opts => opts.WithIdentity("FetchCommentsJob")).StoreDurably() ;
    });

    builder.Services.AddQuartzServer(options =>
    {
        options.WaitForJobsToComplete = true;
    });

    builder.Services.AddResponseCompression(options =>
    {
        options.EnableForHttps = true;
        options.Providers.Add<BrotliCompressionProvider>();
        options.Providers.Add<GzipCompressionProvider>();
    });

    builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
    {
        options.Level = CompressionLevel.SmallestSize;
    });

    builder.Services.Configure<GzipCompressionProviderOptions>(options =>
    {
        options.Level = CompressionLevel.SmallestSize;
    });

    var app = builder.Build();

    app.UseSerilogRequestLogging();
    app.UseResponseCompression();
    app.MapStaticAssets();

    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }
    else
    {
        app.UseExceptionHandler("/Home/Error");
    }

    app.UseStaticFiles();

    var dataPathService = app.Services.GetRequiredService<IDataPathService>();
    dataPathService.EnsureDataDirectoryExists();

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(dataPathService.GetAbsoluteDataDirectory()),
        RequestPath = $"/{dataPathService.RelativeDataDirectory}",
    });

    app.UseRouting();

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
