// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Lokilife
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.GuideGenerator;

public sealed class VesselsJsonGenerator
{
    public static void PublishJson(StreamWriter file)
    {
        var prototypeManager = IoCManager.Resolve<IPrototypeManager>();
        var vessels = prototypeManager
            .EnumeratePrototypes<VesselPrototype>()
            .Where(x => !x.Abstract)
            .Select(x => new VesselEntry(x))
            .ToList();

        var serializeOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
        };

        file.Write(JsonSerializer.Serialize(vessels, serializeOptions));
    }

    private sealed class VesselEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; }

        [JsonPropertyName("name")]
        public string Name { get; }

        [JsonPropertyName("description")]
        public string Description { get; }

        [JsonPropertyName("price")]
        public int Price { get; }

        [JsonPropertyName("group")]
        public string Group { get; }

        [JsonPropertyName("size")]
        public string Size { get; }

        [JsonPropertyName("classes")]
        public List<string> Classes { get; }

        [JsonPropertyName("engines")]
        public List<string> Engines { get; }

        [JsonPropertyName("image")]
        public string Image { get; }

        public VesselEntry(VesselPrototype proto)
        {
            Id = proto.ID;
            Name = proto.Name;
            Description = proto.Description;
            Price = proto.Price;
            Group = proto.Group.ToString().ToLowerInvariant();
            Size = proto.Category.ToString().ToLowerInvariant();
            Classes = proto.Classes.Select(c => c.ToString().ToLowerInvariant()).ToList();
            Engines = proto.Engines.Select(e => e.ToString().ToLowerInvariant()).ToList();
            Image = "/" + Path.GetFileNameWithoutExtension(proto.ShuttlePath.ToString()) + "-0.png";
        }
    }
}
