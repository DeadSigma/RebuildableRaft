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

[HarmonyPatch(typeof(BlockCreator), "Update")]
public static class BlockCreator_Update_RebuildableRaft
{
    private static readonly FieldInfo GhostGreenField =
        AccessTools.Field(
            typeof(GameManager),
            "ghostMaterialGreen"
        );

    private static readonly FieldInfo GhostRedField =
        AccessTools.Field(
            typeof(GameManager),
            "ghostMaterialRed"
        );

    private static readonly FieldInfo RaftBodyField =
        AccessTools.Field(
            typeof(Raft),
            "body"
        );

    private const float PreviewDistance = 2.25f;
    private const float PreviewFollowSpeed = 14f;
    private const float FoundationSurfaceOffset = 0.4f;

    private static bool previewPositionInitialized;
    private static Vector3 previewWorldPosition;
    private static Block previewGhost;

    [HarmonyPrefix]
    public static bool Prefix(
        BlockCreator __instance,
        Item_Base ___selectedBuildableItem,
        Block ___selectedBuildablePrefab,
        Network_Player ___playerNetwork,
        out Transform __state)
    {
        __state =
            MultiRaftRegistry.CurrentBuildPivot;

        if (___playerNetwork == null ||
            !___playerNetwork.IsLocalPlayer)
        {
            return true;
        }

        Physics.SyncTransforms();

        Transform mainPivot =
            MultiRaftRegistry.MainPivot;

        if (mainPivot == null)
        {
            return true;
        }

        Item_Base item =
            ___selectedBuildableItem;

        Transform targetPivot =
            mainPivot;

        bool hasBuildQuad =
            item != null &&
            MultiRaftRegistry
                .TryGetBuildPivotAtCursor(
                    ___playerNetwork,
                    item,
                    out targetPivot
                );

        if (!hasBuildQuad &&
            __instance.selectedBlock == null)
        {
            MultiRaftRegistry
                .TryGetBlockPivotAtCursor(
                    ___playerNetwork,
                    out targetPivot
                );
        }

        if (targetPivot == null)
        {
            targetPivot =
                mainPivot;
        }

        MultiRaftRegistry.SetBuildPivot(
            targetPivot
        );

        Block selectedGhost =
            __instance.selectedBlock;

        if (selectedGhost != null &&
            selectedGhost.transform.parent !=
                targetPivot)
        {
            selectedGhost.transform.SetParent(
                targetPivot,
                true
            );
        }

        if (hasBuildQuad)
        {
            ResetPreviewState();
            return true;
        }

        if (item == null ||
            !Block.IsBlockIndexFoundation(
                item.UniqueIndex
            ))
        {
            ResetPreviewState();
            return true;
        }

        if (CanvasHelper.ActiveMenu !=
                MenuType.None ||
            MyInput.GetButtonDown("RMB") ||
            MyInput.GetButtonUp("RMB"))
        {
            return true;
        }

        Block ghost =
            __instance.selectedBlock;

        if (ghost == null ||
            ___selectedBuildablePrefab == null)
        {
            return true;
        }

        if (ghost.buildableItem
                .settings_buildable
                .MirroredVersion != null &&
            Input.GetKeyDown(KeyCode.Z))
        {
            __instance.SetBlockTypeToBuild(
                ghost.buildableItem
                    .settings_buildable
                    .MirroredVersion
            );

            return false;
        }

        HandleRotation(
            ghost,
            ___selectedBuildablePrefab
        );

        Vector3 worldPosition;

        if (!TryGetBuildPosition(
                ___playerNetwork,
                ghost,
                out worldPosition
            ))
        {
            __instance.SetGhostBlockVisibility(
                false
            );

            ResetPreviewState();
            return false;
        }

        ghost.transform.position =
            worldPosition;

        float gridYaw =
            mainPivot.eulerAngles.y +
            ___selectedBuildablePrefab
                .currentRotationY;

        ghost.transform.rotation =
            Quaternion.Euler(
                0f,
                gridYaw,
                0f
            );

        __instance.SetGhostBlockVisibility(
            true
        );

        bool canBuild =
            ghost.IsOverlapping() ==
                OverlappType.None &&
            __instance.HasEnoughResourcesToBuild(
                ghost
            );

        SetGhostMaterial(
            ghost,
            canBuild
        );

        if (canBuild &&
            MyInput.GetButtonDown("LMB"))
        {
            PlaceFoundation(
                __instance,
                ___playerNetwork,
                item,
                ghost,
                ___selectedBuildablePrefab
                    .currentRotationY
            );
        }

        return false;
    }

    [HarmonyPostfix]
    public static void Postfix(
        Transform __state)
    {
        Transform mainPivot =
            MultiRaftRegistry.MainPivot;

        MultiRaftRegistry.SetBuildPivot(
            mainPivot != null
                ? mainPivot
                : __state
        );
    }

    private static bool TryGetBuildPosition(
        Network_Player player,
        Block ghost,
        out Vector3 worldPosition)
    {
        worldPosition =
            Vector3.zero;

        if (player.CameraTransform == null)
        {
            return false;
        }

        Vector3 forward =
            player.CameraTransform.forward;

        forward.y = 0f;

        if (forward.sqrMagnitude <
            0.001f)
        {
            forward =
                player.transform.forward;

            forward.y = 0f;
        }

        if (forward.sqrMagnitude <
            0.001f)
        {
            return false;
        }

        forward.Normalize();

        Vector3 targetPosition =
            player.transform.position +
            forward *
            PreviewDistance;

        float waterY =
            GetFoundationWorldY(
                targetPosition
            );

        targetPosition.y =
            waterY;

        bool ghostChanged =
            previewGhost != ghost;

        bool positionTooFar =
            previewPositionInitialized &&
            Vector3.Distance(
                previewWorldPosition,
                targetPosition
            ) > 5f;

        if (!previewPositionInitialized ||
            ghostChanged ||
            positionTooFar)
        {
            previewWorldPosition =
                targetPosition;

            previewPositionInitialized =
                true;

            previewGhost =
                ghost;
        }
        else
        {
            float follow =
                1f -
                Mathf.Exp(
                    -PreviewFollowSpeed *
                    Time.deltaTime
                );

            previewWorldPosition =
                Vector3.Lerp(
                    previewWorldPosition,
                    targetPosition,
                    follow
                );

        }

        worldPosition =
            previewWorldPosition;

        return true;
    }

    private static void ResetPreviewState()
    {
        previewPositionInitialized =
            false;

        previewGhost =
            null;

        previewWorldPosition =
            Vector3.zero;
    }

    internal static float GetFoundationWorldY(
        Vector3 worldPosition)
    {
        float surfaceY;

        if (SecondaryRaftWaterSampler
                .TryGetSurfaceY(
                    worldPosition,
                    FoundationSurfaceOffset,
                    out surfaceY
                ))
        {
            return surfaceY;
        }

        GameManager gameManager =
            SingletonGeneric<GameManager>.Singleton;

        if (gameManager != null &&
            gameManager.buoyancy != null)
        {
            return gameManager
                .buoyancy.CenterPointY;
        }

        if (gameManager != null &&
            gameManager.water != null)
        {
            return gameManager.water
                .transform.position.y +
                FoundationSurfaceOffset;
        }

        return FoundationSurfaceOffset;
    }

    private static void HandleRotation(
        Block ghost,
        Block selectedBuildablePrefab)
    {
        if (!ghost.isRotateable ||
            !MyInput.GetButtonDown("Rotate"))
        {
            return;
        }

        float rotation =
            selectedBuildablePrefab
                .currentRotationY;

        rotation +=
            ghost.rotateAmount;

        while (rotation >= 360f)
        {
            rotation -= 360f;
        }

        while (rotation < 0f)
        {
            rotation += 360f;
        }

        selectedBuildablePrefab
            .currentRotationY =
                rotation;
    }

    private static void SetGhostMaterial(
        Block ghost,
        bool canBuild)
    {
        if (ghost == null ||
            ghost.occupyingComponent == null)
        {
            return;
        }

        GameManager gameManager =
            SingletonGeneric<GameManager>.Singleton;

        if (gameManager == null)
        {
            return;
        }

        FieldInfo materialField =
            canBuild
                ? GhostGreenField
                : GhostRedField;

        if (materialField == null)
        {
            return;
        }

        Material material =
            materialField.GetValue(
                gameManager
            ) as Material;

        if (material != null)
        {
            ghost.occupyingComponent
                .SetNewMaterial(
                    material
                );
        }
    }

    private static void PlaceFoundation(
        BlockCreator creator,
        Network_Player player,
        Item_Base item,
        Block ghost,
        float gridRotationY)
    {
        if (!MultiRaftRegistry
                .HasPrimaryBlocks())
        {
            PlacePrimaryFoundation(
                creator,
                player,
                item,
                ghost,
                gridRotationY
            );

            return;
        }

        PlaceSecondaryFoundation(
            creator,
            player,
            item,
            ghost,
            gridRotationY
        );
    }

    private static void PlacePrimaryFoundation(
        BlockCreator creator,
        Network_Player player,
        Item_Base item,
        Block ghost,
        float gridRotationY)
    {
        int hotSlotIndex =
            player.Inventory.hotbar
                .GetSelectedSlotIndex();

        Transform pivot =
            MultiRaftRegistry.MainPivot;

        if (pivot == null)
        {
            return;
        }

        Vector3 worldPosition =
            ghost.transform.position;

        PreparePrimaryRaft(
            pivot,
            worldPosition
        );

        Vector3 localEuler =
            NormalizeLocalEuler(
                ghost,
                gridRotationY
            );

        if (Raft_Network.IsHost)
        {
            uint blockObjectIndex =
                SaveAndLoad
                    .GetUniqueObjectIndex();

            uint networkedObjectIndex =
                ghost.networkedBehaviour != null
                    ? SaveAndLoad
                        .GetUniqueObjectIndex()
                    : 0U;

            uint networkedBehaviourIndex =
                ghost.networkedBehaviour != null
                    ? NetworkUpdateManager
                        .GetUniqueBehaviourIndex()
                    : 0U;

            Message_BlockCreator_PlaceBlock
                message =
                    new Message_BlockCreator_PlaceBlock(
                        Messages.BlockCreator_PlaceBlock,
                        creator,
                        item.UniqueIndex,
                        blockObjectIndex,
                        networkedObjectIndex,
                        networkedBehaviourIndex,
                        Vector3.zero,
                        localEuler,
                        -1,
                        DPS.Default
                    );

            player.Network.RPC(
                message,
                Target.Other,
                EP2PSend
                    .k_EP2PSendReliable,
                NetworkChannel.Channel_Game
            );

            creator.CreateBlock(
                item,
                Vector3.zero,
                localEuler,
                DPS.Default,
                hotSlotIndex,
                false,
                blockObjectIndex,
                networkedObjectIndex,
                networkedBehaviourIndex
            );

            return;
        }

        Message_BlockCreator_PlaceBlock
            clientMessage =
                new Message_BlockCreator_PlaceBlock(
                    Messages.BlockCreator_PlaceBlock,
                    creator,
                    item.UniqueIndex,
                    0U,
                    0U,
                    0U,
                    Vector3.zero,
                    localEuler,
                    hotSlotIndex,
                    DPS.Default
                );

        player.SendP2P(
            clientMessage,
            EP2PSend.k_EP2PSendReliable,
            NetworkChannel.Channel_Game
        );
    }

    private static void PlaceSecondaryFoundation(
        BlockCreator creator,
        Network_Player player,
        Item_Base item,
        Block ghost,
        float gridRotationY)
    {
        int hotSlotIndex =
            player.Inventory.hotbar
                .GetSelectedSlotIndex();

        Vector3 worldPosition =
            ghost.transform.position;

        Vector3 localEuler =
            NormalizeLocalEuler(
                ghost,
                gridRotationY
            );

        if (Raft_Network.IsHost)
        {
            int raftId =
                MultiRaftRegistry
                    .AllocateRaftId();

            SecondaryRaftRoot root =
                MultiRaftRegistry.CreateRoot(
                    raftId,
                    worldPosition,
                    0f
                );

            if (root == null)
            {
                return;
            }

            Transform previousPivot =
                MultiRaftRegistry
                    .CurrentBuildPivot;

            MultiRaftRegistry.SetBuildPivot(
                root.BuildPivot
            );

            uint blockObjectIndex =
                SaveAndLoad
                    .GetUniqueObjectIndex();

            uint networkedObjectIndex =
                ghost.networkedBehaviour != null
                    ? SaveAndLoad
                        .GetUniqueObjectIndex()
                    : 0U;

            uint networkedBehaviourIndex =
                ghost.networkedBehaviour != null
                    ? NetworkUpdateManager
                        .GetUniqueBehaviourIndex()
                    : 0U;

            Message_BlockCreator_PlaceBlock
                message =
                    new Message_BlockCreator_PlaceBlock(
                        Messages.BlockCreator_PlaceBlock,
                        creator,
                        item.UniqueIndex,
                        blockObjectIndex,
                        networkedObjectIndex,
                        networkedBehaviourIndex,
                        worldPosition,
                        localEuler,
                        -1,
                        DPS.Default
                    );

            message.hotSlotIndex =
                MultiRaftRegistry.EncodeHotSlot(
                    raftId,
                    -1,
                    true
                );

            player.Network.RPC(
                message,
                Target.Other,
                EP2PSend
                    .k_EP2PSendReliable,
                NetworkChannel.Channel_Game
            );

            creator.CreateBlock(
                item,
                Vector3.zero,
                localEuler,
                DPS.Default,
                hotSlotIndex,
                false,
                blockObjectIndex,
                networkedObjectIndex,
                networkedBehaviourIndex
            );

            MultiRaftRegistry.SetBuildPivot(
                previousPivot != null
                    ? previousPivot
                    : MultiRaftRegistry.MainPivot
            );

            return;
        }

        Message_BlockCreator_PlaceBlock
            clientMessage =
                new Message_BlockCreator_PlaceBlock(
                    Messages.BlockCreator_PlaceBlock,
                    creator,
                    item.UniqueIndex,
                    0U,
                    0U,
                    0U,
                    worldPosition,
                    localEuler,
                    hotSlotIndex,
                    DPS.Default
                );

        clientMessage.hotSlotIndex =
            MultiRaftRegistry.EncodeHotSlot(
                0,
                hotSlotIndex,
                true
            );

        player.SendP2P(
            clientMessage,
            EP2PSend.k_EP2PSendReliable,
            NetworkChannel.Channel_Game
        );
    }

    private static Vector3 NormalizeLocalEuler(
        Block ghost,
        float gridRotationY)
    {
        float snap =
            ghost.snapRotateAmount;

        if (snap > 0.001f)
        {
            gridRotationY =
                Mathf.Round(
                    gridRotationY /
                    snap
                ) *
                snap;
        }

        while (gridRotationY >= 360f)
        {
            gridRotationY -= 360f;
        }

        while (gridRotationY < 0f)
        {
            gridRotationY += 360f;
        }

        return new Vector3(
            0f,
            gridRotationY,
            0f
        );
    }

    private static void PreparePrimaryRaft(
        Transform pivot,
        Vector3 targetWorldPosition)
    {
        GameManager gameManager =
            SingletonGeneric<GameManager>.Singleton;

        Raft raft =
            ComponentManager<Raft>.Value;

        Rigidbody body =
            raft != null &&
            RaftBodyField != null
                ? RaftBodyField.GetValue(
                    raft
                ) as Rigidbody
                : null;

        if (gameManager != null &&
            gameManager.buoyancyPivot != null)
        {
            Vector3 euler =
                gameManager.buoyancyPivot
                    .eulerAngles;

            gameManager.buoyancyPivot
                .rotation =
                    Quaternion.Euler(
                        0f,
                        euler.y,
                        0f
                    );
        }

        if (body != null)
        {
            Quaternion rotation =
                body.rotation;

            body.rotation =
                Quaternion.Euler(
                    0f,
                    rotation.eulerAngles.y,
                    0f
                );

            Vector3 velocity =
                body.velocity;

            velocity.y = 0f;

            body.velocity =
                velocity;

            Vector3 angularVelocity =
                body.angularVelocity;

            angularVelocity.x = 0f;
            angularVelocity.z = 0f;

            body.angularVelocity =
                angularVelocity;
        }

        Vector3 delta =
            targetWorldPosition -
            pivot.position;

        if (body != null)
        {
            body.position +=
                delta;
        }

        if (raft != null)
        {
            raft.transform.position +=
                delta;
        }

        if (gameManager != null)
        {
            if (gameManager.buoyancy != null)
            {
                gameManager.buoyancy
                    .transform.position +=
                        delta;
            }

            if (gameManager.raftFollowParent !=
                null)
            {
                gameManager.raftFollowParent
                    .position +=
                        delta;
            }
        }

        Vector3 correction =
            targetWorldPosition -
            pivot.position;

        if (correction.sqrMagnitude >
            0.000001f)
        {
            pivot.position +=
                correction;
        }

        Debug.Log(
            "[RebuildableRaft] Primary raft rebuilt"
        );
    }
}

[HarmonyPatch]
public static class Message_BlockCreator_PlaceBlock_Constructor_RebuildableRaft
{
    private static IEnumerable<MethodBase>
        TargetMethods()
    {
        ConstructorInfo[] constructors =
            typeof(Message_BlockCreator_PlaceBlock)
                .GetConstructors(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic
                );

        for (int i = 0;
             i < constructors.Length;
             i++)
        {
            if (constructors[i]
                    .GetParameters()
                    .Length > 0)
            {
                yield return constructors[i];
            }
        }
    }

    [HarmonyPostfix]
    public static void Postfix(
        Message_BlockCreator_PlaceBlock __instance)
    {
        if (__instance == null)
        {
            return;
        }

        int raftId;
        int hotSlot;
        bool newRaft;

        if (MultiRaftRegistry
                .TryDecodeHotSlot(
                    __instance.hotSlotIndex,
                    out raftId,
                    out hotSlot,
                    out newRaft
                ))
        {
            return;
        }

        SecondaryRaftRoot root =
            MultiRaftRegistry
                .GetRootFromTransform(
                    MultiRaftRegistry
                        .CurrentBuildPivot
                );

        if (root == null)
        {
            return;
        }

        __instance.hotSlotIndex =
            MultiRaftRegistry.EncodeHotSlot(
                root.RaftId,
                __instance.hotSlotIndex,
                false
            );
    }
}

[HarmonyPatch]
public static class Message_BlockCreator_UpgradeBlock_Constructor_RebuildableRaft
{
    private static IEnumerable<MethodBase>
        TargetMethods()
    {
        ConstructorInfo[] constructors =
            typeof(Message_BlockCreator_UpgradeBlock)
                .GetConstructors(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic
                );

        for (int i = 0;
             i < constructors.Length;
             i++)
        {
            if (constructors[i]
                    .GetParameters()
                    .Length > 0)
            {
                yield return constructors[i];
            }
        }
    }

    [HarmonyPostfix]
    public static void Postfix(
        Message_BlockCreator_UpgradeBlock __instance)
    {
        if (__instance == null)
        {
            return;
        }

        SecondaryRaftRoot root =
            MultiRaftRegistry
                .GetRootFromTransform(
                    MultiRaftRegistry
                        .CurrentBuildPivot
                );

        if (root == null)
        {
            return;
        }

        __instance.hotSlotIndex =
            MultiRaftRegistry.EncodeHotSlot(
                root.RaftId,
                -1,
                false
            );
    }
}

[HarmonyPatch(typeof(BlockCreator), "Deserialize")]
public static class BlockCreator_Deserialize_RebuildableRaft
{
    private static bool TryHandleCreateSnapshot(
        BlockCreator creator,
        Message_NetworkBehaviour msg,
        ref bool result)
    {
        if (creator == null ||
            msg == null ||
            msg.Type != Messages.BlockCreator_Create)
        {
            return false;
        }

        Message_BlockCreator_Create create =
            msg as Message_BlockCreator_Create;

        if (create == null ||
            create.rgdBlocks == null)
        {
            return false;
        }

        bool hasSecondary = false;

        for (int i = 0;
             i < create.rgdBlocks.Length;
             i++)
        {
            SecondaryRaftSaveData data;

            if (MultiRaftRegistry.TryGetRgdData(
                    create.rgdBlocks[i],
                    out data
                ))
            {
                hasSecondary = true;
                break;
            }
        }

        if (!hasSecondary)
        {
            return false;
        }

        Transform previousPivot =
            MultiRaftRegistry.CurrentBuildPivot;

        try
        {
            for (int i = 0;
                 i < create.rgdBlocks.Length;
                 i++)
            {
                RGD_Block rgd =
                    create.rgdBlocks[i];

                if (rgd == null)
                {
                    continue;
                }

                SecondaryRaftSaveData data;
                SecondaryRaftRoot root = null;

                if (MultiRaftRegistry.TryGetRgdData(
                        rgd,
                        out data
                    ))
                {
                    root =
                        MultiRaftRegistry.GetRoot(
                            data.raftId
                        );

                    if (root == null)
                    {
                        root =
                            MultiRaftRegistry.CreateRoot(
                                data.raftId,
                                new Vector3(
                                    data.x,
                                    data.y,
                                    data.z
                                ),
                                data.yaw
                            );
                    }
                }

                Transform pivot =
                    root != null
                        ? root.BuildPivot
                        : MultiRaftRegistry.MainPivot;

                if (pivot == null)
                {
                    continue;
                }

                MultiRaftRegistry.SetBuildPivot(
                    pivot
                );

                Item_Base item =
                    ItemManager.GetItemByIndex(
                        rgd.BlockIndex
                    );

                if (item == null)
                {
                    continue;
                }

                Block block =
                    creator.CreateBlock(
                        item,
                        rgd.LocalBlockPosition,
                        rgd.LocalBlockRotation,
                        rgd.DPSType,
                        -1,
                        true,
                        rgd.BlockObjectIndex,
                        rgd.NetworkedObjectIndex,
                        rgd.NetworkedBehaviourIndex
                    );

                if (block == null)
                {
                    continue;
                }

                rgd.RestoreBlock(
                    block
                );

                if (root != null)
                {
                    MultiRaftRegistry.TagBlock(
                        block,
                        root
                    );
                }
            }
        }
        finally
        {
            MultiRaftRegistry.SetBuildPivot(
                previousPivot != null
                    ? previousPivot
                    : MultiRaftRegistry.MainPivot
            );
        }

        result = false;
        return true;
    }

    public class DeserializeState
    {
        public Transform previousPivot;
        public bool encoded;
        public bool isPlace;
        public bool newRaft;
        public int raftId;
        public int originalHotSlot;
        public int encodedHotSlot;
        public Vector3 encodedRootPosition;
        public SecondaryRaftRoot root;
    }

    [HarmonyPrefix]
    public static bool Prefix(
        BlockCreator __instance,
        Message_NetworkBehaviour msg,
        Network_UserId remoteID,
        ref bool __result,
        out DeserializeState __state)
    {
        __state =
            null;

        if (TryHandleCreateSnapshot(
                __instance,
                msg,
                ref __result
            ))
        {
            return false;
        }

        if (msg == null)
        {
            return true;
        }

        if (msg.Type ==
            Messages.BlockCreator_PlaceBlock)
        {
            Message_BlockCreator_PlaceBlock
                place =
                    msg as
                    Message_BlockCreator_PlaceBlock;

            if (place == null)
            {
                return true;
            }

            int raftId;
            int hotSlot;
            bool newRaft;

            if (!MultiRaftRegistry
                    .TryDecodeHotSlot(
                        place.hotSlotIndex,
                        out raftId,
                        out hotSlot,
                        out newRaft
                    ))
            {
                return true;
            }

            DeserializeState state =
                new DeserializeState();

            state.previousPivot =
                MultiRaftRegistry
                    .CurrentBuildPivot;

            state.encoded =
                true;

            state.isPlace =
                true;

            state.newRaft =
                newRaft;

            state.raftId =
                raftId;

            state.originalHotSlot =
                hotSlot;

            state.encodedHotSlot =
                place.hotSlotIndex;

            if (newRaft)
            {
                state.encodedRootPosition =
                    place.LocalPosition;

                if (Raft_Network.IsHost &&
                    raftId == 0)
                {
                    raftId =
                        MultiRaftRegistry
                            .AllocateRaftId();

                    state.raftId =
                        raftId;

                    place.hotSlotIndex =
                        MultiRaftRegistry
                            .EncodeHotSlot(
                                raftId,
                                hotSlot,
                                true
                            );

                    state.encodedHotSlot =
                        place.hotSlotIndex;
                }
            }

            SecondaryRaftRoot root =
                MultiRaftRegistry.GetRoot(
                    raftId
                );

            if (root == null &&
                newRaft &&
                raftId > 0)
            {
                root =
                    MultiRaftRegistry.CreateRoot(
                        raftId,
                        state.encodedRootPosition,
                        0f
                    );
            }

            if (root == null)
            {
                Debug.LogWarning(
                    "[RebuildableRaft] Missing secondary raft " +
                    raftId
                );

                __result = false;
                return false;
            }

            state.root =
                root;

            MultiRaftRegistry.SetBuildPivot(
                root.BuildPivot
            );

            if (newRaft)
            {
                place.LocalPosition =
                    Vector3.zero;
            }

            if (!Raft_Network.IsHost)
            {
                place.hotSlotIndex =
                    hotSlot;
            }

            __state =
                state;

            return true;
        }

        if (msg.Type ==
            Messages.BlockCreator_UpgradeBlock)
        {
            Message_BlockCreator_UpgradeBlock
                upgrade =
                    msg as
                    Message_BlockCreator_UpgradeBlock;

            if (upgrade == null)
            {
                return true;
            }

            int raftId;
            int hotSlot;
            bool newRaft;

            if (!MultiRaftRegistry
                    .TryDecodeHotSlot(
                        upgrade.hotSlotIndex,
                        out raftId,
                        out hotSlot,
                        out newRaft
                    ))
            {
                return true;
            }

            SecondaryRaftRoot root =
                MultiRaftRegistry.GetRoot(
                    raftId
                );

            if (root == null)
            {
                __result = false;
                return false;
            }

            DeserializeState state =
                new DeserializeState();

            state.previousPivot =
                MultiRaftRegistry
                    .CurrentBuildPivot;

            state.encoded =
                true;

            state.isPlace =
                false;

            state.raftId =
                raftId;

            state.originalHotSlot =
                hotSlot;

            state.encodedHotSlot =
                upgrade.hotSlotIndex;

            state.root =
                root;

            MultiRaftRegistry.SetBuildPivot(
                root.BuildPivot
            );

            if (!Raft_Network.IsHost)
            {
                upgrade.hotSlotIndex =
                    hotSlot;
            }

            __state =
                state;

            return true;
        }

        return true;
    }

    [HarmonyPostfix]
    public static void Postfix(
        Message_NetworkBehaviour msg,
        DeserializeState __state)
    {
        if (__state == null ||
            !__state.encoded)
        {
            return;
        }

        if (__state.isPlace)
        {
            Message_BlockCreator_PlaceBlock
                place =
                    msg as
                    Message_BlockCreator_PlaceBlock;

            if (place != null)
            {
                if (Raft_Network.IsHost &&
                    __state.newRaft &&
                    __state.root != null)
                {
                    place.LocalPosition =
                        __state.root
                            .transform.position;
                }

                place.hotSlotIndex =
                    MultiRaftRegistry
                        .EncodeHotSlot(
                            __state.raftId,
                            __state.originalHotSlot,
                            __state.newRaft
                        );
            }
        }
        else
        {
            Message_BlockCreator_UpgradeBlock
                upgrade =
                    msg as
                    Message_BlockCreator_UpgradeBlock;

            if (upgrade != null)
            {
                upgrade.hotSlotIndex =
                    MultiRaftRegistry
                        .EncodeHotSlot(
                            __state.raftId,
                            -1,
                            false
                        );
            }
        }

        MultiRaftRegistry.SetBuildPivot(
            __state.previousPivot != null
                ? __state.previousPivot
                : MultiRaftRegistry.MainPivot
        );
    }
}

[HarmonyPatch(typeof(BlockCreator), "CreateBlock")]
public static class BlockCreator_CreateBlock_RebuildableRaft
{
    [HarmonyPostfix]
    public static void Postfix(
        Block __result)
    {
        if (__result == null)
        {
            return;
        }

        SecondaryRaftRoot root =
            MultiRaftRegistry
                .GetRootFromBlock(
                    __result
                );

        if (root == null)
        {
            return;
        }

        MultiRaftRegistry.TagBlock(
            __result,
            root
        );

        root.NotifyBlockChanged();

        if (__result.buildableItem != null &&
            Block.IsBlockIndexFoundation(
                __result.buildableItem
                    .UniqueIndex
            ) &&
            BlockCreator.FoundationCount > 0)
        {
            BlockCreator.FoundationCount--;
        }
    }
}
