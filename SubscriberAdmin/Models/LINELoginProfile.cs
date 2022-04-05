using System.Text.Json.Serialization;

namespace SubscriberAdmin.Models;

public class LINELoginProfile
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; }

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; }

    [JsonPropertyName("pictureUrl")]
    public string PictureUrl { get; set; }

    [JsonPropertyName("statusMessage")]
    public string StatusMessage { get; set; }
}