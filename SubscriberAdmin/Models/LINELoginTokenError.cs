using System.Text.Json.Serialization;

namespace SubscriberAdmin.Models;

public class LINELoginTokenError
{
    [JsonPropertyName("error")]
    public string Error { get; set; }

    [JsonPropertyName("error_description")]
    public string ErrorDescription { get; set; }
}