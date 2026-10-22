namespace Jorvis.Api.Auth;

public static class CsrfProtection
{
    /// <summary>
    /// Rejects state-changing requests that a browser reports as coming from another site. This backs up the
    /// SameSite cookie: browsers always send Sec-Fetch-Site / Origin, other clients carry no ambient cookies.
    /// </summary>
    public static IApplicationBuilder UseCsrfProtection(this IApplicationBuilder app, string[] allowedOrigins) =>
        app.Use(async (context, next) =>
        {
            var request = context.Request;
            if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method))
            {
                await next(context);
                return;
            }

            var site = request.Headers["Sec-Fetch-Site"].ToString();
            var origin = request.Headers.Origin.ToString();

            var allowed = site is "same-origin" or "none"
                || (origin.Length > 0 && IsTrustedOrigin(origin, request.Host.ToString(), allowedOrigins))
                || (origin.Length == 0 && site.Length == 0);

            if (!allowed)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next(context);
        });

    private static bool IsTrustedOrigin(string origin, string host, string[] allowedOrigins)
    {
        if (allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
            return true;

        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && string.Equals(uri.Authority, host, StringComparison.OrdinalIgnoreCase);
    }
}
