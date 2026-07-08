using System.Numerics;
using Content.Shared.Hands.Components;
using Robust.Shared.GameObjects;

namespace Content.Client.Hands.Systems;

public sealed partial class HandsSystem
{
    private static string GetHandLayerKey(string key, Hand hand, HandsComponent hands)
    {
        return HasMultipleHandsAtLocation(hand, hands)
            ? $"{key}-{hand.Name}"
            : key;
    }

    private static PrototypeLayerData GetHandLayerData(
        PrototypeLayerData layerData,
        Hand hand,
        HandsComponent hands)
    {
        var keySuffix = HasMultipleHandsAtLocation(hand, hands)
            ? $"-{hand.Name}"
            : string.Empty;

        if (keySuffix.Length == 0 && hand.VisualOffset == Vector2.Zero)
            return layerData;

        HashSet<string>? mapKeys = null;
        if (layerData.MapKeys is not null)
        {
            mapKeys = new HashSet<string>();
            foreach (var mapKey in layerData.MapKeys)
                mapKeys.Add(mapKey + keySuffix);
        }

        PrototypeCopyToShaderParameters? copyToShaderParameters = null;
        if (layerData.CopyToShaderParameters is not null)
        {
            copyToShaderParameters = new PrototypeCopyToShaderParameters
            {
                LayerKey = layerData.CopyToShaderParameters.LayerKey + keySuffix,
                ParameterTexture = layerData.CopyToShaderParameters.ParameterTexture,
                ParameterUV = layerData.CopyToShaderParameters.ParameterUV,
            };
        }

        return new PrototypeLayerData
        {
            Shader = layerData.Shader,
            TexturePath = layerData.TexturePath,
            RsiPath = layerData.RsiPath,
            State = layerData.State,
            Scale = layerData.Scale,
            Rotation = layerData.Rotation,
            Offset = layerData.Offset.GetValueOrDefault() + hand.VisualOffset,
            Visible = layerData.Visible,
            Color = layerData.Color,
            MapKeys = mapKeys,
            RenderingStrategy = layerData.RenderingStrategy,
            CopyToShaderParameters = copyToShaderParameters,
            Cycle = layerData.Cycle,
            Loop = layerData.Loop,
        };
    }

    private static bool HasMultipleHandsAtLocation(Hand hand, HandsComponent hands)
    {
        var count = 0;
        foreach (var otherHand in hands.Hands.Values)
        {
            if (otherHand.Location != hand.Location)
                continue;

            count++;
            if (count > 1)
                return true;
        }

        return false;
    }
}
