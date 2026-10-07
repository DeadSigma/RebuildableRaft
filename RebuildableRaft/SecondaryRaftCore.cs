using HarmonyLib;
using HMLLibrary;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Unity.Netcode;
using UltimateWater;
using UnityEngine;

public class SecondaryRaftBlockTag : MonoBehaviour
{
    public int raftId;
}

public class SecondaryRaftCollisionProxy : MonoBehaviour
{
    private Collider proxyCollider;

    public void Initialize(
        Collider source,
        Collider proxy)
    {
        proxyCollider = proxy;
        RefreshState();
    }

    public void RefreshState()
    {
        if (proxyCollider == null)
        {
            proxyCollider = GetComponent<Collider>();
        }

        if (proxyCollider == null)
        {
            return;
        }

        // Резервная поверхность второго плота всегда остаётся активной
        proxyCollider.enabled = true;
    }

    private void FixedUpdate()
    {
        RefreshState();
    }

    private void LateUpdate()
    {
        RefreshState();
    }
}

public class SecondaryRaftPlayerPivot : MonoBehaviour
{
    public SecondaryRaftRoot Root
    {
        get;
        private set;
    }

    public void Initialize(
        SecondaryRaftRoot root)
    {
        Root = root;
    }
}

[Serializable]
public class SecondaryRaftSaveData
{
    public int raftId;
    public float x;
    public float y;
    public float z;
    public float yaw;
}

public static class MultiRaftRegistry
{
    private const int EncodedHotSlotMagic = 0x40000000;
    private const int EncodedHotSlotMagicMask = unchecked((int)0xC0000000);
    private const int PayloadMask = 0x3FFFFFFF;
    private const int NewRaftFlag = 0x10;
    private const int SlotMask = 0x0F;
    private const int MaxRaftId = 0x01FFFFFF;

    internal const string SaveKey =
        "RebuildableRaft.SecondaryRaft";

    private static readonly Dictionary<int, SecondaryRaftRoot>
        Roots =
            new Dictionary<int, SecondaryRaftRoot>();

    private static readonly ConditionalWeakTable
        <RGD_Block, SecondaryRaftSaveData>
        RgdData =
            new ConditionalWeakTable
                <RGD_Block, SecondaryRaftSaveData>();

    private static readonly FieldInfo LockedBuildPivotField =
        AccessTools.Field(
            typeof(BlockCreator),
            "lockedBuildPivot"
        );

    private static PropertyInfo oceanDirectionProperty;
    private static bool oceanDirectionLookupDone;
    private static int nextRaftId = 1;

    public static Transform MainPivot
    {
        get
        {
            GameManager gameManager =
                SingletonGeneric<GameManager>.Singleton;

            return gameManager != null
                ? gameManager.lockedPivot
                : null;
        }
    }

    public static Transform CurrentBuildPivot
    {
        get
        {
            return LockedBuildPivotField != null
                ? LockedBuildPivotField.GetValue(null)
                    as Transform
                : null;
        }
    }

    public static void SetBuildPivot(
        Transform pivot)
    {
        if (LockedBuildPivotField != null &&
            pivot != null)
        {
            LockedBuildPivotField.SetValue(
                null,
                pivot
            );
        }
    }

    public static int AllocateRaftId()
    {
        while (Roots.ContainsKey(nextRaftId))
        {
            nextRaftId++;
        }

        if (nextRaftId > MaxRaftId)
        {
            throw new InvalidOperationException(
                "Secondary raft id limit reached"
            );
        }

        return nextRaftId++;
    }

    public static SecondaryRaftRoot CreateRoot(
        int raftId,
        Vector3 position,
        float yaw)
    {
        if (raftId <= 0)
        {
            return null;
        }

        SecondaryRaftRoot existing =
            GetRoot(raftId);

        if (existing != null)
        {
            return existing;
        }

        GameObject rootObject =
            new GameObject(
                "SecondaryRaft_" + raftId
            );

        rootObject.transform.SetPositionAndRotation(
            position,
            Quaternion.Euler(
                0f,
                yaw,
                0f
            )
        );

        Rigidbody body =
            rootObject.AddComponent<Rigidbody>();

        body.useGravity = false;
        body.drag = 1.5f;
        body.angularDrag = 3f;
        body.interpolation =
            RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode =
            CollisionDetectionMode.ContinuousDynamic;
        body.constraints =
            RigidbodyConstraints.FreezeRotationX |
            RigidbodyConstraints.FreezeRotationZ;

        SecondaryRaftRoot root =
            rootObject.AddComponent
                <SecondaryRaftRoot>();

        root.Initialize(raftId);

        return root;
    }

    public static void Register(
        SecondaryRaftRoot root)
    {
        if (root == null ||
            root.RaftId <= 0)
        {
            return;
        }

        Roots[root.RaftId] =
            root;

        if (root.RaftId >= nextRaftId)
        {
            nextRaftId =
                root.RaftId + 1;
        }
    }

    public static void Unregister(
        SecondaryRaftRoot root)
    {
        if (root == null)
        {
            return;
        }

        SecondaryRaftRoot current;
        if (Roots.TryGetValue(
                root.RaftId,
                out current) &&
            current == root)
        {
            Roots.Remove(root.RaftId);
        }
    }

    public static SecondaryRaftRoot GetRoot(
        int raftId)
    {
        SecondaryRaftRoot root;

        if (raftId > 0 &&
            Roots.TryGetValue(
                raftId,
                out root) &&
            root != null)
        {
            return root;
        }

        return null;
    }

    public static IEnumerable
        <SecondaryRaftRoot> GetRoots()
    {
        return Roots.Values;
    }

    public static SecondaryRaftRoot
        GetRootFromTransform(
            Transform transform)
    {
        if (transform == null)
        {
            return null;
        }

        SecondaryRaftRoot root =
            transform.GetComponentInParent
                <SecondaryRaftRoot>();

        if (root != null)
        {
            return root;
        }

        SecondaryRaftPlayerPivot playerPivot =
            transform.GetComponentInParent
                <SecondaryRaftPlayerPivot>();

        return playerPivot != null
            ? playerPivot.Root
            : null;
    }

    public static SecondaryRaftRoot
        GetRootFromBlock(
            Block block)
    {
        return block != null
            ? GetRootFromTransform(
                block.transform
            )
            : null;
    }

    public static bool IsSecondaryBlock(
        Block block)
    {
        return GetRootFromBlock(block) != null;
    }

    public static void TagBlock(
        Block block,
        SecondaryRaftRoot root)
    {
        if (block == null || root == null)
        {
            return;
        }

        SecondaryRaftBlockTag tag =
            block.GetComponent
                <SecondaryRaftBlockTag>();

        if (tag == null)
        {
            tag =
                block.gameObject.AddComponent
                    <SecondaryRaftBlockTag>();
        }

        tag.raftId =
            root.RaftId;
    }

    public static bool HasPrimaryBlocks()
    {
        Transform mainPivot =
            MainPivot;

        if (mainPivot == null)
        {
            return false;
        }

        List<Block> blocks =
            BlockCreator.GetPlacedBlocks();

        if (blocks == null)
        {
            return false;
        }

        for (int i = 0; i < blocks.Count; i++)
        {
            Block block =
                blocks[i];

            if (block == null ||
                !block.hasBeenPlaced ||
                !block.gameObject.activeSelf)
            {
                continue;
            }

            if (block.transform == mainPivot ||
                block.transform.IsChildOf(
                    mainPivot
                ))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryGetBuildPivotAtCursor(
        Network_Player player,
        Item_Base item,
        out Transform pivot)
    {
        pivot = MainPivot;

        if (player == null ||
            player.CameraTransform == null ||
            item == null)
        {
            return false;
        }

        RaycastHit[] hits =
            Physics.RaycastAll(
                player.CameraTransform.position,
                player.CameraTransform.forward,
                Player.UseDistance * 2f,
                LayerMasks.MASK_BuildQuad
            );

        float bestDistance =
            float.MaxValue;

        BlockQuad bestQuad =
            null;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit =
                hits[i];

            if (hit.collider == null)
            {
                continue;
            }

            BlockQuad quad =
                hit.collider.transform
                    .GetComponent<BlockQuad>();

            if (quad == null)
            {
                continue;
            }

            Block quadBlock =
                quad.ParentBlock;

            // Зацепление за BuildQuad предпросмотра исключается
            if (quadBlock == null ||
                !quadBlock.hasBeenPlaced ||
                !quadBlock.gameObject.activeInHierarchy ||
                !quad.AcceptsBlock(
                    item,
                    hit.normal
                ))
            {
                continue;
            }

            if (hit.distance < bestDistance)
            {
                bestDistance =
                    hit.distance;

                bestQuad =
                    quad;
            }
        }

        if (bestQuad == null)
        {
            return false;
        }

        Block parentBlock =
            bestQuad.ParentBlock;

        SecondaryRaftRoot root =
            GetRootFromBlock(
                parentBlock
            );

        pivot =
            root != null
                ? root.BuildPivot
                : MainPivot;

        return true;
    }

    public static bool TryGetBlockPivotAtCursor(
        Network_Player player,
        out Transform pivot)
    {
        pivot = MainPivot;

        if (player == null ||
            player.CameraTransform == null)
        {
            return false;
        }

        RaycastHit hit;

        if (!Physics.Raycast(
                player.CameraTransform.position,
                player.CameraTransform.forward,
                out hit,
                Player.UseDistance * 2f,
                LayerMasks.MASK_Block,
                QueryTriggerInteraction.UseGlobal
            ))
        {
            return false;
        }

        Block block =
            hit.transform.GetComponentInParent
                <Block>();

        SecondaryRaftRoot root =
            GetRootFromBlock(block);

        if (root == null)
        {
            return block != null;
        }

        pivot =
            root.BuildPivot;

        return true;
    }

    public static SecondaryRaftRoot FindRootAtGroundPoint(
        Vector3 worldPoint)
    {
        Vector3 origin =
            worldPoint +
            Vector3.up * 0.55f;

        RaycastHit[] hits =
            Physics.SphereCastAll(
                origin,
                0.15f,
                Vector3.down,
                2f,
                LayerMasks.MASK_GroundMask_Raft,
                QueryTriggerInteraction.Ignore
            );

        if (hits == null ||
            hits.Length == 0)
        {
            return FindRootByGeometry(
                worldPoint
            );
        }

        Array.Sort(
            hits,
            delegate (
                RaycastHit a,
                RaycastHit b)
            {
                return a.distance
                    .CompareTo(b.distance);
            }
        );

        for (int i = 0;
             i < hits.Length;
             i++)
        {
            RaycastHit hit =
                hits[i];

            if (hit.collider == null ||
                hit.collider.isTrigger ||
                hit.normal.y < 0.1f)
            {
                continue;
            }

            SecondaryRaftRoot root =
                GetRootFromTransform(
                    hit.collider.transform
                );

            Block block =
                null;

            if (root == null)
            {
                block =
                    hit.collider
                        .GetComponentInParent
                            <Block>();

                root =
                    GetRootFromBlock(
                        block
                    );
            }

            if (root != null)
            {
                return root;
            }

            Transform mainPivot =
                MainPivot;

            if (mainPivot != null &&
                (hit.collider.transform ==
                    mainPivot ||
                 hit.collider.transform.IsChildOf(
                    mainPivot
                 )))
            {
                return null;
            }

            if (block != null)
            {
                return null;
            }

            RaftCollisionManager mainRaft =
                ComponentManager
                    <RaftCollisionManager>.Value;

            if (mainRaft != null &&
                (hit.collider.transform ==
                    mainRaft.transform ||
                 hit.collider.transform.IsChildOf(
                    mainRaft.transform
                 )))
            {
                return null;
            }
        }

        return FindRootByGeometry(
            worldPoint
        );
    }

    private static SecondaryRaftRoot FindRootByGeometry(
        Vector3 worldPoint)
    {
        SecondaryRaftRoot bestRoot =
            null;

        float bestScore =
            float.MaxValue;

        foreach (SecondaryRaftRoot root
            in Roots.Values)
        {
            if (root == null)
            {
                continue;
            }

            float score =
                root.GetGroundPointScore(
                    worldPoint
                );

            if (score < bestScore)
            {
                bestScore = score;
                bestRoot = root;
            }
        }

        return bestRoot;
    }

    public static void CarryLocalPlayerBeforeUpdate(
        PersonController personController)
    {
        if (personController == null)
        {
            return;
        }

        foreach (SecondaryRaftRoot root
            in Roots.Values)
        {
            if (root != null)
            {
                root.CarryLocalPlayerBeforeUpdate(
                    personController
                );
            }
        }
    }

    public static int EncodeHotSlot(
        int raftId,
        int hotSlotIndex,
        bool newRaft)
    {
        if (raftId < 0)
        {
            raftId = 0;
        }

        if (raftId > MaxRaftId)
        {
            raftId = MaxRaftId;
        }

        int slotCode =
            Mathf.Clamp(
                hotSlotIndex + 1,
                0,
                SlotMask
            );

        int payload =
            (raftId << 5) |
            slotCode;

        if (newRaft)
        {
            payload |=
                NewRaftFlag;
        }

        return EncodedHotSlotMagic |
            (payload & PayloadMask);
    }

    public static bool TryDecodeHotSlot(
        int encoded,
        out int raftId,
        out int hotSlotIndex,
        out bool newRaft)
    {
        raftId = 0;
        hotSlotIndex = encoded;
        newRaft = false;

        if ((encoded &
             EncodedHotSlotMagicMask) !=
            EncodedHotSlotMagic)
        {
            return false;
        }

        int payload =
            encoded & PayloadMask;

        int slotCode =
            payload & SlotMask;

        newRaft =
            (payload &
             NewRaftFlag) != 0;

        raftId =
            payload >> 5;

        hotSlotIndex =
            slotCode - 1;

        return true;
    }

    public static void SetRgdData(
        RGD_Block rgd,
        SecondaryRaftSaveData data)
    {
        if (rgd == null ||
            data == null)
        {
            return;
        }

        RgdData.Remove(rgd);

        try
        {
            RgdData.Add(
                rgd,
                data
            );
        }
        catch
        {
        }
    }

    public static bool TryGetRgdData(
        RGD_Block rgd,
        out SecondaryRaftSaveData data)
    {
        data = null;

        return rgd != null &&
            RgdData.TryGetValue(
                rgd,
                out data
            ) &&
            data != null &&
            data.raftId > 0;
    }

    public static SecondaryRaftSaveData
        CreateSaveData(
            SecondaryRaftRoot root)
    {
        if (root == null)
        {
            return null;
        }

        Vector3 position =
            root.transform.position;

        return new SecondaryRaftSaveData
        {
            raftId =
                root.RaftId,

            x =
                position.x,

            y =
                position.y,

            z =
                position.z,

            yaw =
                root.transform.eulerAngles.y
        };
    }

    public static Vector3 GetOceanDirection()
    {
        if (!oceanDirectionLookupDone)
        {
            oceanDirectionLookupDone =
                true;

            Type dynamicOceanType =
                AccessTools.TypeByName(
                    "DynamicOcean"
                );

            if (dynamicOceanType != null)
            {
                oceanDirectionProperty =
                    dynamicOceanType.GetProperty(
                        "OceanDirection",
                        BindingFlags.Static |
                        BindingFlags.Public |
                        BindingFlags.NonPublic
                    );
            }
        }

        if (oceanDirectionProperty != null)
        {
            try
            {
                object value =
                    oceanDirectionProperty.GetValue(
                        null,
                        null
                    );

                if (value is Vector3)
                {
                    Vector3 direction =
                        (Vector3)value;

                    direction.y = 0f;

                    if (direction.sqrMagnitude >
                        0.0001f)
                    {
                        return direction.normalized;
                    }
                }
            }
            catch
            {
            }
        }

        return Vector3.forward;
    }

    public static void RebuildFromScene()
    {
        Roots.Clear();
        nextRaftId = 1;

        SecondaryRaftRoot[] roots =
            UnityEngine.Object.FindObjectsOfType
                <SecondaryRaftRoot>();

        for (int i = 0;
             i < roots.Length;
             i++)
        {
            Register(roots[i]);
        }
    }

    public static void Reset()
    {
        Roots.Clear();
        nextRaftId = 1;
        oceanDirectionLookupDone = false;
        oceanDirectionProperty = null;
        SecondaryRaftWaterSampler.Reset();
    }
}
