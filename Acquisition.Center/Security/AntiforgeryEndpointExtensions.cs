using Microsoft.AspNetCore.Antiforgery;

namespace Acquisition.Center.Security;

public static class AntiforgeryEndpointExtensions
{
    public static RouteHandlerBuilder RequireCsrf(this RouteHandlerBuilder builder) => builder.AddEndpointFilter(async (context, next) =>
    {
        var http = context.HttpContext;
        try
        {
            await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.Json(new { error = new { code = "INVALID_CSRF", message = "请求安全令牌无效。" } }, statusCode: 400);
        }
        return await next(context);
    });
}
