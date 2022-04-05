using System.Text.Json.Serialization;

namespace SubscriberAdmin.Models;

public class LINENotifyTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; }
}