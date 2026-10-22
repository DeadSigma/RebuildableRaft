using HarmonyLib;
using HMLLibrary;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

// Атаки останавливаются до уничтожения сетевых компонентов при выходе из мира
internal static class SecondaryRaftSharkSession
{
    internal static bool IsLeavingWorld { get; private set; }

    internal static void BeginWorld()
    {
        IsLeavingWorld = false;
    }

    internal static void BeginShutdown()
    {
        IsLeavingWorld = true;
    }

    internal static bool CanProcessSharkDamage
    {
        get
        {
            if (IsLeavingWorld || SaveAndLoad.IsGameLoading ||
                !RAPI.IsCurrentSceneGame())
                return false;

            Raft_Network network = ComponentManager<Raft_Network>.Value;
            Network_Host_Entities entities = ComponentManager<Network_Host_Entities>.Value;
            return network != null && entities != null &&
                entities.gameObject.activeInHierarchy;
        }
    }
}

// За каждой дополнительной акулой закрепляется отдельный плот
public sealed class SecondaryRaftSharkBinding : MonoBehaviour
{
    public int RaftId = -1;
    public Vector3 InitialSpawnPosition;

    private AI_StateMachine_Shark shark;
    private float nextResolveTime;
    private float initializedAt;
    private float ownerAssignedAt;
    private float ownerMissingSince = -1f;
    private bool hadValidOwner;
    private bool removalRequested;

    private void Awake()
    {
        initializedAt = Time.realtimeSinceStartup;
    }

    public AI_StateMachine_Shark Shark
    {
        get
        {
            if (shark == null)
                shark = GetComponent<AI_StateMachine_Shark>();
            return shark;
        }
    }

    public void Assign(int raftId)
    {
        RaftId = raftId;
        ownerAssignedAt = Time.realtimeSinceStartup;
        ownerMissingSince = -1f;
        if (raftId > 0)
        {
            SecondaryRaftRoot root = GetRoot();
            hadValidOwner = root != null && root.HasPlacedBlocks;
        }
        ApplyPivot();
    }

    public void ApplyPivot()
    {
        AI_StateMachine_Shark machine = Shark;
        if (machine == null || RaftId < 0)
            return;

        Transform pivot = RaftId == 0
            ? MultiRaftRegistry.MainPivot
            : GetSecondaryPivot();

        if (pivot != null)
            machine.lockedPivot = pivot;
    }

    public SecondaryRaftRoot GetRoot()
    {
        return RaftId > 0 ? MultiRaftRegistry.GetRoot(RaftId) : null;
    }

    private Transform GetSecondaryPivot()
    {
        SecondaryRaftRoot root = GetRoot();
        return root != null ? root.BuildPivot : null;
    }

    // При исчезновении собственного плота ИИ останавливается до удаления объекта
    public bool OwnerUnavailable()
    {
        if (RaftId <= 0 || SaveAndLoad.IsGameLoading)
            return false;

        SecondaryRaftRoot root = GetRoot();
        if (root != null && root.HasPlacedBlocks && root.BuildPivot != null)
        {
            hadValidOwner = true;
            ownerMissingSince = -1f;
            return false;
        }

        if (ownerMissingSince < 0f)
            ownerMissingSince = Time.realtimeSinceStartup;
        return true;
    }

    public bool ShouldDespawn()
    {
        if (!OwnerUnavailable())
            return false;

        // Хост удаляет акулу сразу после потери подтверждённого плота
        if (hadValidOwner && Raft_Network.IsHost)
            return true;

        float missingFor = Time.realtimeSinceStartup - ownerMissingSince;
        if (hadValidOwner)
            return missingFor >= 0.5f;

        // После загрузки даётся время на регистрацию сохранённого плота
        float assignedFor = Time.realtimeSinceStartup - ownerAssignedAt;
        return assignedFor >= 12f && missingFor >= 5f;
    }

    public void Despawn()
    {
        if (removalRequested || SaveAndLoad.IsGameLoading ||
            SecondaryRaftSharkSession.IsLeavingWorld)
            return;

        removalRequested = true;
        AI_StateMachine_Shark machine = Shark;
        if (machine == null)
        {
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
            Destroy(gameObject);
            return;
        }

        // Сетевая сущность удаляется целиком, а не только компонент ИИ
        GameObject owner = machine.networkBehaviour != null
            ? machine.networkBehaviour.gameObject : machine.gameObject;
        if (owner == null)
            owner = machine.gameObject;
        owner.SetActive(false);
        Destroy(owner);
    }

    private void Update()
    {
        if (removalRequested || SaveAndLoad.IsGameLoading ||
            SecondaryRaftSharkSession.IsLeavingWorld)
            return;

        // Проверка выполняется каждый кадр, а не раз в несколько секунд
        if (RaftId > 0 && OwnerUnavailable())
        {
            if (ShouldDespawn())
                Despawn();
            return;
        }

        if (Time.realtimeSinceStartup < nextResolveTime)
            return;

        nextResolveTime = Time.realtimeSinceStartup + 1.5f;
        if (RaftId < 0)
        {
            SecondaryRaftRoot root = SecondaryRaftSharkLogic.FindRootAtSpawn(
                InitialSpawnPosition, 14f);
            if (root != null)
                Assign(root.RaftId);
            else if (Time.realtimeSinceStartup - initializedAt > 20f)
                Assign(SecondaryRaftSharkLogic.ClassifyRestoredShark(
                    InitialSpawnPosition));
        }

        // При исчезновении плота акула не назначается главному плоту
        ApplyPivot();
    }
}

// Удаление корневого объекта также удаляет закреплённую акулу
public sealed class SecondaryRaftSharkRaftLifetime : MonoBehaviour
{
    private int raftId;

    public void Initialize(int id)
    {
        raftId = id;
    }

    private void OnDestroy()
    {
        if (raftId > 0 && !SaveAndLoad.IsGameLoading &&
            !SecondaryRaftSharkSession.IsLeavingWorld)
            SecondaryRaftSharkLogic.DespawnForRaft(raftId);
    }
}

internal static class SecondaryRaftSharkLogic
{
    private const float SharkTerritoryRadius = 36f;
    private const float PlayerRaftSearchDistance = 30f;

    private static readonly FieldInfo SpawnSharkField =
        AccessTools.Field(typeof(Network_Host_Entities), "spawnShark");

    private static readonly FieldInfo StateMachineField =
        AccessTools.Field(typeof(AI_State), "stateMachine");

    internal static int PendingRaftId = -1;
    internal static bool InHostFactoryCall;

    internal static void DespawnForRaft(int raftId)
    {
        if (raftId <= 0 || SaveAndLoad.IsGameLoading ||
            SecondaryRaftSharkSession.IsLeavingWorld)
            return;

        SecondaryRaftSharkBinding[] bindings =
            UnityEngine.Object.FindObjectsOfType<SecondaryRaftSharkBinding>();
        for (int i = 0; i < bindings.Length; i++)
        {
            SecondaryRaftSharkBinding binding = bindings[i];
            if (binding != null && binding.RaftId == raftId)
                binding.Despawn();
        }
    }

    internal static bool SharksEnabled(Network_Host_Entities entities)
    {
        if (entities == null || SpawnSharkField == null)
            return false;

        return (bool)SpawnSharkField.GetValue(entities);
    }

    internal static SecondaryRaftSharkBinding GetBinding(AI_StateMachine_Shark shark)
    {
        return shark != null
            ? shark.GetComponent<SecondaryRaftSharkBinding>()
            : null;
    }

    internal static AI_StateMachine_Shark GetMachine(AI_State_Attack_Block_Shark state)
    {
        if (state == null)
            return null;

        AI_StateMachine_Shark machine =
            state.GetComponentInParent<AI_StateMachine_Shark>();

        if (machine == null && StateMachineField != null)
            machine = StateMachineField.GetValue(state) as AI_StateMachine_Shark;

        return machine;
    }

    internal static void TrackSpawn(AI_NetworkBehaviour behaviour, Vector3 position)
    {
        if (behaviour == null)
            return;

        AI_StateMachine_Shark shark = behaviour.GetComponent<AI_StateMachine_Shark>();
        if (shark == null)
            shark = behaviour.GetComponentInChildren<AI_StateMachine_Shark>();
        if (shark == null)
            return;

        SecondaryRaftSharkBinding binding = GetBinding(shark);
        if (binding == null)
            binding = shark.gameObject.AddComponent<SecondaryRaftSharkBinding>();

        binding.InitialSpawnPosition = position;

        if (Raft_Network.IsHost && InHostFactoryCall)
        {
            binding.Assign(Mathf.Max(0, PendingRaftId));
            return;
        }

        // При сетевом создании принадлежность восстанавливается по точке появления
        SecondaryRaftRoot root = FindRootAtSpawn(position, 14f);
        if (root != null)
        {
            binding.Assign(root.RaftId);
            return;
        }

        if (!Raft_Network.IsHost || IsNearAnySecondarySpawnHeight(position))
            binding.RaftId = -1;
        else
            binding.Assign(ClassifyRestoredShark(position));
    }

    private static bool IsNearAnySecondarySpawnHeight(Vector3 position)
    {
        foreach (SecondaryRaftRoot root in MultiRaftRegistry.GetRoots())
        {
            if (root != null &&
                Mathf.Abs(position.y - (root.BuildPivot.position.y - 10f)) < 5f &&
                Vector3.Distance(position, root.BuildPivot.position + Vector3.down * 10f) < 45f)
                return true;
        }
        return false;
    }

    internal static SecondaryRaftRoot FindRootAtSpawn(Vector3 position, float tolerance)
    {
        SecondaryRaftRoot closest = null;
        float best = tolerance * tolerance;

        foreach (SecondaryRaftRoot root in MultiRaftRegistry.GetRoots())
        {
            if (root == null || !root.HasPlacedBlocks)
                continue;

            Vector3 expected = root.BuildPivot.position + Vector3.down * 10f;
            float dy = Mathf.Abs(expected.y - position.y);
            if (dy > 6f)
                continue;

            float dx = expected.x - position.x;
            float dz = expected.z - position.z;
            float distance = dx * dx + dz * dz;
            if (distance < best)
            {
                closest = root;
                best = distance;
            }
        }

        return closest;
    }

    internal static int ClassifyRestoredShark(Vector3 position)
    {
        Transform main = MultiRaftRegistry.MainPivot;
        Vector3 mainDelta = main != null
            ? main.position - position
            : Vector3.zero;
        float best = main != null
            ? mainDelta.x * mainDelta.x + mainDelta.z * mainDelta.z
            : float.MaxValue;
        int owner = 0;

        foreach (SecondaryRaftRoot root in MultiRaftRegistry.GetRoots())
        {
            if (root == null || !root.HasPlacedBlocks)
                continue;

            Vector3 delta = root.BuildPivot.position - position;
            float distance = delta.x * delta.x + delta.z * delta.z;
            if (distance < best)
            {
                best = distance;
                owner = root.RaftId;
            }
        }

        return owner;
    }

    internal static bool OwnsBlock(AI_StateMachine_Shark shark, Block block)
    {
        if (shark == null || block == null || !block.hasBeenPlaced ||
            !block.gameObject.activeInHierarchy)
            return false;

        SecondaryRaftSharkBinding binding = GetBinding(shark);
        if (binding == null || binding.RaftId < 0)
            return false;

        SecondaryRaftRoot owner = MultiRaftRegistry.GetRootFromBlock(block);
        if (binding.RaftId > 0)
            return owner != null && owner.RaftId == binding.RaftId;

        Transform mainPivot = MultiRaftRegistry.MainPivot;
        return owner == null && mainPivot != null &&
            (block.transform == mainPivot || block.transform.IsChildOf(mainPivot));
    }

    internal static bool OwnsWaterPoint(AI_StateMachine_Shark shark, Vector3 point)
    {
        SecondaryRaftSharkBinding binding = GetBinding(shark);
        if (binding == null || binding.RaftId < 0)
            return false;

        if (binding.RaftId > 0)
            return IsWithinTerritory(binding.GetRoot(), point);

        Transform main = MultiRaftRegistry.MainPivot;
        if (main == null)
            return false;

        Vector3 diff = point - main.position;
        if (diff.x * diff.x + diff.z * diff.z <
            SharkTerritoryRadius * SharkTerritoryRadius)
            return true;

        RaftBounds bounds = ComponentManager<RaftBounds>.Value;
        if (bounds == null || bounds.walkableBlocks == null)
            return false;

        float maxRangeSquared = PlayerRaftSearchDistance * PlayerRaftSearchDistance;
        foreach (Block candidate in bounds.walkableBlocks)
        {
            if (!OwnsBlock(shark, candidate))
                continue;

            diff = candidate.transform.position - point;
            if (diff.x * diff.x + diff.z * diff.z < maxRangeSquared)
                return true;
        }

        return false;
    }

    internal static List<Block> GetAttackableBlocks(SecondaryRaftRoot root)
    {
        List<Block> results = new List<Block>();
        if (root == null)
            return results;

        List<Block> placed = BlockCreator.GetPlacedBlocks();
        if (placed == null)
            return results;

        for (int i = 0; i < placed.Count; i++)
        {
            Block block = placed[i];
            if (block == null || !block.hasBeenPlaced ||
                block.buildableItem == null || !block.gameObject.activeInHierarchy ||
                block.Reinforced || MultiRaftRegistry.GetRootFromBlock(block) != root)
                continue;

            int index = block.buildableItem.UniqueIndex;
            if (Block.IsBlockIndexFoundation(index) ||
                Block.IsBlockIndexCollectionNet(index))
                results.Add(block);
        }

        return results;
    }

    internal static bool PlayerIsOnAssignedRaft(AI_StateMachine_Shark shark)
    {
        SecondaryRaftSharkBinding binding = GetBinding(shark);
        if (binding == null || binding.RaftId < 0)
            return false;
        if (binding.RaftId == 0)
        {
            if (!Helper.IsAPlayerOnTheRaft())
                return false;

            Network_Player[] primaryPlayers =
                UnityEngine.Object.FindObjectsOfType<Network_Player>();
            RaftBounds mainBounds = ComponentManager<RaftBounds>.Value;
            if (mainBounds == null || mainBounds.walkableBlocks == null)
                return false;

            for (int i = 0; i < primaryPlayers.Length; i++)
            {
                Network_Player player = primaryPlayers[i];
                if (player == null || player.IsKilled || player.PersonController == null ||
                    MultiRaftRegistry.FindRootAtGroundPoint(player.FeetPosition) != null)
                    continue;

                Vector3 feet = player.FeetPosition;
                foreach (Block mainBlock in mainBounds.walkableBlocks)
                {
                    if (!OwnsBlock(shark, mainBlock))
                        continue;
                    Vector3 delta = feet - mainBlock.transform.position;
                    if (Mathf.Abs(delta.x) < 1.4f && Mathf.Abs(delta.z) < 1.4f &&
                        Mathf.Abs(delta.y) < 2.5f)
                        return true;
                }
            }
            return false;
        }

        SecondaryRaftRoot root = binding.GetRoot();
        if (root == null || !root.HasPlacedBlocks)
            return false;

        Network_Player[] players = UnityEngine.Object.FindObjectsOfType<Network_Player>();
        for (int i = 0; i < players.Length; i++)
        {
            Network_Player player = players[i];
            if (player == null || player.IsKilled || player.PersonController == null)
                continue;

            if (MultiRaftRegistry.FindRootAtGroundPoint(player.FeetPosition) == root)
                return true;
        }

        return false;
    }

    internal static bool IsWithinTerritory(SecondaryRaftRoot root, Vector3 worldPoint)
    {
        if (root == null)
            return false;

        Vector3 delta = worldPoint - root.BuildPivot.position;
        if (delta.x * delta.x + delta.z * delta.z <
            SharkTerritoryRadius * SharkTerritoryRadius)
            return true;

        List<Block> blocks = GetAttackableBlocks(root);
        float rangeSquared = PlayerRaftSearchDistance * PlayerRaftSearchDistance;
        for (int i = 0; i < blocks.Count; i++)
        {
            Vector3 offset = worldPoint - blocks[i].transform.position;
            if (offset.x * offset.x + offset.z * offset.z < rangeSquared)
                return true;
        }

        return false;
    }
}

// Создание акул передаётся штатному сетевому механизму игры
[HarmonyPatch(typeof(Network_Host_Entities), "Start")]
internal static class SecondaryRaftSharkHostStart
{
    [HarmonyPostfix]
    private static void Postfix(Network_Host_Entities __instance)
    {
        SecondaryRaftSharkSession.BeginWorld();

        if (Raft_Network.IsHost &&
            __instance.GetComponent<SecondaryRaftSharkManager>() == null)
            __instance.gameObject.AddComponent<SecondaryRaftSharkManager>();
    }
}

[HarmonyPatch(typeof(Network_Host_Entities), "CreateAINetworkBehaviour",
    new Type[] { typeof(AI_NetworkBehaviourType), typeof(Vector3), typeof(LandmarkEntitySpawner) })]
internal static class SecondaryRaftSharkFactoryScope
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        SecondaryRaftSharkLogic.InHostFactoryCall = Raft_Network.IsHost;
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(Exception __exception)
    {
        SecondaryRaftSharkLogic.InHostFactoryCall = false;
        return __exception;
    }
}

[HarmonyPatch(typeof(Network_Host_Entities), "CreateAINetworkBehaviour",
    new Type[] { typeof(AI_NetworkBehaviourType), typeof(Vector3), typeof(float),
        typeof(uint), typeof(uint), typeof(LandmarkEntitySpawner) })]
internal static class SecondaryRaftSharkCreated
{
    [HarmonyPostfix]
    private static void Postfix(AI_NetworkBehaviourType behaviourType,
        Vector3 position, AI_NetworkBehaviour __result)
    {
        if (behaviourType == AI_NetworkBehaviourType.Shark)
            SecondaryRaftSharkLogic.TrackSpawn(__result, position);
    }
}

[HarmonyPatch(typeof(AI_StateMachine_Shark), "Start")]
internal static class SecondaryRaftSharkStarted
{
    [HarmonyPostfix]
    private static void Postfix(AI_StateMachine_Shark __instance)
    {
        SecondaryRaftSharkBinding binding = SecondaryRaftSharkLogic.GetBinding(__instance);
        if (binding != null)
            binding.ApplyPivot();
    }
}

[HarmonyPatch(typeof(AI_StateMachine_Shark), "UpdateStateMachine")]
internal static class SecondaryRaftSharkPlayerOnRaft
{
    [HarmonyPrefix]
    private static bool Prefix(AI_StateMachine_Shark __instance)
    {
        if (!SecondaryRaftSharkSession.CanProcessSharkDamage)
            return false;

        SecondaryRaftSharkBinding binding =
            SecondaryRaftSharkLogic.GetBinding(__instance);
        if (binding == null || binding.RaftId <= 0)
            return true;

        // ИИ дополнительной акулы не обновляется во время загрузки мира
        if (SaveAndLoad.IsGameLoading)
            return false;

        // Состояние ИИ не обновляется при удалённом опорном Transform
        if (!binding.OwnerUnavailable())
            return true;

        if (binding.ShouldDespawn())
            binding.Despawn();
        return false;
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo original = AccessTools.Method(typeof(Helper), "IsAPlayerOnTheRaft", Type.EmptyTypes);
        MethodInfo replacement = AccessTools.Method(
            typeof(SecondaryRaftSharkLogic), "PlayerIsOnAssignedRaft");

        foreach (CodeInstruction instruction in instructions)
        {
            if (original != null && replacement != null && instruction.Calls(original))
            {
                CodeInstruction loadShark = new CodeInstruction(OpCodes.Ldarg_0);
                loadShark.labels.AddRange(instruction.labels);
                loadShark.blocks.AddRange(instruction.blocks);
                yield return loadShark;
                yield return new CodeInstruction(OpCodes.Call, replacement);
            }
            else
            {
                yield return instruction;
            }
        }
    }
}

// Защита для акул из старых сохранений с уже уничтоженным опорным Transform
[HarmonyPatch(typeof(AI_StateMachine_Shark), "GetRandomWaterPointInVision")]
internal static class SecondaryRaftSharkInvalidCirculationTarget
{
    [HarmonyPrefix]
    private static bool Prefix(AI_StateMachine_Shark __instance,
        Transform originTarget, ref Vector3 __result)
    {
        if (originTarget != null)
            return true;

        SecondaryRaftSharkBinding binding =
            SecondaryRaftSharkLogic.GetBinding(__instance);
        if (binding != null && binding.RaftId > 0 && binding.ShouldDespawn())
            binding.Despawn();

        // Обращение к originTarget.position при null блокируется
        __result = __instance != null ? __instance.transform.position : Vector3.zero;
        return false;
    }
}

// Список целей фильтруется для ВСЕХ акул, в том числе основной
[HarmonyPatch(typeof(AI_State_Attack_Block_Shark), "GetAttackableBlocks")]
internal static class SecondaryRaftSharkAttackTargets
{
    [HarmonyPostfix]
    private static void Postfix(AI_State_Attack_Block_Shark __instance,
        ref List<Block> __result)
    {
        AI_StateMachine_Shark shark = SecondaryRaftSharkLogic.GetMachine(__instance);
        if (shark == null || __instance != shark.biteRaftState)
            return;

        SecondaryRaftSharkBinding binding = SecondaryRaftSharkLogic.GetBinding(shark);
        if (binding == null || binding.RaftId < 0)
        {
            __result = new List<Block>();
            return;
        }

        if (binding.RaftId > 0)
        {
            __result = SecondaryRaftSharkLogic.GetAttackableBlocks(binding.GetRoot());
            return;
        }

        List<Block> filtered = new List<Block>();
        if (__result != null)
        {
            for (int i = 0; i < __result.Count; i++)
            {
                Block block = __result[i];
                if (SecondaryRaftSharkLogic.OwnsBlock(shark, block))
                    filtered.Add(block);
            }
        }
        __result = filtered;
    }
}

// Событие установки нового блока не должно менять цель чужой акулы
[HarmonyPatch(typeof(AI_State_Attack_Block_Shark), "OnBlockIsPlaced")]
internal static class SecondaryRaftSharkNewBlockTargets
{
    [HarmonyPrefix]
    private static bool Prefix(AI_State_Attack_Block_Shark __instance, Block block)
    {
        AI_StateMachine_Shark shark = SecondaryRaftSharkLogic.GetMachine(__instance);
        return shark == null || __instance != shark.biteRaftState ||
            SecondaryRaftSharkLogic.OwnsBlock(shark, block);
    }
}

// Уже назначенная цель также проверяется непосредственно перед укусом
[HarmonyPatch(typeof(AI_State_Attack_Block_Shark), "UpdateState")]
internal static class SecondaryRaftSharkValidateCurrentTarget
{
    [HarmonyPrefix]
    private static bool Prefix(AI_State_Attack_Block_Shark __instance)
    {
        if (!SecondaryRaftSharkSession.CanProcessSharkDamage)
            return false;

        if (!Raft_Network.IsHost)
            return true;

        AI_StateMachine_Shark shark = SecondaryRaftSharkLogic.GetMachine(__instance);
        if (shark == null || __instance != shark.biteRaftState ||
            shark.currentState != __instance ||
            __instance.currentTargetBlock == null)
            return true;

        if (SecondaryRaftSharkLogic.OwnsBlock(shark, __instance.currentTargetBlock))
            return true;

        shark.ChangeState(shark.diveState);
        return false;
    }
}

[HarmonyPatch(typeof(AI_State_Attack_Block_Shark), "InitiateAttackOnBlock")]
internal static class SecondaryRaftSharkBeforeBite
{
    [HarmonyPrefix]
    private static bool Prefix(AI_State_Attack_Block_Shark __instance,
        Block p_targetBlock)
    {
        if (!SecondaryRaftSharkSession.CanProcessSharkDamage ||
            p_targetBlock == null || p_targetBlock.Health <= 0 ||
            !p_targetBlock.hasBeenPlaced || !p_targetBlock.gameObject.activeInHierarchy)
            return false;

        AI_StateMachine_Shark shark = SecondaryRaftSharkLogic.GetMachine(__instance);
        if (shark == null)
            return false;

        if (__instance != shark.biteRaftState ||
            SecondaryRaftSharkLogic.OwnsBlock(shark, p_targetBlock))
            return true;

        if (Raft_Network.IsHost)
            shark.ChangeState(shark.diveState);
        return false;
    }
}

// Атака не продолжается, если началась выгрузка сцены или цель уже исчезла
[HarmonyPatch(typeof(AI_State_Attack_Block_Shark), "AttackBlock")]
internal static class SecondaryRaftSharkAttackOnExit
{
    [HarmonyPrefix]
    private static bool Prefix(Block p_targetBlock)
    {
        return SecondaryRaftSharkSession.CanProcessSharkDamage &&
            p_targetBlock != null && p_targetBlock.Health > 0 &&
            p_targetBlock.gameObject.activeInHierarchy;
    }
}

// Контроль непосредственно перед нанесением урона защищает от смены цели в этот же кадр
[HarmonyPatch(typeof(AI_State_Attack_Block_Shark), "DealDamageToBlock")]
internal static class SecondaryRaftSharkDamageGuard
{
    [HarmonyPrefix]
    private static bool Prefix(AI_State_Attack_Block_Shark __instance,
        Block p_targetBlock)
    {
        if (!SecondaryRaftSharkSession.CanProcessSharkDamage ||
            p_targetBlock == null || p_targetBlock.Health <= 0 ||
            !p_targetBlock.hasBeenPlaced || !p_targetBlock.gameObject.activeInHierarchy)
            return false;

        AI_StateMachine_Shark shark = SecondaryRaftSharkLogic.GetMachine(__instance);
        if (shark == null)
            return false;

        if (__instance != shark.biteRaftState ||
            SecondaryRaftSharkLogic.OwnsBlock(shark, p_targetBlock))
            return true;

        if (Raft_Network.IsHost)
            shark.ChangeState(shark.diveState);
        return false;
    }
}

// Приманка может привлекать акулу только в пределах её собственного плота
[HarmonyPatch(typeof(AI_StateMachine_Shark), "GetBaitInRange")]
internal static class SecondaryRaftSharkBaitBoundary
{
    [HarmonyPostfix]
    private static void Postfix(AI_StateMachine_Shark __instance,
        ref SharkBait __result)
    {
        if (__result != null &&
            !SecondaryRaftSharkLogic.OwnsWaterPoint(__instance,
                __result.transform.position))
            __result = null;
    }
}

[HarmonyPatch(typeof(AI_StateMachine_Shark), "FindAndSetTargetToAttack")]
internal static class SecondaryRaftSharkPlayerTarget
{
    [HarmonyPostfix]
    private static void Postfix(AI_StateMachine_Shark __instance)
    {
        if (!Raft_Network.IsHost)
            return;

        SecondaryRaftSharkBinding binding = SecondaryRaftSharkLogic.GetBinding(__instance);
        if (binding == null || binding.RaftId < 0)
        {
            __instance.targetToAttack = null;
            return;
        }

        if (binding.RaftId == 0)
        {
            if (__instance.targetToAttack != null &&
                !SecondaryRaftSharkLogic.OwnsWaterPoint(__instance,
                    __instance.targetToAttack.transform.position))
                __instance.targetToAttack = null;
            return;
        }

        SecondaryRaftRoot root = binding.GetRoot();
        if (root == null || !root.HasPlacedBlocks)
        {
            __instance.targetToAttack = null;
            return;
        }

        Network_Player player = Helper.ClosestPlayerInWaterToPoint(
            root.BuildPivot.position, 60f, false);

        __instance.targetToAttack = player != null &&
            SecondaryRaftSharkLogic.OwnsWaterPoint(__instance, player.transform.position)
            ? player : null;
    }
}

// При нескольких плотах возрождение полностью контролируется менеджером
// Штатный респавн в противном случае создаёт акулу у главного плота
[HarmonyPatch(typeof(AI_State_Decay_Shark), "CheckToSpawnOnDecay")]
internal static class SecondaryRaftSharkDisableVanillaRespawn
{
    [HarmonyPrefix]
    private static bool Prefix(ref bool __result)
    {
        if (!Raft_Network.IsHost)
            return true;

        foreach (SecondaryRaftRoot root in MultiRaftRegistry.GetRoots())
        {
            if (root != null && root.HasPlacedBlocks)
            {
                __result = false;
                return false;
            }
        }

        return true;
    }
}

// Только хост проверяет наличие акул и создаёт недостающих
public sealed class SecondaryRaftSharkManager : MonoBehaviour
{
    private void OnApplicationQuit()
    {
        SecondaryRaftSharkSession.BeginShutdown();
    }

    private void OnDisable()
    {
        SecondaryRaftSharkSession.BeginShutdown();
    }

    private void OnDestroy()
    {
        SecondaryRaftSharkSession.BeginShutdown();
    }

    private Network_Host_Entities entities;
    private readonly Dictionary<int, float> missingUntil = new Dictionary<int, float>();
    private readonly HashSet<int> previouslyCovered = new HashSet<int>();
    private float nextCheck;
    private float firstCheck;

    private void Awake()
    {
        entities = GetComponent<Network_Host_Entities>();
        firstCheck = Time.realtimeSinceStartup + 12f;
    }

    private void Update()
    {
        if (!Raft_Network.IsHost ||
            !SecondaryRaftSharkSession.CanProcessSharkDamage ||
            !SecondaryRaftSharkLogic.SharksEnabled(entities) ||
            Time.realtimeSinceStartup < firstCheck ||
            Time.realtimeSinceStartup < nextCheck)
            return;

        nextCheck = Time.realtimeSinceStartup + 4f;
        Reconcile();
    }

    private void Reconcile()
    {
        AI_StateMachine_Shark[] sharks =
            UnityEngine.Object.FindObjectsOfType<AI_StateMachine_Shark>();
        HashSet<int> aliveOwners = new HashSet<int>();

        for (int i = 0; i < sharks.Length; i++)
        {
            AI_StateMachine_Shark shark = sharks[i];
            if (shark == null)
                continue;

            SecondaryRaftSharkBinding binding =
                SecondaryRaftSharkLogic.GetBinding(shark);

            if (binding == null)
            {
                binding = shark.gameObject.AddComponent<SecondaryRaftSharkBinding>();
                binding.InitialSpawnPosition = shark.transform.position;
                binding.Assign(SecondaryRaftSharkLogic.ClassifyRestoredShark(
                    shark.transform.position));
            }
            else if (binding.RaftId < 0)
            {
                binding.Assign(SecondaryRaftSharkLogic.ClassifyRestoredShark(
                    binding.InitialSpawnPosition));
            }

            if (binding.RaftId > 0 && binding.OwnerUnavailable())
            {
                if (binding.ShouldDespawn())
                    binding.Despawn();
                continue;
            }

            if (IsDeadOrDecaying(shark))
                continue;

            aliveOwners.Add(binding.RaftId);
            previouslyCovered.Add(binding.RaftId);
        }

        bool hasSecondary = false;
        foreach (SecondaryRaftRoot root in MultiRaftRegistry.GetRoots())
        {
            if (root == null || !root.HasPlacedBlocks || !root.IsNetworkPoseReady)
                continue;

            hasSecondary = true;
            EnsureShark(root.RaftId, root, aliveOwners);
        }

        if (hasSecondary && MultiRaftRegistry.HasPrimaryBlocks())
            EnsureShark(0, null, aliveOwners);
    }

    private static bool IsDeadOrDecaying(AI_StateMachine_Shark shark)
    {
        if (shark.currentState == null)
            return false;

        string stateName = shark.currentState.GetType().Name;
        return stateName.IndexOf("Dead", StringComparison.OrdinalIgnoreCase) >= 0 ||
            stateName.IndexOf("Decay", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void EnsureShark(int raftId, SecondaryRaftRoot root,
        HashSet<int> aliveOwners)
    {
        if (aliveOwners.Contains(raftId))
        {
            missingUntil.Remove(raftId);
            return;
        }

        float time = Time.realtimeSinceStartup;
        float deadline;
        if (!missingUntil.TryGetValue(raftId, out deadline))
        {
            deadline = time + (previouslyCovered.Contains(raftId)
                ? (raftId == 0 ? 85f : 80f) : 0f);
            missingUntil[raftId] = deadline;
        }

        if (time < deadline)
            return;

        Vector3 spawn = root == null
            ? entities.GetSharkSpawnPosition()
            : root.BuildPivot.position + Vector3.down * 10f;

        SecondaryRaftSharkLogic.PendingRaftId = raftId;
        AI_NetworkBehaviour created;
        try
        {
            created = entities.CreateAINetworkBehaviour(
                AI_NetworkBehaviourType.Shark, spawn, null);
        }
        finally
        {
            SecondaryRaftSharkLogic.PendingRaftId = -1;
        }

        if (created != null)
        {
            previouslyCovered.Add(raftId);
            missingUntil.Remove(raftId);
            aliveOwners.Add(raftId);
        }
        else
        {
            missingUntil[raftId] = time + 10f;
        }
    }
}

// При выгрузке мира блок не отправляет сетевое удаление через уже отключённые компоненты
[HarmonyPatch(typeof(BlockCreator), "RemoveBlockNetwork")]
internal static class SecondaryRaftSharkBlockRemovalOnExit
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool Prefix()
    {
        return !SecondaryRaftSharkSession.IsLeavingWorld;
    }
}
