using Content.Shared.Atmos;
using Content.Shared.Containers.ItemSlots;
using Content.Server._Funkystation.Atmos.EntitySystems;
using System.Linq;

namespace Content.Server._Funkystation.Atmos.Components
{
    [RegisterComponent]
    public sealed partial class CrystallizerComponent : Component
    {
        [ViewVariables(VVAccess.ReadWrite)]
        public string? SelectedRecipeId { get; set; }

        [ViewVariables(VVAccess.ReadWrite)]
        public float GasInput { get; set; }

        [DataField("inlet")]
        public string InletName = "inlet";

        [DataField("regulator")]
        public string RegulatorName = "regulator";

        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("crystallizerGasMixture")]
        public GasMixture CrystallizerGasMixture { get; set; } = new();

        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("progressBar")]
        public float ProgressBar { get; set; } = 0f;

        // Exodus-begin: elapsed processing time, not a timestamp; unpowered and paused devices do not advance it.
        /// <summary>Accumulated processing time for the selected recipe, reduced when its conditions fail.</summary>
        [ViewVariables]
        public TimeSpan CraftingTime;
        // Exodus-end

        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("qualityLoss")]
        public float QualityLoss { get; set; } = 0f;

        [ViewVariables]
        [DataField("totalRecipeMoles")]
        public float TotalRecipeMoles { get; set; } = 0f;
    }
}
