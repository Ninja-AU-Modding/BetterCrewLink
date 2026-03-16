using MiraAPI.Roles;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterCrewLink
{
    public static class Utilities
    {
        public static bool IsImpostor(this PlayerControl player)
        {
            return player?.Data && player?.Data?.Role && player?.Data?.Role.IsImpostor() == true;
        }
        public static bool IsImpostor(this RoleBehaviour role)
        {
            return role is ICustomRole customRole
                ? customRole.Team is ModdedRoleTeams.Impostor
                : role.TeamType is RoleTeamTypes.Impostor;
        }
        public static bool IsCommsSabotaged()
        {
            if (!ShipStatus.Instance) return false;
            var comms = ShipStatus.Instance.Systems[SystemTypes.Comms].Cast<HudOverrideSystemType>();
            return comms != null && comms.IsActive;
        }
    }
}
