using System.Text.RegularExpressions;
using Content.Server.Speech.Components;
using Robust.Shared.Random; // Exodus-Localization

namespace Content.Server.Speech.EntitySystems;

public sealed partial class FrontalLispSystem : EntitySystem
{
    // @formatter:off
    private static readonly Regex RegexUpperTh = new(@"[T]+[Ss]+|[S]+[Cc]+(?=[IiEeYy]+)|[C]+(?=[IiEeYy]+)|[P][Ss]+|([S]+[Tt]+|[T]+)(?=[Ii]+[Oo]+[Uu]*[Nn]*)|[C]+[Hh]+(?=[Ii]*[Ee]*)|[Z]+|[S]+|[X]+(?=[Ee]+)");
    private static readonly Regex RegexLowerTh = new(@"[t]+[s]+|[s]+[c]+(?=[iey]+)|[c]+(?=[iey]+)|[p][s]+|([s]+[t]+|[t]+)(?=[i]+[o]+[u]*[n]*)|[c]+[h]+(?=[i]*[e]*)|[z]+|[s]+|[x]+(?=[e]+)");
    private static readonly Regex RegexUpperEcks = new(@"[E]+[Xx]+[Cc]*|[X]+");
    private static readonly Regex RegexLowerEcks = new(@"[e]+[x]+[c]*|[x]+");

    // Exodus-Localization-Start
    private static readonly Regex RegexUpperSs = new(@"С");
    private static readonly Regex RegexLowerSs = new(@"с");
    private static readonly Regex RegexUpperChs = new(@"Ч");
    private static readonly Regex RegexLowerChs = new(@"ч");
    private static readonly Regex RegexUpperCs = new(@"Ц");
    private static readonly Regex RegexLowerCs = new(@"ц");
    private static readonly Regex RegexUpperTs = new(@"\B[Т](?![АЕЁИОУЫЭЮЯаеёиоуыэюя])");
    private static readonly Regex RegexLowerTs = new(@"\B[т](?![АЕЁИОУЫЭЮЯаеёиоуыэюя])");
    private static readonly Regex RegexUpperZs = new(@"З");
    private static readonly Regex RegexLowerZs = new(@"з");
    // Exodus-Localization-End
    // @formatter:on

    [Dependency] private IRobustRandom _random = default!; // Exodus-Localization

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FrontalLispComponent, AccentGetEvent>(OnAccent);
    }

    private void OnAccent(EntityUid uid, FrontalLispComponent component, AccentGetEvent args)
    {
        var message = args.Message;

        // handles ts, sc(i|e|y), c(i|e|y), ps, st(io(u|n)), ch(i|e), z, s
        message = RegexUpperTh.Replace(message, "Th");
        message = RegexLowerTh.Replace(message, "th");
        // handles ex(c), x
        message = RegexUpperEcks.Replace(message, "Ekth");
        message = RegexLowerEcks.Replace(message, "ekth");

        // Exodus-Localization-Start
        // с - ш
        message = RegexUpperSs.Replace(message, _random.Prob(0.9f) ? "Ш" : "С");
        message = RegexLowerSs.Replace(message, _random.Prob(0.9f) ? "ш" : "с");
        // ч - ш
        message = RegexUpperChs.Replace(message, _random.Prob(0.9f) ? "Ш" : "Ч");
        message = RegexLowerChs.Replace(message, _random.Prob(0.9f) ? "ш" : "ч");
        // ц - ч
        message = RegexUpperCs.Replace(message, _random.Prob(0.9f) ? "Ч" : "Ц");
        message = RegexLowerCs.Replace(message, _random.Prob(0.9f) ? "ч" : "ц");
        // т - ч
        message = RegexUpperTs.Replace(message, _random.Prob(0.9f) ? "Ч" : "Т");
        message = RegexLowerTs.Replace(message, _random.Prob(0.9f) ? "ч" : "т");
        // з - ж
        message = RegexUpperZs.Replace(message, _random.Prob(0.90f) ? "Ж" : "З");
        message = RegexLowerZs.Replace(message, _random.Prob(0.90f) ? "ж" : "з");
        // Exodus-Localization-End

        args.Message = message;
    }
}
