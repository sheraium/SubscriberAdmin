using JWT;
using JWT.Serializers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SubscriberAdmin.Models;
using SubscriberAdmin.Utils;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using WebApplication1.Models;

namespace SubscriberAdmin;

public static class LINELoginHandler
{
    public static async Task<IResult> Profile(
        [FromServices] IConfiguration config,
        [FromServices] IHttpContextAccessor accessor,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] SubscriberContext db
    )
    {
        var profile = await db.Subscribers.FirstOrDefaultAsync(x =>
            x.Id.ToString() == accessor.HttpContext.User.Identity.Name);
        if (profile is null)
        {
            return Results.Redirect("/signout");
        }
        else
        {
            // 呼叫 Profile API 取得個人資料
            var http = httpClientFactory.CreateClient();
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {profile.LINELoginAccessToken}");
            var result = await http.GetFromJsonAsync<LINELoginProfile>(config[$"{nameof(LINELoginHandler)}:profileURL"]);
            return Results.Ok(result);
        }
    }

    public static IResult SignIn(
        [FromServices] IConfiguration config,
        [FromServices] IHttpContextAccessor httpContextAccessor)
    {
        var redirectUri = GetRedirectUri(config, httpContextAccessor);

        var qb = new QueryBuilder();
        qb.Add("response_type", "code");
        qb.Add("client_id", config[$"{nameof(LINELoginHandler)}:client_id"]);
        qb.Add("scope", config[$"{nameof(LINELoginHandler)}:scope"]);
        qb.Add("redirect_uri", redirectUri);

        var state = KeyGenerator.GetUniqueKey(16);
        httpContextAccessor.HttpContext.Session.SetString("state", state);
        qb.Add("state", state);

        var authUrl = config[$"{nameof(LINELoginHandler)}:authURL"] + qb.ToQueryString().Value;

        return Results.Redirect(authUrl);
    }

    public static async Task<IResult> SignInCallback(
        [FromServices] IConfiguration config,
        [FromServices] IHttpContextAccessor accessor,
        [FromServices] SubscriberContext dbContext,
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
        var httpClient = httpClientFactory.CreateClient();
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("client_id", config[$"{nameof(LINELoginHandler)}:client_id"]),
            new KeyValuePair<string, string>("client_secret", config[$"{nameof(LINELoginHandler)}:client_secret"]),
            new KeyValuePair<string, string>("redirect_uri", redirectUri),
        });

        var response = await httpClient.PostAsync(config[$"{nameof(LINELoginHandler)}:tokenURL"], content);
        var jsonString = await response.Content.ReadAsStringAsync();

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var result = JsonSerializer.Deserialize<LINELoginTokenResponse>(jsonString);

            // 解析 ID Token 直接取得 JWT 中的 Payload 資訊
            var serializer = new JsonNetSerializer();
            var urlEncoder = new JwtBase64UrlEncoder();
            var decoder = new JwtDecoder(serializer, urlEncoder);

            // 將 ID Token 解開，取得重要的 ID 資訊！
            var payload = decoder.DecodeToObject<JwtPayload>(result.IdToken);

            // 呼叫 Profile API 取得個人資料，我們主要需拿到 UserId 資訊
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {result.AccessToken}");
            var profileReuslt = await httpClient.GetFromJsonAsync<LINELoginProfile>(config[$"{nameof(LINELoginHandler)}:profileURL"]);
            if (string.IsNullOrEmpty(profileReuslt.UserId))
            {
                return Results.BadRequest(profileReuslt);
            }

            // LINE 帳號的 UserId 是不會變的資料，可以用來當成登入驗證的參考資訊
            var profile = await dbContext.Subscribers
                .FirstOrDefaultAsync(x => x.LINEUserId == profileReuslt.UserId);
            if (profile is null)
            {
                // Create new account
                profile = new Subscriber()
                {
                    LINELoginAccessToken = result.AccessToken,
                    LINELoginIDToken = result.IdToken,
                    LINEUserId = profileReuslt.UserId,
                    Username = payload.Name,
                    Email = payload.Email ?? "",
                    Photo = payload.Picture,
                };
                dbContext.Subscribers.Add(profile);
                await dbContext.SaveChangesAsync();
            }
            else
            {
                profile.LINELoginAccessToken = result.AccessToken;
                profile.LINELoginIDToken = result.IdToken;
                profile.Username = payload.Name;
                profile.Email = payload.Email ?? "";
                profile.Photo = payload.Picture;
                await dbContext.SaveChangesAsync();
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, profile.Id.ToString()),
                //new Claim(ClaimTypes.Email, payload.Email),
                new Claim("FullName", payload.Name),
                new Claim(ClaimTypes.Role, (profile.Id == 1 ? "Admin" : "User")),
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await accessor.HttpContext.SignInAsync(new ClaimsPrincipal(claimsIdentity));
            return Results.Ok(result);
        }
        else
        {
            var result = JsonSerializer.Deserialize<LINELoginTokenError>(jsonString);
            return Results.BadRequest(result);
        }
    }

    public static async Task<IResult> SignOut(
        [FromServices] IConfiguration config,
        [FromServices] SubscriberContext db,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] IHttpContextAccessor accessor
    )
    {
        var revokeURL = config[$"{nameof(LINELoginHandler)}:revokeURL"];
        var clientId = config[$"{nameof(LINELoginHandler)}:client_id"];
        var clientSecret = config[$"{nameof(LINELoginHandler)}:client_secret"];
        var userId = accessor.HttpContext.User.Identity.Name;

        var http = httpClientFactory.CreateClient();
        // https://developers.line.biz/en/reference/line-login/#revoke-access-token
        /*
            curl -v -X POST https://api.line.me/oauth2/v2.1/revoke \
            -H "Content-Type: application/x-www-form-urlencoded" \
            -d "client_id={channel id}&client_secret={channel secret}&access_token={access token}"
        */
        var profile = await db.Subscribers.FirstOrDefaultAsync(x => x.Id.ToString() == userId);
        if (profile is null)
        {
            return Results.Redirect("/signin");
        }
        else
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", config[$"{nameof(LINELoginHandler)}:client_id"]),
                new KeyValuePair<string, string>("client_secret", config[$"{nameof(LINELoginHandler)}:client_secret"]),
                new KeyValuePair<string, string>("access_token", profile.LINELoginAccessToken),
            });

            var response = await http.PostAsync(revokeURL, content);
            var jsonString = await response.Content.ReadAsStringAsync();
            profile.LINELoginAccessToken = "";
            profile.LINELoginIDToken = "";
            await db.SaveChangesAsync();
        }

        await accessor.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Ok("You has been signed-out.");
    }

    private static string GetRedirectUri(IConfiguration config, IHttpContextAccessor httpContextAccessor)
    {
        var currentUrl = httpContextAccessor.HttpContext.Request.GetEncodedUrl();
        var authority = new Uri(currentUrl).GetLeftPart(UriPartial.Authority);
        var redirectUri = config[$"{nameof(LINELoginHandler)}:redirect_uri"];

        if (Uri.IsWellFormedUriString(redirectUri, UriKind.Relative))
        {
            redirectUri = authority + redirectUri;
        }

        return redirectUri;
    }
}