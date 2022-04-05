using System.Text.Json.Serialization;

namespace SubscriberAdmin.Models;

public class LINENotifyResult
{
    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; }
}