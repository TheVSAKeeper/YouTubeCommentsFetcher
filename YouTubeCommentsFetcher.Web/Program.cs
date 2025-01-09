using Google.Apis.Services;
using Serilog;
using Serilog.Events;
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

    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
    builder.Services.AddSerilog();

    builder.Services.AddControllersWithViews();

    builder.Services.AddScoped<YouTubeService>(_ => new YouTubeService(new BaseClientService.Initializer
    {
        ApiKey = builder.Configuration["YouTubeApiKey"],
        ApplicationName = "YouTubeCommentsFetcher",
    }));

    builder.Services.AddScoped<IYouTubeService, YouTubeCommentsFetcher.Web.Services.YouTubeService>();

    WebApplication app = builder.Build();

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }
    else
    {
        app.UseExceptionHandler("/Home/Error");
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseStaticFiles();

    app.UseRouting();

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
