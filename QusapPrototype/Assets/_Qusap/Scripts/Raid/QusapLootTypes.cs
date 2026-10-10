using System;
using System.Collections.Generic;

namespace Qusap
{
    // Preserve serialized values and the Fragment spelling used by approved callers.
    public enum QusapLootCategory { Material = 0, Relic = 1, Consumable = 2, Weapon = 3, Armor = 4, Fragment = Material, Horn = 5, Objective = 6 }
    public enum QusapLootRarity { Common = 0, Uncommon = 1, Rare = 2, Epic = 3, Legendary = 4 }
    public enum QusapConsumableType { None, Healing }
    public enum QusapLootLocation { World, Backpack, SecurePocket, Equipped, DeathContainer, Stash, Consumed, Merged }
    public enum QusapLootResult { Success, Missing, InvalidSource, InvalidDestination, Full, Occupied, Ineligible, Blocked, Reserved, NoEffect, EquipmentRejected, StashUnavailable }
    public enum QusapRaidInventoryStatus { Active, Blocked, SettlementPending, Settled, ExtractionPending, Extracted }

    // Location, holder and slot are changed together exclusively by QusapLootWorld.
    // An Equipped location is a ledger projection, never another equipment slot.
    public sealed class QusapLootInstance
    {
        internal QusapLootInstance(string id, string raidId, QusapLootDefinition definition,
            string provenance, QusapWeaponInstance weapon, int quantity = 1)
        { LootInstanceId = id; RaidId = raidId; Definition = definition; Provenance = provenance; Weapon = weapon; Quantity = quantity; }
        public string LootInstanceId { get; }
        public string RaidId { get; }
        public QusapLootDefinition Definition { get; }
        public string DefinitionId => Definition.DefinitionId;
        public string Provenance { get; }
        public QusapWeaponInstance Weapon { get; }
        public int Quantity { get; internal set; }
        public QusapLootLocation Location { get; internal set; }
        public string HolderId { get; internal set; }
        public int SlotIndex { get; internal set; }
        internal bool Reserved { get; set; }
    }

    public readonly struct QusapLootSnapshot
    {
        public QusapLootSnapshot(QusapLootInstance item)
        {
            LootInstanceId = item.LootInstanceId; DefinitionId = item.DefinitionId;
            RaidId = item.RaidId; Provenance = item.Provenance; DisplayName = item.Definition.DisplayName;
            Location = item.Location; HolderId = item.HolderId; SlotIndex = item.SlotIndex;
            Category = item.Definition.Category; Weapon = item.Weapon; Quantity = item.Quantity; Rarity = item.Definition.Rarity;
        }
        public string LootInstanceId { get; }
        public string DefinitionId { get; }
        public string RaidId { get; }
        public string Provenance { get; }
        public string DisplayName { get; }
        public QusapLootLocation Location { get; }
        public string HolderId { get; }
        public int SlotIndex { get; }
        public QusapLootCategory Category { get; }
        public QusapWeaponInstance Weapon { get; }
        public int Quantity { get; }
        public QusapLootRarity Rarity { get; }
    }

    public sealed class QusapRaidInventorySnapshot
    {
        internal QusapRaidInventorySnapshot(string participant, QusapRaidInventoryStatus status,
            QusapLootSnapshot?[] backpack, QusapLootSnapshot? pocket, QusapLootSnapshot? equipped)
        { ParticipantId = participant; Status = status; Backpack = Array.AsReadOnly(backpack); SecurePocket = pocket; EquippedWeapon = equipped; }
        public string ParticipantId { get; }
        public QusapRaidInventoryStatus Status { get; }
        public IReadOnlyList<QusapLootSnapshot?> Backpack { get; }
        public QusapLootSnapshot? SecurePocket { get; }
        public QusapLootSnapshot? EquippedWeapon { get; }
    }

    public sealed class QusapDeathLootRecord
    {
        internal QusapDeathLootRecord(string id, string participant, QusapLootSnapshot[] transferred)
        { ContainerId = id; ParticipantId = participant; InitialTransferredSnapshot = Array.AsReadOnly(transferred); }
        public string ContainerId { get; }
        public string ParticipantId { get; }
        public IReadOnlyList<QusapLootSnapshot> InitialTransferredSnapshot { get; }
    }
}
