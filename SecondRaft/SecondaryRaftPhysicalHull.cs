using System;
using System.Collections.Generic;
using UnityEngine;

// Физический корпус плота закрепляется за движущимся Rigidbody
[DefaultExecutionOrder(-100)]
public sealed class SecondaryRaftPhysicalHull : MonoBehaviour
{
    private const float RefreshInterval = 0.25f;
    private const float BlockSize = 1.5f;
    private const float FoundationHeight = 0.5f;
    private const float FloorHeight = 0.1f;

    private SecondaryRaftRoot root;
    private Rigidbody body;
    private Transform holder;
    private float nextRefreshTime;
    private int hullLayer = -1;
    private int lastReportedCount = -1;

    private readonly Dictionary<Block, BoxCollider> hulls =
        new Dictionary<Block, BoxCollider>();
    private readonly HashSet<Collider> ignoredInternal =
        new HashSet<Collider>();
    private readonly List<BoxCollider> newHulls =
        new List<BoxCollider>();
    private readonly List<Block> removed =
        new List<Block>();

    private void Awake()
    {
        root = GetComponent<SecondaryRaftRoot>();
        body = GetComponent<Rigidbody>();
        EnsureHolder();
    }

    private void Update()
    {
        if (root == null || body == null || Raft_Network.InMenuScene ||
            SaveAndLoad.IsGameLoading)
            return;

        if (Time.realtimeSinceStartup < nextRefreshTime)
            return;

        nextRefreshTime = Time.realtimeSinceStartup + RefreshInterval;
        Refresh();
    }

    public void Refresh()
    {
        if (root == null || body == null || Raft_Network.InMenuScene ||
            SaveAndLoad.IsGameLoading)
            return;

        EnsureHolder();
        if (holder == null)
            return;

        int layer = ResolveVanillaHullLayer();
        if (hullLayer != layer)
        {
            hullLayer = layer;
            holder.gameObject.layer = layer;
            foreach (BoxCollider box in hulls.Values)
                if (box != null)
                    box.gameObject.layer = layer;
        }

        newHulls.Clear();
        HashSet<Block> present = new HashSet<Block>();
        Transform wavePivot = root.BuildPivot;
        Block[] blocks = root.GetComponentsInChildren<Block>(true);

        for (int i = 0; i < blocks.Length; i++)
        {
            Block block = blocks[i];
            if (block == null || !block.hasBeenPlaced ||
                !block.gameObject.activeInHierarchy || !block.IsWalkable() ||
                block.buildableItem == null ||
                MultiRaftRegistry.GetRootFromBlock(block) != root)
                continue;

            present.Add(block);
            BoxCollider box;
            if (!hulls.TryGetValue(block, out box) || box == null)
            {
                GameObject cell = new GameObject("SecondRaftHullCell");
                cell.layer = layer;
                cell.transform.SetParent(holder, false);
                box = cell.AddComponent<BoxCollider>();
                box.isTrigger = false;
                hulls[block] = box;
                newHulls.Add(box);
            }

            // Локальные координаты берутся ДО наклона волновой платформы
            Vector3 cellPosition = wavePivot != null
                ? wavePivot.InverseTransformPoint(block.transform.position)
                : transform.InverseTransformPoint(block.transform.position);
            box.transform.localPosition = cellPosition;
            box.transform.localRotation = Quaternion.identity;

            bool foundation = Block.IsBlockIndexFoundation(
                block.buildableItem.UniqueIndex);
            float height = foundation ? FoundationHeight : FloorHeight;
            box.center = new Vector3(0f, -height * 0.5f, 0f);
            box.size = new Vector3(BlockSize, height, BlockSize);
            box.enabled = Raft_Network.IsHost && CollisionEnabled();
        }

        removed.Clear();
        foreach (KeyValuePair<Block, BoxCollider> pair in hulls)
            if (pair.Key == null || !present.Contains(pair.Key))
                removed.Add(pair.Key);

        for (int i = 0; i < removed.Count; i++)
        {
            Block block = removed[i];
            BoxCollider old;
            if (!hulls.TryGetValue(block, out old))
                continue;
            if (old != null)
            {
                old.enabled = false;
                Destroy(old.gameObject);
            }
            hulls.Remove(block);
        }

        if (lastReportedCount != hulls.Count)
        {
            lastReportedCount = hulls.Count;
            Debug.Log("[SecondRaft] Physical hull raft=" + root.RaftId +
                " colliders=" + hulls.Count + " layer=" + hullLayer);
        }

        // У обоих коллайдеров плота разные Rigidbody - их контакт исключается
        // Контакт с островом и с другим плотом при этом остаётся физическим
        if (Raft_Network.IsHost)
            IgnoreOwnWaveColliders(wavePivot);
    }

    private void IgnoreOwnWaveColliders(Transform wavePivot)
    {
        if (wavePivot == null)
            return;

        Collider[] own = wavePivot.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < own.Length; i++)
        {
            Collider source = own[i];
            if (source == null || !source.enabled ||
                !source.gameObject.activeInHierarchy || source.isTrigger)
                continue;

            if (ignoredInternal.Add(source))
            {
                foreach (BoxCollider hull in hulls.Values)
                    if (hull != null && hull.enabled)
                        Physics.IgnoreCollision(hull, source, true);
            }
        }

        // Новые ячейки исключаются из столкновений с ранее найденными частями плота
        for (int i = 0; i < newHulls.Count; i++)
        {
            BoxCollider hull = newHulls[i];
            if (hull == null || !hull.enabled)
                continue;
            foreach (Collider source in ignoredInternal)
                if (source != null && source.enabled && source.gameObject.activeInHierarchy)
                    Physics.IgnoreCollision(hull, source, true);
        }

        ignoredInternal.RemoveWhere(c => c == null || !c.enabled ||
            !c.gameObject.activeInHierarchy);
    }

    private void EnsureHolder()
    {
        if (holder != null)
            return;

        holder = transform.Find("SecondRaftPhysicalHull");
        if (holder != null)
            return;

        GameObject go = new GameObject("SecondRaftPhysicalHull");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        holder = go.transform;
    }

    private static bool CollisionEnabled()
    {
        return GameModeValueManager.GetCurrentGameModeValue()
            .raftSpecificVariables.handleRaftCollision;
    }

    private static int ResolveVanillaHullLayer()
    {
        RaftCollisionManager manager =
            ComponentManager<RaftCollisionManager>.Value;
        if (manager != null)
        {
            BoxCollider[] boxes = manager.GetComponentsInChildren<BoxCollider>(true);
            for (int i = 0; i < boxes.Length; i++)
            {
                BoxCollider box = boxes[i];
                if (box != null && box.enabled && box.gameObject.activeInHierarchy)
                    return box.gameObject.layer;
            }
        }
        // Default взаимодействует с геометрией островов даже без ванильного пула
        return 0;
    }
}
