using System.Text.Json.Serialization;

namespace SubscriberAdmin.Models;

public class JwtPayload
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("picture")]
    public string Picture { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; }
}