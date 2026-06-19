// (c) Space Exodus Team - EXDS-RL with CLA

using System.Net;
using System.Text.Json.Serialization;
using Content.Server.SS220.Serialization;

namespace Content.Server.SS220.EPA.DTO;

public sealed partial class EPATokenPayload
{
    [JsonPropertyName("iat")]
    public uint IssuedAt { get; set; }

    [JsonPropertyName("iss")]
    public string Issuer { get; set; } = string.Empty;

    [JsonPropertyName("exp")]
    public uint Expiration { get; set; }

    [JsonPropertyName("aud")]
    public string Audience { get; set; } = string.Empty;

    [JsonPropertyName("sub")]
    public Guid UserId { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("ips")]
    [JsonConverter(typeof(IPAddressArrayConverter))]
    public IPAddress[] IPs { get; set; } = [];
}
