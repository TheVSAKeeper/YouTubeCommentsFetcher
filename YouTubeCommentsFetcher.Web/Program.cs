using Google.Apis.Services;
using YouTubeCommentsFetcher.Web.Services;
using YouTubeService = Google.Apis.YouTube.v3.YouTubeService;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.AddScoped<YouTubeService>(_ => new YouTubeService(new BaseClientService.Initializer
{
    ApiKey = builder.Configuration["YouTubeApiKey"],
    ApplicationName = "YouTubeCommentsFetcher"
}));

builder.Services.AddScoped<IYouTubeService, YouTubeCommentsFetcher.Web.Services.YouTubeService>();

WebApplication app = builder.Build();

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
