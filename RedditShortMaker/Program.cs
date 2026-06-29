using RedditShortMaker.Components;
using RedditShortMaker.Models;
using RedditShortMaker.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

// In self-contained published builds, the binary and wwwroot/ live in the same
// directory. Override the content root so static files are resolved correctly
// regardless of CWD. In dev mode (dotnet run), this check fails gracefully and
// the default project-relative content root is used.
var appDir = Path.GetDirectoryName(Environment.ProcessPath!)!;
var publishDir = Path.Combine(appDir, "wwwroot");

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = Directory.Exists(publishDir) ? appDir : null,
    WebRootPath = Directory.Exists(publishDir) ? publishDir : null,
});

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient();
builder.Services.AddSingleton<RedditService>();
builder.Services.AddSingleton<RedditCardService>();
builder.Services.AddSingleton<EdgeTtsService>();
builder.Services.AddSingleton<SubtitleGeneratorService>();
builder.Services.AddSingleton<FfmpegService>();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(exceptionHandlerApp =>
    {
        exceptionHandlerApp.Run(async context =>
        {
            var feature = context.Features.Get<IExceptionHandlerPathFeature>();
            var exception = feature?.Error;

            int status = exception switch
            {
                RedditPostNotFoundException => 404,
                RedditFetchException => 502,
                _ => 500
            };
            string title = exception switch
            {
                RedditPostNotFoundException => exception.Message,
                RedditFetchException => exception.Message,
                _ => "An error occurred."
            };

            context.Response.StatusCode = status;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = status,
                Title = title,
                Type = $"https://httpstatuses.com/{status}",
            });
        });
    });
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

var outputsDir = Path.Combine(app.Environment.ContentRootPath, "outputs");
Directory.CreateDirectory(outputsDir);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(outputsDir),
    RequestPath = "/outputs",
});

// Serve wwwroot/ from disk (content root is set to binary's directory at startup)
app.UseStaticFiles();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
