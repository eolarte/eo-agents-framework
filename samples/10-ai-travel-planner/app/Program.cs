using ai_travel_planner.Modules.Destinations.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDestinationsModule();

var app = builder.Build();
app.UseStaticFiles();
app.MapGet("/", async (IWebHostEnvironment environment) =>
{
    var webRoot = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
    var indexPath = Path.Combine(webRoot, "customer-ui", "index.html");
    return Results.Content(await File.ReadAllTextAsync(indexPath), "text/html");
});
app.MapDestinationsModule();
app.Run();

public partial class Program;
