using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SubscriberAdmin.Models;
using SubscriberAdmin.Utils;
using WebApplication1.Models;

public static class LINENotifyHandler
{
    public static IResult Subscribe(
        [FromServices] IConfiguration config,
        [FromServices] IHttpContextAccessor accessor
    )
    {
        var redirectUri = GetRedirectUri(config, accessor);

        var qb = new QueryBuilder();
        qb.Add("response_type", "code");
        qb.Add("client_id", config[$"{nameof(LINENotifyHandler)}:client_id"]);
        qb.Add("scope", config[$"{nameof(LINENotifyHandler)}:scope"]);
        qb.Add("redirect_uri", redirectUri);

        var state = KeyGenerator.GetUniqueKey(16);
        accessor.HttpContext.Session.SetString("state", state);
        qb.Add("state", state);

        var authUrl = config[$"{nameof(LINENotifyHandler)}:authURL"] + qb.ToQueryString().Value;
        return Results.Redirect(authUrl);
    }

    private static string GetRedirectUri(IConfiguration config, IHttpContextAccessor accessor)
    {
        var currentUrl = accessor.HttpContext.Request.GetEncodedUrl();
        var authority = new Uri(currentUrl).GetLeftPart(UriPartial.Authority);

        var redirectUri = config[$"{nameof(LINENotifyHandler)}:redirect_uri"];
        if (Uri.IsWellFormedUriString(redirectUri, UriKind.Relative))
        {
            redirectUri = authority + redirectUri;
        }

        return redirectUri;
    }

    public static async Task<IResult> SubscribeCallback(
        [FromServices] IConfiguration config,
        [FromServices] IHttpContextAccessor accessor,
        [FromServices] SubscriberContext db,
        [FromServices] IHttpClientFactory httpClientFactory,
        string code,
        string state
    )
    {
        if (state != accessor.HttpContext.Session.GetString("state"))
        {
            return Results.BadRequest();
        }

        var redirectUri = GetRedirectUri(config, accessor);
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("client_id", config[$"{nameof(LINENotifyHandler)}:client_id"]),
            new KeyValuePair<string, string>("client_secret", config[$"{nameof(LINENotifyHandler)}:client_secret"]),
            new KeyValuePair<string, string>("redirect_uri", redirectUri),
        });

        var http = httpClientFactory.CreateClient();
        var response = await http.PostAsync(config[$"{nameof(LINENotifyHandler)}:tokenURL"], content);
        var jsonString = await response.Content.ReadAsStringAsync();
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var result = JsonSerializer.Deserialize<LINENotifyTokenResponse>(jsonString);
            var userId = accessor.HttpContext.User.Identity.Name;
            var profile = await db.Subscribers.FirstOrDefaultAsync(x => x.Id.ToString() == userId);
            if (profile is null)
            {
                return Results.Redirect("/signout");
            }
            else
            {
                profile.LINENotifyAccessToken = result.AccessToken;
                await db.SaveChangesAsync();
                return Results.Ok(result);
            }
        }
        else
        {
            var result = JsonSerializer.Deserialize<LINELoginTokenError>(jsonString);
            return Results.BadRequest(result);
        }
    }

    public static async Task<IResult> Unsubscribe(
        [FromServices] IConfiguration config,
        [FromServices] IHttpContextAccessor accessor,
        [FromServices] SubscriberContext db,
        [FromServices] IHttpClientFactory httpClientFactory
    )
    {
        var revokeURL = config[$"{nameof(LINENotifyHandler)}:revokeURL"];
        var profile = await db.Subscribers.FirstOrDefaultAsync(x => x.Id.ToString() == accessor.HttpContext.User.Identity.Name);
        if (profile is null)
        {
            return Results.Redirect("/signout");
        }
        else
        {
            var http = httpClientFactory.CreateClient();
            if (string.IsNullOrEmpty(profile.LINENotifyAccessToken))
            {
                return Results.Redirect("/my");
            }
            else
            {
                http.DefaultRequestHeaders.Add("Authorization", $"Bearer {profile.LINELoginAccessToken}");
                var response = await http.PostAsync(revokeURL, null);
                var jsonString = await response.Content.ReadAsStringAsync();

                profile.LINENotifyAccessToken = "";
                await db.SaveChangesAsync();

                var result = JsonSerializer.Deserialize<LINENotifyResult>(jsonString);
                return Results.Ok(result);
            }
        }
    }

    public static async Task<IResult> NotifyAll(
        [FromServices] IConfiguration config,
        [FromServices] IHttpContextAccessor accessor,
        [FromServices] SubscriberContext db,
        [FromServices] IHttpClientFactory httpClientFactory
    )
    {
        var all = await db.Subscribers
            .Where(x => !string.IsNullOrEmpty(x.LINENotifyAccessToken))
            .Select(x => new { x.LINENotifyAccessToken, x.Username })
            .ToListAsync();

        if (all.Any())
        {
            var results = new List<string>();
            foreach (var item in all)
            {
                var http = httpClientFactory.CreateClient();
                http.DefaultRequestHeaders.Add("Authorization", $"Bearer {item.LINENotifyAccessToken}");

                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("message", "Hello LINENotify!"),
                });

                var response = await http.PostAsync(config[$"{nameof(LINENotifyHandler)}:notifyURL"], content);
                var jsonString = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<LINENotifyResult>(jsonString);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    results.Add($"Sending to {item.Username}: {result.Message}");
                }
                else
                {
                    results.Add($"Sending to {item.Username} failed: {result.Message} ({result.Status})");
                }
            }

            results.Add($"We already notified {all.Count} subscribers!");

            return Results.Ok(results);
        }
        else
        {
            return Results.Ok("No subscribers!");
        }
    }
}