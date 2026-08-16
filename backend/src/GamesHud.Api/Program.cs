using GamesHud.Api.Configuration;
using GamesHud.Api.Docker.Services;
using GamesHud.Api.Palworld.Configuration;
using GamesHud.Api.Palworld.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DockerOptions>(builder.Configuration.GetSection(DockerOptions.SectionName));
builder.Services.Configure<PalworldOptions>(builder.Configuration.GetSection(PalworldOptions.SectionName));
builder.Services.AddScoped<IContainerService, DockerContainerService>();
builder.Services.AddSingleton<IPalworldConfigFileSystem, PalworldConfigFileSystem>();
builder.Services.AddScoped<IPalworldConfigService, PalworldConfigService>();
builder.Services.AddScoped<IPalworldOverviewService, PalworldOverviewService>();
builder.Services
    .AddHttpClient<IPalworldRestService, PalworldRestService>()
    .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddControllers();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("LocalVite", policy =>
        {
            policy
                .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
    });
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseCors("LocalVite");
}

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapControllers();

app.Run();

public partial class Program;
