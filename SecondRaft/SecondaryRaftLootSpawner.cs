using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

internal static class SecondaryRaftLootSpawner
{
    private const float MinimumRaftGap = 20f;
    private const float ScanInterval = 0.4f;
    private const int MaxLootPerSecondaryRaft = 80;
    private const int MaxSecondaryLootTotal = 240;
    private const int MaxSpawnAttempts = 8;
    private const float MinimumHorizontalSpeedSquared = 1.5f;

    private sealed class RaftTimers
    {
        public float planks = -1f;
        public float items = -1f;
    }

    private sealed class LootOwner
    {
        public int raftId;
        public ObjectSpawner_RaftDirection source;
    }

    private struct Footprint
    {
        public Vector2[] corners;
    }

    private static readonly Dictionary<int, RaftTimers> Timers =
        new Dictionary<int, RaftTimers>();

    private static readonly Dictionary<PickupItem_Networked, LootOwner> Owners =
        new Dictionary<PickupItem_Networked, LootOwner>();

    private static ObjectSpawner_RaftDirection plankSpawner;
    private static ObjectSpawner_RaftDirection itemSpawner;
    private static int originalPlankLimit = -1;
    private static int originalItemLimit = -1;
    private static float elapsed;
    private static int spawnErrorsLogged;

    public static void Reset()
    {
        RestoreVanillaLimits();
        Timers.Clear();
        Owners.Clear();
        plankSpawner = null;
        itemSpawner = null;
        originalPlankLimit = -1;
        originalItemLimit = -1;
        elapsed = 0f;
        spawnErrorsLogged = 0;
    }

    public static void Update()
    {
        if (!Raft_Network.IsHost)
            return;

        elapsed += Time.deltaTime;
        if (elapsed < ScanInterval)
            return;

        float delta = elapsed;
        elapsed = 0f;

        ObjectSpawnerManager manager = ComponentManager<ObjectSpawnerManager>.Value;
        if (manager == null || manager.floatingObjectParent == null)
            return;

        SetSources(manager.plankSpawner, manager.itemSpawner);
        CleanupOwnedLoot();
        UpdateVanillaLimits();

        if (plankSpawner == null || itemSpawner == null)
            return;

        if (!GameModeValueManager.GetCurrentGameModeValue()
                .enviromentalVariables.spawnObjects)
            return;

        Raft mainRaft = ComponentManager<Raft>.Value;
        RaftBounds mainBounds = ComponentManager<RaftBounds>.Value;
        Raft_Network network = ComponentManager<Raft_Network>.Value;
        if (mainRaft == null || network == null)
            return;

        Footprint mainFootprint = GetMainFootprint(mainRaft, mainBounds);
        List<SecondaryRaftRoot> rafts = new List<SecondaryRaftRoot>();
        foreach (SecondaryRaftRoot root in MultiRaftRegistry.GetRoots())
        {
            if (root != null && root.HasPlacedBlocks)
                rafts.Add(root);
        }
        rafts.Sort((a, b) => a.RaftId.CompareTo(b.RaftId));

        Dictionary<int, Footprint> footprints = new Dictionary<int, Footprint>();
        foreach (SecondaryRaftRoot root in rafts)
            footprints[root.RaftId] = GetSecondaryFootprint(root);

        List<SecondaryRaftRoot> selected = new List<SecondaryRaftRoot>();
        float gapSquared = MinimumRaftGap * MinimumRaftGap;

        foreach (SecondaryRaftRoot root in rafts)
        {
            Footprint hull = footprints[root.RaftId];

            // При сближении с основным плотом дополнительный спавн отключается
            if (DistanceSquared(hull, mainFootprint) < gapSquared)
                continue;

            bool tooClose = false;
            for (int i = 0; i < selected.Count; i++)
            {
                if (DistanceSquared(hull, footprints[selected[i].RaftId]) < gapSquared)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose)
                continue;

            selected.Add(root);
        }

        foreach (SecondaryRaftRoot root in selected)
        {
            if (root.IsAnchored || root.Body == null)
                continue;

            Vector3 velocity = root.Body.velocity;
            velocity.y = 0f;
            if (velocity.sqrMagnitude <= MinimumHorizontalSpeedSquared)
                continue;

            if (Owners.Count >= MaxSecondaryLootTotal ||
                CountForRaft(root.RaftId) >= MaxLootPerSecondaryRaft)
                continue;

            Vector3 direction = velocity.normalized;
            RaftTimers timers;
            if (!Timers.TryGetValue(root.RaftId, out timers))
            {
                timers = new RaftTimers();
                Timers[root.RaftId] = timers;
            }

            UpdateSpawnerTimer(root, plankSpawner, network.PlayerCount,
                direction, delta, ref timers.planks);
            UpdateSpawnerTimer(root, itemSpawner, network.PlayerCount,
                direction, delta, ref timers.items);
        }

        List<int> removed = null;
        foreach (int raftId in Timers.Keys)
        {
            if (MultiRaftRegistry.GetRoot(raftId) != null)
                continue;
            if (removed == null)
                removed = new List<int>();
            removed.Add(raftId);
        }
        if (removed != null)
            foreach (int raftId in removed)
                Timers.Remove(raftId);
    }

    private static void SetSources(ObjectSpawner_RaftDirection planks,
        ObjectSpawner_RaftDirection items)
    {
        if (plankSpawner == planks && itemSpawner == items)
            return;

        RestoreVanillaLimits();
        plankSpawner = planks;
        itemSpawner = items;
        originalPlankLimit = planks != null ? planks.maxSpawnedObjects : -1;
        originalItemLimit = items != null ? items.maxSpawnedObjects : -1;
    }

    private static void RestoreVanillaLimits()
    {
        if (plankSpawner != null && originalPlankLimit >= 0)
            plankSpawner.maxSpawnedObjects = originalPlankLimit;
        if (itemSpawner != null && originalItemLimit >= 0)
            itemSpawner.maxSpawnedObjects = originalItemLimit;
    }

    private static void UpdateVanillaLimits()
    {
        // Лимит основного плота не расходуется мусором вторых плотов
        if (plankSpawner != null && originalPlankLimit >= 0)
            plankSpawner.maxSpawnedObjects =
                originalPlankLimit + CountForSpawner(plankSpawner);
        if (itemSpawner != null && originalItemLimit >= 0)
            itemSpawner.maxSpawnedObjects =
                originalItemLimit + CountForSpawner(itemSpawner);
    }

    private static int CountForSpawner(ObjectSpawner_RaftDirection spawner)
    {
        int count = 0;
        foreach (LootOwner owner in Owners.Values)
            if (owner.source == spawner)
                count++;
        return count;
    }

    private static int CountForRaft(int raftId)
    {
        int count = 0;
        foreach (LootOwner owner in Owners.Values)
            if (owner.raftId == raftId)
                count++;
        return count;
    }

    private static void UpdateSpawnerTimer(SecondaryRaftRoot root,
        ObjectSpawner_RaftDirection source, int playerCount,
        Vector3 direction, float delta, ref float remaining)
    {
        if (source == null || source.spawnSettingsAsset == null)
            return;

        ObjectSpawnerAssetSettings settings =
            source.spawnSettingsAsset.GetSettings(playerCount);
        if (settings == null)
            return;

        if (remaining < 0f)
            remaining = Mathf.Max(0.1f, settings.spawnRateInterval.GetRandomValue());

        remaining -= delta;
        if (remaining > 0f)
            return;

        remaining = Mathf.Max(0.1f, settings.spawnRateInterval.GetRandomValue());
        int amount = Mathf.Clamp(settings.spawnAmount.GetRandomValue(), 0, 30);
        for (int i = 0; i < amount; i++)
        {
            if (Owners.Count >= MaxSecondaryLootTotal ||
                CountForRaft(root.RaftId) >= MaxLootPerSecondaryRaft)
                break;

            SpawnOne(root, source, direction);
        }
    }

    private static void SpawnOne(SecondaryRaftRoot root,
        ObjectSpawner_RaftDirection source, Vector3 direction)
    {
        SpawnableFloatingObject chosen = null;
        for (int attempt = 0; attempt < MaxSpawnAttempts; attempt++)
        {
            chosen = source.GetObjectToSpawn(true);
            if (chosen != null)
                break;
        }
        if (chosen == null)
            return;

        Vector3 right = Vector3.Cross(direction, Vector3.up).normalized;
        Vector3 position = root.transform.position +
            direction * source.spawnDistanceFromRaft;

        if (source.spawnPositions != null && source.spawnPositions.Length > 0)
        {
            SpawnPosition area = source.spawnPositions[
                UnityEngine.Random.Range(0, source.spawnPositions.Length)];
            if (area != null)
            {
                position += right * (area.horizontalOffset +
                    area.RandomHorizontalSize);
                position += direction * area.RandomVerticalSize;
            }
        }

        // Положение по высоте берётся из штатного спавнера, а не из волны
        position.y = source.transform.position.y;
        if (!CanSpawnHere(position))
            return;

        PickupItem_Networked pickup;
        try
        {
            // Штатный вызов инициализирует необходимые сетевые компоненты
            pickup = source.SpawnItemNetwork(position, unchecked((int)chosen.uniqueIndex));
        }
        catch (Exception exception)
        {
            if (spawnErrorsLogged < 3)
            {
                Debug.LogWarning("Second Raft: loot spawn failed: " + exception.Message);
                spawnErrorsLogged++;
            }
            return;
        }
        if (pickup != null)
        {
            Owners[pickup] = new LootOwner
            {
                raftId = root.RaftId,
                source = source
            };
        }
    }

    private static bool CanSpawnHere(Vector3 position)
    {
        List<ObjectSpawnerNoSpawnZone> zones = ObjectSpawner_RaftDirection.NoSpawnZones;
        if (zones == null)
            return true;

        for (int i = 0; i < zones.Count; i++)
        {
            ObjectSpawnerNoSpawnZone zone = zones[i];
            if (zone == null)
                continue;
            Vector3 difference = position - zone.transform.position;
            difference.y = 0f;
            if (difference.sqrMagnitude <= zone.radius * zone.radius)
                return false;
        }
        return true;
    }

    private static void CleanupOwnedLoot()
    {
        List<PickupItem_Networked> expired = new List<PickupItem_Networked>();
        foreach (KeyValuePair<PickupItem_Networked, LootOwner> entry in Owners)
        {
            PickupItem_Networked pickup = entry.Key;
            LootOwner owner = entry.Value;
            if (pickup == null || owner.source == null ||
                !pickup.gameObject.activeInHierarchy ||
                pickup.spawner != owner.source ||
                !owner.source.spawnedObjects.Contains(pickup))
            {
                expired.Add(pickup);
                continue;
            }

            SecondaryRaftRoot raft = MultiRaftRegistry.GetRoot(owner.raftId);
            if (raft == null ||
                IsOutOfRange(pickup.transform.position,
                    raft.transform.position, owner.source))
            {
                expired.Add(pickup);
                PickupObjectManager.RemovePickupItemNetwork(pickup);
            }
        }
        foreach (PickupItem_Networked item in expired)
            Owners.Remove(item);
    }

    private static bool IsOutOfRange(Vector3 itemPosition,
        Vector3 raftPosition, ObjectSpawner_RaftDirection source)
    {
        float limit = Mathf.Max(source.removeDistanceFromRaft,
            source.spawnDistanceFromRaft + 25f);
        float dx = itemPosition.x - raftPosition.x;
        float dz = itemPosition.z - raftPosition.z;
        return dx * dx + dz * dz >= limit * limit;
    }

    public static bool HandlesRemoval(ObjectSpawner_RaftDirection source)
    {
        if (source == null)
            return false;
        foreach (LootOwner owner in Owners.Values)
            if (owner.source == source)
                return true;
        return false;
    }

    public static bool RemoveItemsUsingCorrectRaft(ObjectSpawner_RaftDirection source)
    {
        if (!HandlesRemoval(source))
            return true;

        for (int i = source.spawnedObjects.Count - 1; i >= 0; i--)
        {
            PickupItem_Networked item = source.spawnedObjects[i];
            if (item == null)
                continue;

            LootOwner owner;
            if (Owners.TryGetValue(item, out owner))
                continue;

            // Обычный мусор удаляется по исходным правилам основного плота
            Vector3 difference = item.transform.position - source.transform.position;
            difference.y = 0f;
            if (difference.sqrMagnitude >=
                source.removeDistanceFromRaft * source.removeDistanceFromRaft)
                PickupObjectManager.RemovePickupItemNetwork(item);
        }
        return false;
    }

    public static void OnVanillaSpawn(PickupItem_Networked spawned)
    {
        if (spawned != null)
            Owners.Remove(spawned);
    }

    private static Footprint GetMainFootprint(Raft raft, RaftBounds bounds)
    {
        Transform t = raft.transform;
        Vector3 center = bounds != null ? bounds.Center : t.position;
        float halfWidth = bounds != null && bounds.Width > 0f
            ? bounds.Width * 0.5f : 0.75f;
        float halfLength = bounds != null && bounds.Length > 0f
            ? bounds.Length * 0.5f : 0.75f;
        Vector3 right = bounds != null ? bounds.RightDir : t.right;
        Vector3 forward = bounds != null ? bounds.ForwardDir : t.forward;
        return MakeFootprint(center, right, forward, halfWidth, halfLength);
    }

    private static Footprint GetSecondaryFootprint(SecondaryRaftRoot root)
    {
        Transform pivot = root.BuildPivot;
        Block[] blocks = root.GetComponentsInChildren<Block>();
        for (int pass = 0; pass < 2; pass++)
        {
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minZ = float.MaxValue;
            float maxZ = float.MinValue;
            bool found = false;

            foreach (Block block in blocks)
            {
                if (block == null || block.buildableItem == null ||
                    !block.hasBeenPlaced || !block.gameObject.activeInHierarchy)
                    continue;

                if (pass == 0 &&
                    !Block.IsBlockIndexFoundation(block.buildableItem.UniqueIndex))
                    continue;
                if (pass == 1 && !block.IsWalkable())
                    continue;

                Vector3 local = pivot.InverseTransformPoint(block.transform.position);
                minX = Mathf.Min(minX, local.x - 0.75f);
                maxX = Mathf.Max(maxX, local.x + 0.75f);
                minZ = Mathf.Min(minZ, local.z - 0.75f);
                maxZ = Mathf.Max(maxZ, local.z + 0.75f);
                found = true;
            }

            if (found)
            {
                Vector3 localCenter = new Vector3(
                    (minX + maxX) * 0.5f, 0f,
                    (minZ + maxZ) * 0.5f);
                Vector3 center = root.transform.TransformPoint(localCenter);
                return MakeFootprint(center, root.transform.right,
                    root.transform.forward, (maxX - minX) * 0.5f,
                    (maxZ - minZ) * 0.5f);
            }
        }
        return MakeFootprint(root.transform.position,
            root.transform.right, root.transform.forward, 0.75f, 0.75f);
    }

    private static Footprint MakeFootprint(Vector3 center, Vector3 right,
        Vector3 forward, float halfX, float halfZ)
    {
        right.y = 0f;
        forward.y = 0f;
        right.Normalize();
        forward.Normalize();
        Vector3 r = right * halfX;
        Vector3 f = forward * halfZ;
        return new Footprint
        {
            corners = new[]
            {
                XZ(center - r - f), XZ(center + r - f),
                XZ(center + r + f), XZ(center - r + f)
            }
        };
    }

    private static Vector2 XZ(Vector3 p)
    {
        return new Vector2(p.x, p.z);
    }

    private static float DistanceSquared(Footprint a, Footprint b)
    {
        Vector2[] ap = a.corners;
        Vector2[] bp = b.corners;
        if (Inside(ap, bp[0]) || Inside(bp, ap[0]))
            return 0f;

        float best = float.MaxValue;
        for (int i = 0; i < 4; i++)
        {
            Vector2 a0 = ap[i];
            Vector2 a1 = ap[(i + 1) % 4];
            for (int j = 0; j < 4; j++)
            {
                Vector2 b0 = bp[j];
                Vector2 b1 = bp[(j + 1) % 4];
                if (SegmentsCross(a0, a1, b0, b1))
                    return 0f;

                best = Mathf.Min(best, PointSegmentDistanceSq(a0, b0, b1));
                best = Mathf.Min(best, PointSegmentDistanceSq(b0, a0, a1));
            }
        }
        return best;
    }

    private static bool Inside(Vector2[] polygon, Vector2 p)
    {
        bool positive = false;
        bool negative = false;
        for (int i = 0; i < 4; i++)
        {
            float c = Cross(polygon[(i + 1) % 4] - polygon[i],
                p - polygon[i]);
            if (c > 0.0001f) positive = true;
            if (c < -0.0001f) negative = true;
        }
        return !(positive && negative);
    }

    private static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float ac = Cross(b - a, c - a);
        float ad = Cross(b - a, d - a);
        float ca = Cross(d - c, a - c);
        float cb = Cross(d - c, b - c);
        return ((ac > 0f && ad < 0f) || (ac < 0f && ad > 0f)) &&
               ((ca > 0f && cb < 0f) || (ca < 0f && cb > 0f));
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static float PointSegmentDistanceSq(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float length = ab.sqrMagnitude;
        float t = length > 0.00001f
            ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / length) : 0f;
        return (p - a - ab * t).sqrMagnitude;
    }
}

[HarmonyPatch(typeof(ObjectSpawner_RaftDirection), "RemoveItemsOutsideRange")]
internal static class SecondaryRaftLootRemovalPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ObjectSpawner_RaftDirection __instance)
    {
        return SecondaryRaftLootSpawner.RemoveItemsUsingCorrectRaft(__instance);
    }
}

[HarmonyPatch(typeof(ObjectSpawner), "SpawnItem")]
internal static class SecondaryRaftLootReusePatch
{
    [HarmonyPostfix]
    private static void Postfix(PickupItem_Networked __result)
    {
        SecondaryRaftLootSpawner.OnVanillaSpawn(__result);
    }
}
