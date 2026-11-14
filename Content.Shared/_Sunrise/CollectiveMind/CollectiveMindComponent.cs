using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Sunrise.CollectiveMind
{
    [RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
    public sealed partial class CollectiveMindComponent : Component
    {
        [DataField, AutoNetworkedField]
        public List<ProtoId<CollectiveMindPrototype>> Minds = [];
    }
}
