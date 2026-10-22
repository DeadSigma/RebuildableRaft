using HarmonyLib;
using System.Reflection;
using UnityEngine;

// Учитываются коллайдеры дополнительных плотов
[HarmonyPatch(typeof(Hammer), "HandleRepairingAndReinforcement")]
internal static class SecondaryRaftHammerFix
{
    private static readonly MethodInfo RepairMethod = AccessTools.Method(
        typeof(Hammer), "HandleRepairingOfBlock");

    private static readonly MethodInfo ReinforceMethod = AccessTools.Method(
        typeof(Hammer), "HandleReinforcementOfBlock");

    [HarmonyPrefix]
    private static bool Prefix(Hammer __instance, Network_Player ___playerNetwork,
        BlockCreator ___blockCreator, GhostDisplayer ___reinforceGhostObject,
        Item_Base ___reinforceItem)
    {
        if (__instance == null || ___playerNetwork == null ||
            !___playerNetwork.IsLocalPlayer ||
            PlayerItemManager.IsBusy || CanvasHelper.ActiveMenu != MenuType.None ||
            RepairMethod == null || ReinforceMethod == null)
            return true;

        Block block;
        RaycastHit hit;
        if (!TryHitBlock(___playerNetwork, out block, out hit) ||
            !MultiRaftRegistry.IsSecondaryBlock(block))
            return true;

        // Проверки и сетевые вызовы выполняются штатными методами
        RepairMethod.Invoke(__instance, new object[] { block, hit });
        ReinforceMethod.Invoke(__instance, new object[] { block, hit });

        if (___reinforceGhostObject != null &&
            (___blockCreator == null || ___blockCreator.buildMenu == null ||
             ___blockCreator.buildMenu.lastSelectedBuildabe != ___reinforceItem))
            ___reinforceGhostObject.Hide();

        return false;
    }

    private static bool TryHitBlock(Network_Player player,
        out Block block, out RaycastHit selectedHit)
    {
        block = null;
        selectedHit = default(RaycastHit);
        if (player.CameraTransform == null)
            return false;

        Ray ray = new Ray(player.CameraTransform.position,
            player.CameraTransform.forward);
        float range = Player.UseDistance * 2f;

        Physics.SyncTransforms();

        // Проверяются все слои коллизий
        RaycastHit[] hits = Physics.RaycastAll(ray, range, ~0,
            QueryTriggerInteraction.Ignore);
        if (FindClosestBlock(hits, out block, out selectedHit))
            return true;

        // Учитываются края повреждённого фундамента
        hits = Physics.SphereCastAll(ray, 0.12f, range, ~0,
            QueryTriggerInteraction.Ignore);
        return FindClosestBlock(hits, out block, out selectedHit);
    }

    private static bool FindClosestBlock(RaycastHit[] hits,
        out Block result, out RaycastHit selectedHit)
    {
        result = null;
        selectedHit = default(RaycastHit);
        float nearest = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || hit.distance >= nearest)
                continue;

            Block candidate = hit.collider.GetComponentInParent<Block>();
            if (candidate == null || !candidate.hasBeenPlaced ||
                !candidate.gameObject.activeInHierarchy ||
                candidate.buildableItem == null)
                continue;

            int index = candidate.buildableItem.UniqueIndex;
            if (!Block.IsBlockIndexFoundation(index) &&
                !Block.IsBlockIndexCollectionNet(index))
                continue;

            result = candidate;
            selectedHit = hit;
            nearest = hit.distance;
        }

        return result != null;
    }
}
