using System;
using System.Linq;

namespace Qusap
{
    // Extends the same raid ledger. No inventory, settlement or ownership authority is introduced.
    public sealed partial class QusapLootWorld
    {
        public int DeathLootDestroyed { get; private set; }
        internal void DestroyDeathLoaner(QusapLootInstance item)
        {
            if (!item.RaidLoaner || item.Weapon == null || item.Weapon.IsEquipped)
                throw new InvalidOperationException("Release a death loaner before destroying its native identity.");
            Move(item, QusapLootLocation.Destroyed);
            if (!item.Weapon.IsRetired) { item.Weapon.RetireFromRaid(); LoanersDestroyed++; }
        }
        internal int DestroyRemainingDeathLoot()
        {
            var abandoned = items.Values.Where(i => i.Location == QusapLootLocation.DeathContainer).ToArray();
            foreach (var item in abandoned)
            {
                if (item.Weapon?.IsEquipped == true) throw new InvalidOperationException("Death cargo cannot have a native equipped owner.");
                item.Weapon?.RetireFromRaid(); Move(item, QusapLootLocation.Destroyed);
            }
            DeathLootDestroyed += abandoned.Length;
            return abandoned.Length;
        }
    }
}
