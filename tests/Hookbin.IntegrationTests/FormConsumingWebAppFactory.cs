using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Hookbin.IntegrationTests;

/// <summary>
/// Integration test fixture that reproduces the antiforgery filter side-effect:
/// for form-encoded POSTs, the body stream is consumed before the MVC action runs.
/// Uses an IStartupFilter middleware to call ReadFormAsync early, exactly as
/// AutoValidateAntiforgeryTokenAuthorizationFilter does in production.
/// NoOpAntiforgery is still in place (inherited from WebAppFactory) so token/request
/// API write calls don't need XSRF tokens.
/// </summary>
public sealed class FormConsumingWebAppFactory : WebAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
            services.AddSingleton<IStartupFilter, FormBodyConsumingStartupFilter>());
    }
}

file sealed class FormBodyConsumingStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.HasFormContentType)
                    await context.Request.ReadFormAsync(context.RequestAborted);

                await nextMiddleware(context);
            });

            next(app);
        };
}
