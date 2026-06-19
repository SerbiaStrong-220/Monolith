// (c) Space Exodus Team - EXDS-RL with CLA

using System.Text.Json.Serialization;

namespace Content.Server.SS220.EPA.DTO;

public sealed class RequestTokenResDTO
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;
}
