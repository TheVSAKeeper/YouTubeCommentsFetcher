using Google.Apis.Services;
using Google.Apis.YouTube.v3;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.AddSingleton<YouTubeService>(_ => new YouTubeService(new BaseClientService.Initializer
{
    ApiKey = builder.Configuration["YouTubeApiKey"],
    ApplicationName = "YouTubeCommentsFetcher"
}));

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
