using Content.Shared._Mono.Company;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Shared.IoC;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    private void UpdateCompanyImage(CompanyPrototype company)
    {
        if (company.LobbyImage is { } lobbyImage)
        {
            CompanyImage.Texture = _entManager.System<SpriteSystem>().Frame0(lobbyImage);
            CompanyImage.Visible = true;
            return;
        }

        if (string.IsNullOrEmpty(company.Image))
        {
            CompanyImage.Visible = false;
            return;
        }

        CompanyImage.Texture = IoCManager.Resolve<IResourceCache>()
            .GetResource<TextureResource>(company.Image)
            .Texture;
        CompanyImage.Visible = true;
    }
}
