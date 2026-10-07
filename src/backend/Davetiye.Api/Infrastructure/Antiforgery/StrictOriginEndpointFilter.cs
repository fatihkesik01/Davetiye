namespace Davetiye.Api.Infrastructure.Antiforgery;

/// <summary>Rejects missing, opaque, or non-allowlisted browser origins on anonymous writes.</summary>
public sealed class StrictOriginEndpointFilter(IConfiguration configuration) : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var values = context.HttpContext.Request.Headers.Origin;
        if (values.Count != 1 || !TryOrigin(values[0], out var requestOrigin))
            return ValueTask.FromResult<object?>(Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "A trusted Origin header is required."));

        var allowed = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (!allowed.Any(item => TryOrigin(item, out var allowedOrigin) &&
                                 string.Equals(requestOrigin, allowedOrigin, StringComparison.OrdinalIgnoreCase)))
            return ValueTask.FromResult<object?>(Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Origin is not allowed."));
        return next(context);
    }

    private static bool TryOrigin(string? value, out string origin)
    {
        origin = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || value == "null" ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return false;
        origin = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }
}
