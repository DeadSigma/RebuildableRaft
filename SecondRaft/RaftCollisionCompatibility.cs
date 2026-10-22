using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

public class SecondaryRaftBodyCollisionProxy : MonoBehaviour
{
}

[HarmonyPatch(
    typeof(SecondaryRaftRoot),
    "Initialize"
)]
public static class SecondaryRaftRoot_Initialize_RaftCollision
{
    [HarmonyPostfix]
    public static void Postfix(
        SecondaryRaftRoot __instance)
    {
        if (__instance == null ||
            __instance.GetComponent
                <SecondaryRaftPhysicalCollision>() != null)
        {
            return;
        }

        __instance.gameObject.AddComponent
            <SecondaryRaftPhysicalCollision>();
    }
}

public class SecondaryRaftPhysicalCollision : MonoBehaviour
{
    private const float RefreshInterval = 0.25f;
    private const float FoundationSize = 1.5f;
    private const float FoundationHeight = 0.5f;

    private class HullEntry
    {
        public Block block;
        public BoxCollider collider;
        public Transform transform;
    }

    private SecondaryRaftRoot root;
    private readonly Dictionary<int, HullEntry>
        hulls =
            new Dictionary<int, HullEntry>();

    private float refreshTimer;

    private void Awake()
    {
        root =
            GetComponent<SecondaryRaftRoot>();
    }

    private void Start()
    {
        RefreshHull();
    }

    private void Update()
    {
        refreshTimer -=
            Time.deltaTime;

        if (refreshTimer > 0f)
        {
            return;
        }

        refreshTimer =
            RefreshInterval;

        RefreshHull();
    }

    private void OnDestroy()
    {
        ClearHull();
    }

    private void RefreshHull()
    {
        if (root == null)
        {
            root =
                GetComponent<SecondaryRaftRoot>();
        }

        if (root == null)
        {
            return;
        }

        if (!Raft_Network.IsHost)
        {
            SetHullEnabled(false);
            return;
        }

        Transform pivot =
            root.BuildPivot;

        if (pivot == null)
        {
            return;
        }

        Block[] blocks =
            root.GetComponentsInChildren<Block>(
                true
            );

        HashSet<int> alive =
            new HashSet<int>();

        for (int i = 0;
             i < blocks.Length;
             i++)
        {
            Block block =
                blocks[i];

            if (block == null ||
                !block.hasBeenPlaced ||
                !block.gameObject.activeInHierarchy ||
                !block.IsWalkable())
            {
                continue;
            }

            Vector3 localPosition =
                pivot.InverseTransformPoint(
                    block.transform.position
                );

            // Для столкновения плотов достаточно уровня фундамента
            if (Mathf.Abs(localPosition.y) >
                0.2f)
            {
                continue;
            }

            int id =
                block.GetInstanceID();

            alive.Add(id);

            HullEntry entry;

            if (!hulls.TryGetValue(
                    id,
                    out entry) ||
                entry == null ||
                entry.collider == null)
            {
                entry =
                    CreateHull(block);

                if (entry == null)
                {
                    continue;
                }

                hulls[id] =
                    entry;
            }

            entry.block =
                block;

            entry.transform.localPosition =
                new Vector3(
                    localPosition.x,
                    0f,
                    localPosition.z
                );

            entry.transform.localRotation =
                Quaternion.identity;
        }

        List<int> dead =
            null;

        foreach (KeyValuePair<int, HullEntry> pair
            in hulls)
        {
            if (alive.Contains(pair.Key))
            {
                continue;
            }

            if (dead == null)
            {
                dead =
                    new List<int>();
            }

            dead.Add(pair.Key);
        }

        if (dead != null)
        {
            for (int i = 0;
                 i < dead.Count;
                 i++)
            {
                RemoveHull(dead[i]);
            }
        }

        IgnoreOwnRaftColliders();
        SetHullEnabled(true);
    }

    private HullEntry CreateHull(
        Block block)
    {
        GameObject proxyObject =
            new GameObject(
                "SecondaryRaftBodyCollision"
            );

        int collisionLayer =
            LayerMask.NameToLayer(
                "CollideWithBlock"
            );

        if (collisionLayer < 0)
        {
            collisionLayer =
                block.gameObject.layer;
        }

        proxyObject.layer =
            collisionLayer;

        proxyObject.transform.SetParent(
            transform,
            false
        );

        proxyObject.AddComponent
            <SecondaryRaftBodyCollisionProxy>();

        BoxCollider collider =
            proxyObject.AddComponent<BoxCollider>();

        collider.enabled =
            false;

        collider.center =
            new Vector3(
                0f,
                -FoundationHeight * 0.5f,
                0f
            );

        collider.size =
            new Vector3(
                FoundationSize,
                FoundationHeight,
                FoundationSize
            );

        return new HullEntry
        {
            block = block,
            collider = collider,
            transform = proxyObject.transform
        };
    }

    private void IgnoreOwnRaftColliders()
    {
        Collider[] ownColliders =
            root.GetComponentsInChildren<Collider>(
                true
            );

        Network_Player[] players =
            UnityEngine.Object.FindObjectsOfType
                <Network_Player>();

        foreach (HullEntry entry
            in hulls.Values)
        {
            if (entry == null ||
                entry.collider == null)
            {
                continue;
            }

            for (int i = 0;
                 i < ownColliders.Length;
                 i++)
            {
                Collider other =
                    ownColliders[i];

                if (other == null ||
                    other == entry.collider ||
                    other.GetComponent
                        <SecondaryRaftBodyCollisionProxy>() != null)
                {
                    continue;
                }

                Physics.IgnoreCollision(
                    entry.collider,
                    other,
                    true
                );
            }

            for (int i = 0;
                 i < players.Length;
                 i++)
            {
                Network_Player player =
                    players[i];

                if (player == null)
                {
                    continue;
                }

                Collider[] playerColliders =
                    player.GetComponentsInChildren
                        <Collider>(
                            true
                        );

                for (int j = 0;
                     j < playerColliders.Length;
                     j++)
                {
                    Collider playerCollider =
                        playerColliders[j];

                    if (playerCollider != null)
                    {
                        Physics.IgnoreCollision(
                            entry.collider,
                            playerCollider,
                            true
                        );
                    }
                }
            }
        }
    }

    private void SetHullEnabled(
        bool enabled)
    {
        foreach (HullEntry entry
            in hulls.Values)
        {
            if (entry != null &&
                entry.collider != null)
            {
                entry.collider.enabled =
                    enabled;
            }
        }
    }

    private void RemoveHull(
        int id)
    {
        HullEntry entry;

        if (!hulls.TryGetValue(
                id,
                out entry))
        {
            return;
        }

        hulls.Remove(id);

        if (entry != null &&
            entry.transform != null)
        {
            Destroy(
                entry.transform.gameObject
            );
        }
    }

    private void ClearHull()
    {
        foreach (HullEntry entry
            in hulls.Values)
        {
            if (entry != null &&
                entry.transform != null)
            {
                Destroy(
                    entry.transform.gameObject
                );
            }
        }

        hulls.Clear();
    }
}
