// (c) Space Exodus Team - EXDS-RL with CLA

using Robust.Shared.Serialization;

namespace Content.Shared.SS220.Discord;

[Serializable, NetSerializable]
public enum SponsorTier
{
    None,

    Developer,
    Spriter,
    QA,
    Mapper,
    WikiEdit,
    LoreEdit,
    Moderator,
    Administrator,
    Mentor,
    SeniorDevTeam,
    HeadDeveloper,

    // Тиры подписок должны идти в конце, для упрощения вычисления лучшего уровня поддержки.
    Shlopa,
    BigShlopa,
    HugeShlopa,
    GoldenShlopa,
    CriticalMassShlopa,
}
