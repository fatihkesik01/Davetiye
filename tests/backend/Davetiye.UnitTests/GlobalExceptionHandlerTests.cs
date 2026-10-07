using System.Text.Json;
using Davetiye.Api.Infrastructure.ErrorHandling;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class GlobalExceptionHandlerTests
{
    [Theory]
    [InlineData(StatusCodes.Status400BadRequest)]
    [InlineData(StatusCodes.Status413PayloadTooLarge)]
    public async Task Bad_http_requests_keep_the_safe_client_status_and_never_expose_exception_details(
        int expectedStatusCode)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        await using var serviceProvider = services.BuildServiceProvider();

        var context = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/webhooks/iyzico";
        context.Response.Body = new MemoryStream();

        var handler = new GlobalExceptionHandler(
            new TestHostEnvironment("Production"), NullLogger<GlobalExceptionHandler>.Instance);
        var handled = await handler.TryHandleAsync(context,
            new BadHttpRequestException("private parser detail", expectedStatusCode), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(expectedStatusCode, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(expectedStatusCode, document.RootElement.GetProperty("status").GetInt32());
        Assert.DoesNotContain("private parser detail", document.RootElement.GetProperty("detail").GetString(),
            StringComparison.Ordinal);
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Davetiye.UnitTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
