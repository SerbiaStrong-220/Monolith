// SS220 BanChat adapted to the existing ban schema, without changing role ban storage.
using System.ComponentModel.DataAnnotations.Schema;
using Content.Shared.Database._Exodus.Chat;

namespace Content.Server.Database._Exodus.Chat;

public sealed class BanChat
{
    public int Id { get; set; }
    public BannableChats Chat { get; set; }

    [ForeignKey(nameof(Ban))]
    public int BanId { get; set; }

    public Ban? Ban { get; set; }
}
