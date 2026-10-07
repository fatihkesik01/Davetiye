using System.Net;
using System.Text;
using Davetiye.Api.Endpoints.Payments;
using Davetiye.Api.Infrastructure.Hosting;
using Davetiye.Application.Modules.Payments.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;
using Xunit;

namespace Davetiye.IntegrationTests;

public sealed class IyzicoWebhookEndpointTests
{
    [Fact]
    public async Task MissingAndMultipleSignatureHeadersAreRejectedBeforeIngestion()
    {
        await using var server = await CreateServerAsync(new FakeIngestor(), maximumBodyBytes: 1024);
        using var client = server.CreateClient();

        var missing = await client.PostAsync("/webhooks/iyzico", JsonBody());
        var multipleRequest = new HttpRequestMessage(HttpMethod.Post, "/webhooks/iyzico") { Content = JsonBody() };
        multipleRequest.Headers.TryAddWithoutValidation("X-IYZ-SIGNATURE-V3", [new string('a', 64), new string('b', 64)]);
        var multiple = await client.SendAsync(multipleRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, multiple.StatusCode);
        Assert.Equal(0, server.Ingestor.CallCount);
    }

    [Fact]
    public async Task OversizedChunkedBodyIsRejectedWhileStreaming()
    {
        await using var server = await CreateServerAsync(new FakeIngestor(), maximumBodyBytes: 16);
        using var client = server.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/iyzico")
        {
            Content = new StreamContent(new NonSeekableStream(new byte[128]))
        };
        request.Headers.TryAddWithoutValidation("X-IYZ-SIGNATURE-V3", new string('a', 64));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, server.Ingestor.CallCount);
    }

    [Theory]
    [InlineData(PaymentWebhookIngestionOutcome.Accepted, HttpStatusCode.OK)]
    [InlineData(PaymentWebhookIngestionOutcome.Duplicate, HttpStatusCode.OK)]
    [InlineData(PaymentWebhookIngestionOutcome.InvalidSignature, HttpStatusCode.Unauthorized)]
    [InlineData(PaymentWebhookIngestionOutcome.InvalidPayload, HttpStatusCode.BadRequest)]
    [InlineData(PaymentWebhookIngestionOutcome.Unavailable, HttpStatusCode.ServiceUnavailable)]
    public async Task IngestionOutcomesMapToRetrySafeHttpStatuses(
        PaymentWebhookIngestionOutcome outcome, HttpStatusCode expectedStatus)
    {
        await using var server = await CreateServerAsync(new FakeIngestor { Outcome = outcome }, maximumBodyBytes: 1024);
        using var client = server.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/iyzico") { Content = JsonBody() };
        request.Headers.TryAddWithoutValidation("X-IYZ-SIGNATURE-V3", new string('a', 64));

        var response = await client.SendAsync(request);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(1, server.Ingestor.CallCount);
    }

    [Fact]
    public async Task StorageExceptionReturnsRetryable503InsteadOfAcknowledgement()
    {
        await using var server = await CreateServerAsync(new FakeIngestor { ThrowOnIngest = true }, maximumBodyBytes: 1024);
        using var client = server.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/iyzico") { Content = JsonBody() };
        request.Headers.TryAddWithoutValidation("X-IYZ-SIGNATURE-V3", new string('a', 64));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static StringContent JsonBody() => new("{}", Encoding.UTF8, "application/json");

    private static async Task<TestEndpointServer> CreateServerAsync(FakeIngestor ingestor, long maximumBodyBytes)
    {
        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost => webHost
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddLogging();
                    services.AddRateLimiter(options => options.AddFixedWindowLimiter("webhook-test", limiter =>
                    {
                        limiter.PermitLimit = 20;
                        limiter.Window = TimeSpan.FromMinutes(1);
                        limiter.QueueLimit = 0;
                    }));
                    services.AddSingleton<IOptions<RequestLimitsOptions>>(
                        Options.Create(new RequestLimitsOptions { MaxRequestBodyBytes = maximumBodyBytes }));
                    services.AddSingleton<IPaymentWebhookIngestor>(ingestor);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseRateLimiter();
                    app.UseEndpoints(endpoints => endpoints.MapIyzicoWebhookEndpoints("webhook-test"));
                }));

        var host = hostBuilder.Build();
        await host.StartAsync();
        return new TestEndpointServer(host, ingestor);
    }

    private sealed class TestEndpointServer(IHost host, FakeIngestor ingestor) : IAsyncDisposable
    {
        public FakeIngestor Ingestor => ingestor;
        public HttpClient CreateClient() => host.GetTestClient();
        public async ValueTask DisposeAsync() => await host.StopAsync();
    }

    private sealed class FakeIngestor : IPaymentWebhookIngestor
    {
        private int _callCount;
        public PaymentWebhookIngestionOutcome Outcome { get; init; } = PaymentWebhookIngestionOutcome.Accepted;
        public bool ThrowOnIngest { get; init; }
        public int CallCount => _callCount;

        public Task<PaymentWebhookIngestionOutcome> IngestAsync(
            ReadOnlyMemory<byte> body, string? signature, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            if (ThrowOnIngest) throw new InvalidOperationException("simulated storage failure");
            return Task.FromResult(Outcome);
        }
    }

    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private int _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var take = Math.Min(count, data.Length - _position);
            Array.Copy(data, _position, buffer, offset, take);
            _position += take;
            return take;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var take = Math.Min(buffer.Length, data.Length - _position);
            data.AsMemory(_position, take).CopyTo(buffer);
            _position += take;
            return ValueTask.FromResult(take);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
