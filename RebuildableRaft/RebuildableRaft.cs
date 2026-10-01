using HarmonyLib;
using HMLLibrary;
using Steamworks;
using System.Reflection;
using UnityEngine;

public class RebuildableRaft : Mod
{
    private Harmony harmony;

    public void Awake()
    {
        harmony = new Harmony("el.rebuildableraft");
        harmony.PatchAll();

        Debug.Log("[RebuildableRaft] Loaded");
    }

    public void OnModUnload()
    {
        if (harmony != null)
        {
            harmony.UnpatchAll(harmony.Id);
        }

        Debug.Log("[RebuildableRaft] Unloaded");
    }
}

[HarmonyPatch(typeof(BlockCreator), "Update")]
public static class BlockCreator_Update_RebuildableRaft
{
    private static readonly FieldInfo LockedBuildPivotField =
        AccessTools.Field(typeof(BlockCreator), "lockedBuildPivot");

    private static readonly FieldInfo GameManagerField =
        AccessTools.Field(typeof(BlockCreator), "gameManager");

    private static readonly FieldInfo GhostGreenField =
        AccessTools.Field(typeof(GameManager), "ghostMaterialGreen");

    private static readonly FieldInfo GhostRedField =
        AccessTools.Field(typeof(GameManager), "ghostMaterialRed");

    private static readonly FieldInfo RaftBodyField =
        AccessTools.Field(typeof(Raft), "body");

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
        Network_Player ___playerNetwork)
    {
        if (___playerNetwork == null || !___playerNetwork.IsLocalPlayer)
        {
            return true;
        }

        if (HasPlacedBlocks())
        {
            ResetPreviewState();
            return true;
        }

        Item_Base item = ___selectedBuildableItem;
        if (item == null || !Block.IsBlockIndexFoundation(item.UniqueIndex))
        {
            ResetPreviewState();
            return true;
        }

        // Меню оставляется ванильному коду
        if (CanvasHelper.ActiveMenu != MenuType.None ||
            MyInput.GetButtonDown("RMB") ||
            MyInput.GetButtonUp("RMB"))
        {
            return true;
        }

        Block ghost = __instance.selectedBlock;
        if (ghost == null || ___selectedBuildablePrefab == null)
        {
            return true;
        }

        Transform pivot = LockedBuildPivotField.GetValue(null) as Transform;
        if (pivot == null)
        {
            return true;
        }

        if (ghost.buildableItem.settings_buildable.MirroredVersion != null &&
            Input.GetKeyDown(KeyCode.Z))
        {
            __instance.SetBlockTypeToBuild(
                ghost.buildableItem.settings_buildable.MirroredVersion
            );

            return false;
        }

        HandleRotation(ghost, ___selectedBuildablePrefab);

        Vector3 worldPosition;
        if (!TryGetBuildPosition(___playerNetwork, pivot, ghost, out worldPosition))
        {
            __instance.SetGhostBlockVisibility(false);
            ResetPreviewState();
            return false;
        }

        ghost.transform.position = worldPosition;

        float gridYaw =
            pivot.eulerAngles.y +
            ___selectedBuildablePrefab.currentRotationY;

        ghost.transform.rotation = Quaternion.Euler(
            0f,
            gridYaw,
            0f
        );

        __instance.SetGhostBlockVisibility(true);

        bool canBuild =
            ghost.IsOverlapping() == OverlappType.None &&
            __instance.HasEnoughResourcesToBuild(ghost);

        SetGhostMaterial(ghost, canBuild);

        if (canBuild && MyInput.GetButtonDown("LMB"))
        {
            PlaceFoundation(
                __instance,
                ___playerNetwork,
                item,
                ghost,
                ___selectedBuildablePrefab.currentRotationY
            );
        }

        return false;
    }

    private static bool HasPlacedBlocks()
    {
        var blocks = BlockCreator.GetPlacedBlocks();

        if (blocks == null || blocks.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < blocks.Count; i++)
        {
            Block block = blocks[i];

            if (block != null &&
                block.hasBeenPlaced &&
                block.gameObject.activeSelf)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetBuildPosition(
        Network_Player player,
        Transform pivot,
        Block ghost,
        out Vector3 worldPosition)
    {
        worldPosition = Vector3.zero;

        if (player.CameraTransform == null)
        {
            return false;
        }

        Vector3 forward = player.CameraTransform.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.001f)
        {
            forward = player.transform.forward;
            forward.y = 0f;
        }

        if (forward.sqrMagnitude < 0.001f)
        {
            return false;
        }

        forward.Normalize();

        Vector3 playerPosition = player.transform.position;
        Vector3 targetPosition = playerPosition + forward * PreviewDistance;
        float waterY = GetFoundationWorldY();
        targetPosition.y = waterY;

        bool ghostChanged = previewGhost != ghost;
        bool positionTooFar =
            previewPositionInitialized &&
            Vector3.Distance(previewWorldPosition, targetPosition) > 5f;

        if (!previewPositionInitialized || ghostChanged || positionTooFar)
        {
            previewWorldPosition = targetPosition;
            previewPositionInitialized = true;
            previewGhost = ghost;
        }
        else
        {
            float follow =
                1f - Mathf.Exp(-PreviewFollowSpeed * Time.deltaTime);

            previewWorldPosition = Vector3.Lerp(
                previewWorldPosition,
                targetPosition,
                follow
            );

            previewWorldPosition.y = waterY;
        }

        worldPosition = previewWorldPosition;
        return true;
    }

    private static void ResetPreviewState()
    {
        previewPositionInitialized = false;
        previewGhost = null;
        previewWorldPosition = Vector3.zero;
    }

    internal static int CountPlacedBlocks()
    {
        var blocks = BlockCreator.GetPlacedBlocks();
        if (blocks == null)
        {
            return 0;
        }

        int count = 0;

        for (int i = 0; i < blocks.Count; i++)
        {
            Block block = blocks[i];

            if (block != null &&
                block.hasBeenPlaced &&
                block.gameObject.activeSelf)
            {
                count++;
            }
        }

        return count;
    }

    internal static float GetFoundationWorldY()
    {
        GameManager gameManager = SingletonGeneric<GameManager>.Singleton;

        if (gameManager != null && gameManager.water != null)
        {
            return gameManager.water.transform.position.y + FoundationSurfaceOffset;
        }

        return FoundationSurfaceOffset;
    }

    private static void HandleRotation(
        Block ghost,
        Block selectedBuildablePrefab)
    {
        if (!ghost.isRotateable || !MyInput.GetButtonDown("Rotate"))
        {
            return;
        }

        float rotation = selectedBuildablePrefab.currentRotationY;
        rotation += ghost.rotateAmount;

        while (rotation >= 360f)
        {
            rotation -= 360f;
        }

        while (rotation < 0f)
        {
            rotation += 360f;
        }

        selectedBuildablePrefab.currentRotationY = rotation;
    }

    private static void SetGhostMaterial(Block ghost, bool canBuild)
    {
        if (ghost == null || ghost.occupyingComponent == null)
        {
            return;
        }

        object gameManager = GameManagerField.GetValue(null);
        if (gameManager == null)
        {
            return;
        }

        FieldInfo materialField =
            canBuild ? GhostGreenField : GhostRedField;

        if (materialField == null)
        {
            return;
        }

        Material material = materialField.GetValue(gameManager) as Material;
        if (material != null)
        {
            ghost.occupyingComponent.SetNewMaterial(material);
        }
    }

    private static void PrepareRaftForFirstFoundation(
        Transform pivot,
        Vector3 targetWorldPosition)
    {
        GameManager gameManager = SingletonGeneric<GameManager>.Singleton;
        Raft raft = ComponentManager<Raft>.Value;

        Rigidbody body =
            raft != null && RaftBodyField != null
                ? RaftBodyField.GetValue(raft) as Rigidbody
                : null;

        if (gameManager != null && gameManager.buoyancyPivot != null)
        {
            Vector3 euler = gameManager.buoyancyPivot.eulerAngles;

            gameManager.buoyancyPivot.rotation = Quaternion.Euler(
                0f,
                euler.y,
                0f
            );
        }

        if (body != null)
        {
            Quaternion rotation = body.rotation;

            body.rotation = Quaternion.Euler(
                0f,
                rotation.eulerAngles.y,
                0f
            );

            Vector3 velocity = body.velocity;
            velocity.y = 0f;
            body.velocity = velocity;

            Vector3 angularVelocity = body.angularVelocity;
            angularVelocity.x = 0f;
            angularVelocity.z = 0f;
            body.angularVelocity = angularVelocity;
        }

        Vector3 delta = targetWorldPosition - pivot.position;

        if (body != null)
        {
            body.position += delta;
        }

        if (raft != null)
        {
            raft.transform.position += delta;
        }

        if (gameManager != null)
        {
            if (gameManager.buoyancy != null)
            {
                gameManager.buoyancy.transform.position += delta;
            }

            if (gameManager.raftFollowParent != null)
            {
                gameManager.raftFollowParent.position += delta;
            }
        }

        Vector3 correction = targetWorldPosition - pivot.position;

        if (correction.sqrMagnitude > 0.000001f)
        {
            pivot.position += correction;
        }

        Debug.Log(
            "[RebuildableRaft] Плот подготовлен к первой платформе"
        );
    }

    private static void PlaceFoundation(
        BlockCreator creator,
        Network_Player player,
        Item_Base item,
        Block ghost,
        float gridRotationY)
    {
        int hotSlotIndex =
            player.Inventory.hotbar.GetSelectedSlotIndex();

        Transform pivot = LockedBuildPivotField.GetValue(null) as Transform;
        if (pivot == null)
        {
            return;
        }

        Vector3 worldPosition = ghost.transform.position;
        PrepareRaftForFirstFoundation(pivot, worldPosition);

        Vector3 localPosition = Vector3.zero;

        float snap = ghost.snapRotateAmount;
        if (snap > 0.001f)
        {
            gridRotationY =
                Mathf.Round(gridRotationY / snap) * snap;
        }

        while (gridRotationY >= 360f)
        {
            gridRotationY -= 360f;
        }

        while (gridRotationY < 0f)
        {
            gridRotationY += 360f;
        }

        Vector3 localEuler = new Vector3(
            0f,
            gridRotationY,
            0f
        );

        if (Raft_Network.IsHost)
        {
            uint blockObjectIndex =
                SaveAndLoad.GetUniqueObjectIndex();

            uint networkedObjectIndex =
                ghost.networkedBehaviour != null
                    ? SaveAndLoad.GetUniqueObjectIndex()
                    : 0U;

            uint networkedBehaviourIndex =
                ghost.networkedBehaviour != null
                    ? NetworkUpdateManager.GetUniqueBehaviourIndex()
                    : 0U;

            Message_BlockCreator_PlaceBlock message =
                new Message_BlockCreator_PlaceBlock(
                    Messages.BlockCreator_PlaceBlock,
                    creator,
                    item.UniqueIndex,
                    blockObjectIndex,
                    networkedObjectIndex,
                    networkedBehaviourIndex,
                    localPosition,
                    localEuler,
                    -1,
                    DPS.Default
                );

            player.Network.RPC(
                message,
                Target.Other,
                EP2PSend.k_EP2PSendReliable,
                NetworkChannel.Channel_Game
            );

            creator.CreateBlock(
                item,
                localPosition,
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

        Message_BlockCreator_PlaceBlock clientMessage =
            new Message_BlockCreator_PlaceBlock(
                Messages.BlockCreator_PlaceBlock,
                creator,
                item.UniqueIndex,
                0U,
                0U,
                0U,
                localPosition,
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
}
