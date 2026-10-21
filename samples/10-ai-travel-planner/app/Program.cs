using ai_travel_planner.Modules.Attractions.Api;
using ai_travel_planner.Modules.Destinations.Api;
using ai_travel_planner.Modules.Tourism.Api;
using ai_travel_planner.Modules.TravelPlans.Api;
using ai_travel_planner.Modules.Weather.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDestinationsModule();
builder.Services.AddWeatherModule();
builder.Services.AddAttractionsModule();
builder.Services.AddTourismModule();
builder.Services.AddTravelPlansModule();

var app = builder.Build();
app.UseStaticFiles();
app.MapGet("/", async (IWebHostEnvironment environment) =>
{
    var webRoot = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
    var indexPath = Path.Combine(webRoot, "customer-ui", "index.html");
    return Results.Content(await File.ReadAllTextAsync(indexPath), "text/html");
});
app.MapDestinationsModule();
app.MapWeatherModule();
app.MapAttractionsModule();
app.MapTourismModule();
app.MapTravelPlansModule();
app.Run();

public partial class Program;
