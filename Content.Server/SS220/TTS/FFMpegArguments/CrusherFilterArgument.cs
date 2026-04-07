// (c) Space Exodus Team - EXDS-RL with CLA

using System.Globalization;
using System.Linq;
using FFMpegCore.Arguments;

namespace Content.Server.SS220.TTS.FFMPegArguments;

public sealed class CrusherFilterArgument : IAudioFilterArgument
{
    private readonly Dictionary<string, string> _arguments = new Dictionary<string, string>();

    public CrusherFilterArgument(
        double levelIn = 1d,
        double levelOut = 1d,
        int bits = 1,
        double mix = 1.0,
        string mode = "log",
        double aa = 1d)
    {
        _arguments.Add("level_in", levelIn.ToString("n2", CultureInfo.InvariantCulture));
        _arguments.Add("level_out", levelOut.ToString("n2", CultureInfo.InvariantCulture));
        _arguments.Add("bits", bits.ToString("0", CultureInfo.InvariantCulture));
        _arguments.Add("mix", mix.ToString("n2", CultureInfo.InvariantCulture));
        _arguments.Add("mode", mode);
        _arguments.Add("aa", aa.ToString("n2", CultureInfo.InvariantCulture));
    }

    public string Key => "acrusher";

    public string Value => string.Join(":", _arguments.Select(pair => pair.Key + "=" + pair.Value));
}
