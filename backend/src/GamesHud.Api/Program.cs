using GamesHud.Api.Configuration;
using GamesHud.Api.Docker.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DockerOptions>(builder.Configuration.GetSection(DockerOptions.SectionName));
builder.Services.AddScoped<IContainerService, DockerContainerService>();
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
