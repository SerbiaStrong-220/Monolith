// (c) Space Exodus Team - EXDS-RL with CLA

using System.Diagnostics.CodeAnalysis;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Content.Server.SS220.EPA.DTO;
using Content.Server.SS220.Extensions;
using Content.Shared.SS220.CCVars;
using Microsoft.IdentityModel.Tokens;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Content.Server.SS220.EPA;

public sealed partial class EPAManager
{
    private ECDsa _ecdsa = ECDsa.Create();

    private JwtSecurityTokenHandler _tokenHandler = default!;
    private TokenValidationParameters _tokenValidation = default!;

    private void InitializeJWT()
    {
        _tokenHandler = new();
        _tokenValidation = new()
        {
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(5),

            ValidateIssuerSigningKey = true,

            RequireExpirationTime = true,
            RequireSignedTokens = true
        };

        var pemKey = _config.GetCVar(CCVars220.EPAJwtPemKeyPath);
        if (!TryImportPem(pemKey))
        {
            var derKey = _config.GetCVar(CCVars220.EPAJwtDerKey);
            TryImportDer(derKey);
        }

        _config.OnValueChanged(CCVars220.EPAJwtDerKey, OnRawKeyChange);
        _config.OnValueChanged(CCVars220.EPAJwtPemKeyPath, OnPemPathChange);
        _config.OnValueChanged(CCVars220.EPAJwtIssuer, OnIssuerChange, true);
        _config.OnValueChanged(CCVars220.EPAJwtAudience, OnAudienceChange, true);
        _config.OnValueChanged(CCVars220.EPAJwtClockSkew, OnClockSkewChange, true);
    }

    private void OnIssuerChange(string issuer)
    {
        _tokenValidation.ValidateIssuer = !string.IsNullOrEmpty(issuer);
        _tokenValidation.ValidIssuer = issuer;
    }

    private void OnAudienceChange(string audience)
    {
        _tokenValidation.ValidateAudience = !string.IsNullOrEmpty(audience);
        _tokenValidation.ValidAudience = audience;
    }

    private void OnClockSkewChange(int seconds)
    {
        _tokenValidation.ClockSkew = TimeSpan.FromSeconds(seconds);
    }

    private void OnPemPathChange(string newPath)
    {
        if (string.IsNullOrEmpty(newPath))
            return;

        TryImportPem(newPath);
    }

    private bool TryImportPem(string newPath)
    {
        if (string.IsNullOrEmpty(newPath))
            return false;

        if (!File.Exists(newPath))
        {
            _sawmill.Error("Submitted path for PEM-key to invalid location");
            return false;
        }

        var raw = File.ReadAllText(newPath, EncodingHelpers.UTF8);
        try
        {
            _ecdsa.ImportFromPem(raw);
            UpdateJwtKey();
            return true;
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Error occured during importing PEM key at {newPath}\n{ex.ToStringBetter()}");
            return false;
        }
    }

    private void OnRawKeyChange(string newKey)
    {
        if (string.IsNullOrEmpty(newKey))
            return;

        TryImportDer(newKey);
    }

    private bool TryImportDer(string newKey)
    {
        if (string.IsNullOrEmpty(newKey))
            return false;

        try
        {
            var derBytes = Convert.FromBase64String(newKey);
            _ecdsa.ImportSubjectPublicKeyInfo(derBytes, out _);
            UpdateJwtKey();
            return true;
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Error occured during importing DER key\n{ex.ToStringBetter()}");
            return false;
        }
    }

    private void UpdateJwtKey()
    {
        _tokenValidation.IssuerSigningKey = new ECDsaSecurityKey(_ecdsa);
    }

    private bool TryReadToken(INetChannel channel, string token, [NotNullWhen(true)] out EPATokenPayload? payload)
    {
        payload = null;

        token = token.Trim()
            .TrimStart('\uFEFF', '\u200B', '\u200C', '\u200D', '\u00A0') // neccesary: cut UTF8 trash
            .TrimEnd('\0');

        try
        {
            var principal = _tokenHandler.ValidateToken(token, _tokenValidation, out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwt) return false;

            var payloadJson = jwt.Payload.SerializeToJson();
            var deserializedPayload = JsonSerializer.Deserialize<EPATokenPayload>(payloadJson);

            if (deserializedPayload == null)
            {
                _sawmill.Debug($"Token deserialization failed for {channel.ToPrettyString()}");
                return false;
            }

            if (!IPAddress.IsLoopback(channel.RemoteEndPoint.Address) &&
                !deserializedPayload.IPs.Contains(channel.RemoteEndPoint.Address))
            {
                _sawmill.Verbose($"Token for {channel.ToPrettyString()} was sent from an unexpected network and so was rejected; probably a VPN?");
                return false;
            }

            payload = deserializedPayload;
            return true;
        }
        catch (Exception ex)
        {
            _sawmill.Debug($"Token validation for {channel.ToPrettyString()} failed:\n{ex.ToStringBetter()}");
            return false;
        }
    }
}
