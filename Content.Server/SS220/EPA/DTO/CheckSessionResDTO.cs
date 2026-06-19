// (c) Space Exodus Team - EXDS-RL with CLA

using System.Text.Json.Serialization;

namespace Content.Server.SS220.EPA.DTO;

public sealed class CheckSessionResDTO
{
    [JsonPropertyName("userId")]
    public string? UserId { get; set; } = string.Empty;

    [JsonPropertyName("token")]
    public string? Token { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public EPASessionStatus Status { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EPASessionStatus
{
    Waiting,
    Passed,
    Expired,
    Rejected
}
