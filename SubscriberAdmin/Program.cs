using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SubscriberAdmin;
using WebApplication1.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddHttpClient();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(option =>
    {
        option.LoginPath = "/signin";
        option.LogoutPath = "/signout";
        option.AccessDeniedPath = "/accessdenied";
        ;
    });
builder.Services.AddAuthorization(options => { options.AddPolicy("AdminsOnly", policyBuilder => { policyBuilder.RequireClaim(ClaimTypes.Role, "Admin"); }); });
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
builder.Services.AddDbContext<SubscriberContext>(options => options.UseInMemoryDatabase("subs"));
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options => { options.Cookie.IsEssential = true; });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseSession();

// Line Login
app.MapGet("/signin", LINELoginHandler.SignIn)
    .WithName(nameof(LINELoginHandler.SignIn))
    .AllowAnonymous();
app.MapGet("/signin-callback", LINELoginHandler.SignInCallback)
    .WithName(nameof(LINELoginHandler.SignInCallback))
    .AllowAnonymous();
app.MapGet("/signout", LINELoginHandler.SignOut)
    .WithName(nameof(LINELoginHandler.SignOut))
    .AllowAnonymous();

app.MapGet("/subscribe", LINENotifyHandler.Subscribe)
    .WithName(nameof(LINENotifyHandler.Subscribe))
    .RequireAuthorization();
app.MapGet("/subscribe-callback", LINENotifyHandler.SubscribeCallback)
    .WithName(nameof(LINENotifyHandler.SubscribeCallback))
    .RequireAuthorization();
app.MapGet("/unsubscribe", LINENotifyHandler.Unsubscribe)
    .WithName(nameof(LINENotifyHandler.Unsubscribe))
    .RequireAuthorization();

// Normal User
app.MapGet("/profile", LINELoginHandler.Profile)
    .WithName(nameof(LINELoginHandler.Profile))
    .RequireAuthorization();
app.MapGet("/my", DefaultHandler.My)
    .WithName(nameof(DefaultHandler.My))
    .RequireAuthorization();

// AdminsOnly
app.MapGet("/all", DefaultHandler.AllSubscribers)
    .WithName(nameof(DefaultHandler.AllSubscribers))
    .RequireAuthorization("AdminsOnly");
app.MapGet("/notifyall", LINENotifyHandler.NotifyAll)
    .WithName(nameof(LINENotifyHandler.NotifyAll))
    .RequireAuthorization("AdminsOnly");

// Others
app.MapGet("/accessdenied", () => Results.Ok("Access Denied!"))
    .WithName("accessdenied")
    .AllowAnonymous();

app.MapGet("/claims", ([FromServices] IHttpContextAccessor accessor) =>
    {
        var claimsIdentity = accessor.HttpContext.User.Identity as ClaimsIdentity;
        return Results.Ok(claimsIdentity.Claims.ToDictionary(x => x.Type, x => x.Value));
    })
    .WithName("claims")
    .RequireAuthorization();

app.Run();