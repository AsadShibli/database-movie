using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ReactionVideoAggregator.Security;

/// <summary>
/// Requires a matching x-api-key header for mutating admin endpoints.
/// </summary>
public class AdminApiKeyAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var expected = config["AdminSettings:ApiKey"];
        var provided = context.HttpContext.Request.Headers["x-api-key"].ToString();

        if (string.IsNullOrEmpty(expected)
            || !string.Equals(provided, expected, StringComparison.Ordinal))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        await next();
    }
}
