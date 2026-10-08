using System;
using System.Collections.Generic;

namespace Qusap
{
    public enum QusapLootCategory { Fragment, Relic, Consumable, Weapon, Armor, Horn, Objective }
    public enum QusapConsumableType { None, Healing }
    public enum QusapLootLocation { World, Backpack, SecurePocket, Equipped, DeathContainer, Stash, Consumed }
    public enum QusapLootResult { Success, Missing, InvalidSource, InvalidDestination, Full, Occupied, Ineligible, Blocked, Reserved, NoEffect, EquipmentRejected, StashUnavailable }
    public enum QusapRaidInventoryStatus { Active, Blocked, SettlementPending, Settled }

    // Location, holder and slot are changed together exclusively by QusapLootWorld.
    // An Equipped location is a ledger projection, never another equipment slot.
    public sealed class QusapLootInstance
    {
        internal QusapLootInstance(string id, string raidId, QusapLootDefinition definition,
            string provenance, QusapWeaponInstance weapon)
        { LootInstanceId = id; RaidId = raidId; Definition = definition; Provenance = provenance; Weapon = weapon; }
        public string LootInstanceId { get; }
        public string RaidId { get; }
        public QusapLootDefinition Definition { get; }
        public string DefinitionId => Definition.DefinitionId;
        public string Provenance { get; }
        public QusapWeaponInstance Weapon { get; }
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
            Category = item.Definition.Category; Weapon = item.Weapon;
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
