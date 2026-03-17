using System;

namespace BetterCrewLink
{
    public static class Utilities
    {
        public static bool IsImpostor(this PlayerControl player)
        {
            return player?.Data?.Role != null && IsImpostor(player.Data.Role);
        }

        public static bool IsImpostor(this RoleBehaviour role)
        {
            return role.TeamType == RoleTeamTypes.Impostor;
        }

        public static bool IsCommsSabotaged()
        {
            if (!ShipStatus.Instance) return false;
            if (!ShipStatus.Instance.Systems.ContainsKey(SystemTypes.Comms)) return false;
            var comms = ShipStatus.Instance.Systems[SystemTypes.Comms].Cast<HudOverrideSystemType>();
            return comms != null && comms.IsActive;
        }
    }
}
