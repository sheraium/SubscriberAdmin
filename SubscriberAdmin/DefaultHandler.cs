using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Models;

public static class DefaultHandler
{
    public static async Task<IResult> AllSubscribers(
        [FromServices] IHttpContextAccessor accessor,
        [FromServices] SubscriberContext db
        )
    {
        return Results.Ok(await db.Subscribers.ToListAsync());
    }

    public static async Task<IResult> My(
        [FromServices] IHttpContextAccessor accessor,
        [FromServices] SubscriberContext db
        )
    {
        var userId = accessor.HttpContext.User.Identity.Name;

        var profile = await db.Subscribers.FirstOrDefaultAsync(x => x.Id.ToString() == userId);
        if (profile is null)
        {
            return Results.Redirect("/signout");
        }
        else
        {
            return Results.Ok(profile);
        }
    }
}