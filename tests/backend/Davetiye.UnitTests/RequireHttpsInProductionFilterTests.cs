using Davetiye.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Davetiye.UnitTests;

public sealed class RequireHttpsInProductionFilterTests
{
    [Fact]
    public async Task Production_rejects_raw_http_but_development_allows_it()
    {
        var productionResult = await InvokeAsync("Production", "http");
        var developmentResult = await InvokeAsync("Development", "http");
        var productionHttpsResult = await InvokeAsync("Production", "https");

        Assert.Equal(StatusCodes.Status400BadRequest, productionResult.StatusCode);
        Assert.True(productionResult.Rejected);
        Assert.Equal(StatusCodes.Status200OK, developmentResult.StatusCode);
        Assert.False(developmentResult.Rejected);
        Assert.Equal(StatusCodes.Status200OK, productionHttpsResult.StatusCode);
        Assert.False(productionHttpsResult.Rejected);
    }

    private static async Task<(int StatusCode, bool Rejected)> InvokeAsync(string environmentName, string scheme)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = scheme;
        var handlerWasCalled = false;
        var filter = new RequireHttpsInProductionFilter(new TestHostEnvironment(environmentName));
        var result = await filter.InvokeAsync(
            EndpointFilterInvocationContext.Create(httpContext),
            _ =>
            {
                handlerWasCalled = true;
                return ValueTask.FromResult<object?>(Results.Ok());
            });

        var statusCode = (result as IStatusCodeHttpResult)?.StatusCode ?? StatusCodes.Status200OK;
        return (statusCode, !handlerWasCalled);
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Davetiye.UnitTests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
