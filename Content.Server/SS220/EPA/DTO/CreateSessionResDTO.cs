// (c) Space Exodus Team - EXDS-RL with CLA

using System.Text.Json.Serialization;

namespace Content.Server.SS220.EPA.DTO;

public sealed class CreateSessionResDTO
{
    [JsonPropertyName("accountLinkUrl")]
    public string AuthUrl { get; set; } = string.Empty;

    [JsonPropertyName("key")]
    public string SessionCode { get; set; } = string.Empty;
}
