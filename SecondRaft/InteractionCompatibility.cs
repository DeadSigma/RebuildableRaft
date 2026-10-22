using HarmonyLib;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

public class SecondaryRaftInteractionProxy : MonoBehaviour
{
}

public class SecondaryRaftBlockRayProxy : MonoBehaviour
{
}

public static class SecondaryRaftInteraction
{
    private const float ProxyPadding = 0.08f;
    private const float MinimumProxySize = 0.15f;

    private static int raycastInteractableLayer = -1;
    private static int fallbackProxyLayer = -1;

    public static void EnsureInteractionCollider(
        Block block)
    {
        if (block == null ||
            !MultiRaftRegistry.IsSecondaryBlock(block))
        {
            return;
        }

        if (block.colliderPrefab != null &&
            block.activeColliderPrefab == null)
        {
            ColliderPrefabEnabler enabler =
                ComponentManager<ColliderPrefabEnabler>.Value;

            if (enabler != null)
            {
                enabler.AttachColliderPrefab(block);
            }
        }

        if (block.activeColliderPrefab != null)
        {
            block.activeColliderPrefab.gameObject
                .SetActiveSafe(true);
        }

        EnsureRaycastInteractableProxies(block);
        EnsureBlockRayProxy(block);
    }

    public static RaycastInteractable FindInteractable(
        float distance,
        QueryTriggerInteraction triggerMode)
    {
        if (Helper.MainCamera == null)
        {
            return null;
        }

        Ray ray = Helper.MainCamera.ScreenPointToRay(
            new Vector3(
                Screen.width * 0.5f,
                Screen.height * 0.5f
            )
        );

        RaycastInteractable result =
            FindInteractableOnRay(
                ray,
                distance,
                triggerMode
            );

        if (result != null)
        {
            return result;
        }

        Ray reverseRay =
            new Ray(
                ray.origin +
                ray.direction.normalized * distance,
                -ray.direction
            );

        return FindInteractableOnRay(
            reverseRay,
            distance,
            triggerMode
        );
    }

    public static bool TryFindSecondaryBlockHit(
        out RaycastHit hit,
        float distance,
        QueryTriggerInteraction triggerMode)
    {
        hit = default(RaycastHit);

        if (Helper.MainCamera == null)
        {
            return false;
        }

        Ray ray = Helper.MainCamera.ScreenPointToRay(
            new Vector3(
                Screen.width * 0.5f,
                Screen.height * 0.5f
            )
        );

        RaycastHit[] hits =
            Physics.RaycastAll(
                ray,
                distance,
                ~0,
                QueryTriggerInteraction.Collide
            );

        SortHits(hits);

        for (int i = 0; i < hits.Length; i++)
        {
            Block block =
                GetSecondaryBlockFromHit(hits[i]);

            if (block == null)
            {
                continue;
            }

            hit = hits[i];
            return true;
        }

        return false;
    }

    public static RaycastHit[] FindSecondaryBlockHits(
        float distance)
    {
        if (Helper.MainCamera == null)
        {
            return new RaycastHit[0];
        }

        Ray ray = Helper.MainCamera.ScreenPointToRay(
            new Vector3(
                Screen.width * 0.5f,
                Screen.height * 0.5f
            )
        );

        RaycastHit[] hits =
            Physics.RaycastAll(
                ray,
                distance,
                ~0,
                QueryTriggerInteraction.Collide
            );

        SortHits(hits);

        List<RaycastHit> result =
            new List<RaycastHit>();

        HashSet<Block> added =
            new HashSet<Block>();

        for (int i = 0; i < hits.Length; i++)
        {
            Block block =
                GetSecondaryBlockFromHit(hits[i]);

            if (block == null ||
                !added.Add(block))
            {
                continue;
            }

            result.Add(hits[i]);
        }

        return result.ToArray();
    }

    public static Block FindSecondaryBlockAtCursor(
        float distance,
        out RaycastHit selectedHit)
    {
        selectedHit = default(RaycastHit);

        if (Helper.MainCamera == null)
        {
            return null;
        }

        Ray ray = Helper.MainCamera.ScreenPointToRay(
            new Vector3(
                Screen.width * 0.5f,
                Screen.height * 0.5f
            )
        );

        RaycastHit[] hits =
            Physics.RaycastAll(
                ray,
                distance,
                ~0,
                QueryTriggerInteraction.Collide
            );

        SortHits(hits);

        for (int i = 0; i < hits.Length; i++)
        {
            Block block =
                GetSecondaryBlockFromHit(hits[i]);

            if (block == null)
            {
                continue;
            }

            selectedHit = hits[i];
            return block;
        }

        return null;
    }

    public static Block FindSecondaryRemovablePlaceableAtCursor(
        float distance,
        out RaycastHit selectedHit)
    {
        selectedHit = default(RaycastHit);

        if (Helper.MainCamera == null)
        {
            return null;
        }

        Ray ray = Helper.MainCamera.ScreenPointToRay(
            new Vector3(
                Screen.width * 0.5f,
                Screen.height * 0.5f
            )
        );

        RaycastHit[] hits =
            Physics.RaycastAll(
                ray,
                distance,
                ~0,
                QueryTriggerInteraction.Collide
            );

        SortHits(hits);

        Block fallback = null;
        RaycastHit fallbackHit = default(RaycastHit);

        for (int i = 0; i < hits.Length; i++)
        {
            Block block =
                GetSecondaryBlockFromHit(hits[i]);

            if (block == null)
            {
                continue;
            }

            if (hits[i].collider != null &&
                hits[i].collider.gameObject
                    .CompareTag("IgnoreRemovePlaceables"))
            {
                continue;
            }

            if (fallback == null)
            {
                fallback = block;
                fallbackHit = hits[i];
            }

            if (!block.canBeRemoved ||
                block.buildableItem == null ||
                !block.buildableItem
                    .settings_buildable.Placeable)
            {
                continue;
            }

            selectedHit = hits[i];
            return block;
        }

        if (fallback != null &&
            fallback.canBeRemoved &&
            fallback.buildableItem != null &&
            fallback.buildableItem
                .settings_buildable.Placeable)
        {
            selectedHit = fallbackHit;
            return fallback;
        }

        return null;
    }

    public static bool IsCursorOverSecondaryBlock(
        Block target,
        float distance)
    {
        if (target == null ||
            !MultiRaftRegistry.IsSecondaryBlock(target) ||
            Helper.MainCamera == null)
        {
            return false;
        }

        Ray ray = Helper.MainCamera.ScreenPointToRay(
            new Vector3(
                Screen.width * 0.5f,
                Screen.height * 0.5f
            )
        );

        RaycastHit[] hits =
            Physics.RaycastAll(
                ray,
                distance,
                ~0,
                QueryTriggerInteraction.Collide
            );

        SortHits(hits);

        for (int i = 0; i < hits.Length; i++)
        {
            Block block =
                GetSecondaryBlockFromHit(hits[i]);

            if (block == target)
            {
                return true;
            }
        }

        return false;
    }

    public static Anchor_Throwable_Stand
        FindThrowableAnchorStand(
            float distance)
    {
        if (Helper.MainCamera == null)
        {
            return null;
        }

        Ray ray = Helper.MainCamera.ScreenPointToRay(
            new Vector3(
                Screen.width * 0.5f,
                Screen.height * 0.5f
            )
        );

        RaycastHit[] hits =
            Physics.RaycastAll(
                ray,
                distance,
                ~0,
                QueryTriggerInteraction.Collide
            );

        SortHits(hits);

        for (int i = 0; i < hits.Length; i++)
        {
            Transform hitTransform =
                hits[i].collider != null
                    ? hits[i].collider.transform
                    : hits[i].transform;

            if (hitTransform == null)
            {
                continue;
            }

            Anchor_Throwable_Stand stand =
                hitTransform.GetComponentInParent
                    <Anchor_Throwable_Stand>();

            if (stand != null)
            {
                Block standBlock =
                    stand.GetComponentInParent<Block>();

                if (standBlock != null &&
                    MultiRaftRegistry
                        .IsSecondaryBlock(standBlock))
                {
                    return stand;
                }
            }

            Block block =
                hitTransform.GetComponentInParent<Block>();

            if (block == null ||
                !MultiRaftRegistry
                    .IsSecondaryBlock(block))
            {
                continue;
            }

            stand =
                block.GetComponentInChildren
                    <Anchor_Throwable_Stand>(true);

            if (stand != null)
            {
                return stand;
            }
        }

        return null;
    }

    public static void ExpandRaycastables(
        RaycastInteractable interactable)
    {
        if (interactable == null ||
            !IsSecondaryInteractable(interactable))
        {
            return;
        }

        MonoBehaviour[] behaviours =
            interactable.GetComponentsInChildren
                <MonoBehaviour>(true);

        List<IRaycastable> raycastables =
            new List<IRaycastable>();

        for (int i = 0;
             i < behaviours.Length;
             i++)
        {
            IRaycastable raycastable =
                behaviours[i] as IRaycastable;

            if (raycastable == null)
            {
                continue;
            }

            RaycastInteractable owner =
                FindOwningInteractable(
                    behaviours[i].transform
                );

            if (owner != interactable)
            {
                continue;
            }

            raycastables.Add(raycastable);
        }

        if (raycastables.Count > 0)
        {
            interactable.AddRaycastables(
                raycastables.ToArray()
            );
        }
    }

    private static RaycastInteractable
        FindOwningInteractable(
            Transform target)
    {
        Transform current = target;

        while (current != null)
        {
            RaycastInteractable interactable =
                current.GetComponent
                    <RaycastInteractable>();

            if (interactable != null)
            {
                return interactable;
            }

            current = current.parent;
        }

        return null;
    }

    private static void EnsureRaycastInteractableProxies(
        Block block)
    {
        RaycastInteractable[] interactables =
            block.GetComponentsInChildren
                <RaycastInteractable>(true);

        for (int i = 0;
             i < interactables.Length;
             i++)
        {
            RaycastInteractable interactable =
                interactables[i];

            if (interactable == null)
            {
                continue;
            }

            ExpandRaycastables(interactable);

            if (HasRaycastCollider(interactable))
            {
                continue;
            }

            EnsureInteractableProxy(
                block,
                interactable
            );
        }
    }

    private static void EnsureInteractableProxy(
        Block block,
        RaycastInteractable interactable)
    {
        SecondaryRaftInteractionProxy[] existing =
            interactable.GetComponentsInChildren
                <SecondaryRaftInteractionProxy>(true);

        if (existing != null &&
            existing.Length > 0)
        {
            for (int i = 0;
                 i < existing.Length;
                 i++)
            {
                if (existing[i] != null)
                {
                    existing[i].gameObject.layer =
                        ResolveRaycastInteractableLayer();
                }
            }

            return;
        }

        Bounds localBounds;

        if (!TryGetLocalBounds(
                interactable.transform,
                interactable.transform,
                out localBounds))
        {
            if (!TryGetLocalBounds(
                    block.transform,
                    interactable.transform,
                    out localBounds))
            {
                localBounds =
                    new Bounds(
                        Vector3.zero,
                        new Vector3(
                            0.75f,
                            0.75f,
                            0.75f
                        )
                    );
            }
        }

        GameObject proxyObject =
            new GameObject(
                "SecondaryRaftInteractionProxy"
            );

        proxyObject.layer =
            ResolveRaycastInteractableLayer();

        proxyObject.transform.SetParent(
            interactable.transform,
            false
        );

        proxyObject.transform.localPosition =
            localBounds.center;

        proxyObject.transform.localRotation =
            Quaternion.identity;

        proxyObject.transform.localScale =
            Vector3.one;

        proxyObject.AddComponent
            <SecondaryRaftInteractionProxy>();

        BoxCollider collider =
            proxyObject.AddComponent<BoxCollider>();

        collider.center =
            Vector3.zero;

        collider.size =
            ExpandSize(localBounds.size);

        collider.isTrigger =
            true;

        RaycastInteractable_Redirect redirect =
            proxyObject.AddComponent
                <RaycastInteractable_Redirect>();

        redirect.SetRaycastInteractableTarget(
            interactable
        );
    }

    private static void EnsureBlockRayProxy(
        Block block)
    {
        SecondaryRaftBlockRayProxy[] existing =
            block.GetComponentsInChildren
                <SecondaryRaftBlockRayProxy>(true);

        if (existing != null &&
            existing.Length > 0)
        {
            for (int i = 0;
                 i < existing.Length;
                 i++)
            {
                if (existing[i] != null)
                {
                    existing[i].gameObject.layer =
                        ResolveFallbackProxyLayer();
                }
            }

            return;
        }

        Bounds localBounds;

        if (!TryGetLocalBounds(
                block.transform,
                block.transform,
                out localBounds))
        {
            localBounds =
                new Bounds(
                    Vector3.zero,
                    new Vector3(
                        0.75f,
                        0.75f,
                        0.75f
                    )
                );
        }

        GameObject proxyObject =
            new GameObject(
                "SecondaryRaftBlockRayProxy"
            );

        proxyObject.layer =
            ResolveFallbackProxyLayer();

        proxyObject.transform.SetParent(
            block.transform,
            false
        );

        proxyObject.transform.localPosition =
            localBounds.center;

        proxyObject.transform.localRotation =
            Quaternion.identity;

        proxyObject.transform.localScale =
            Vector3.one;

        proxyObject.AddComponent
            <SecondaryRaftBlockRayProxy>();

        BoxCollider collider =
            proxyObject.AddComponent<BoxCollider>();

        collider.center =
            Vector3.zero;

        collider.size =
            ExpandSize(localBounds.size);

        collider.isTrigger =
            true;
    }

    private static bool HasRaycastCollider(
        RaycastInteractable interactable)
    {
        Collider[] colliders =
            interactable.GetComponentsInChildren
                <Collider>(true);

        int mask =
            LayerMasks.MASK_RaycastInteractable.value;

        for (int i = 0;
             i < colliders.Length;
             i++)
        {
            Collider collider =
                colliders[i];

            if (collider == null ||
                collider.GetComponent
                    <SecondaryRaftInteractionProxy>() !=
                    null ||
                collider.GetComponent
                    <SecondaryRaftBlockRayProxy>() !=
                    null)
            {
                continue;
            }

            int layerMask =
                1 << collider.gameObject.layer;

            if ((mask & layerMask) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetLocalBounds(
        Transform contentRoot,
        Transform targetSpace,
        out Bounds localBounds)
    {
        localBounds =
            new Bounds();

        bool initialized =
            false;

        Collider[] colliders =
            contentRoot.GetComponentsInChildren
                <Collider>(true);

        for (int i = 0;
             i < colliders.Length;
             i++)
        {
            Collider collider =
                colliders[i];

            if (collider == null ||
                collider.GetComponent
                    <SecondaryRaftInteractionProxy>() !=
                    null ||
                collider.GetComponent
                    <SecondaryRaftBlockRayProxy>() !=
                    null ||
                collider.GetComponent
                    <SecondaryRaftCollisionProxy>() !=
                    null)
            {
                continue;
            }

            EncapsulateWorldBounds(
                collider.bounds,
                targetSpace,
                ref localBounds,
                ref initialized
            );
        }

        if (initialized)
        {
            return true;
        }

        Renderer[] renderers =
            contentRoot.GetComponentsInChildren
                <Renderer>(true);

        for (int i = 0;
             i < renderers.Length;
             i++)
        {
            Renderer renderer =
                renderers[i];

            if (renderer == null ||
                renderer.GetType().Name == "ParticleSystemRenderer")
            {
                continue;
            }

            EncapsulateWorldBounds(
                renderer.bounds,
                targetSpace,
                ref localBounds,
                ref initialized
            );
        }

        return initialized;
    }

    private static void EncapsulateWorldBounds(
        Bounds worldBounds,
        Transform targetSpace,
        ref Bounds localBounds,
        ref bool initialized)
    {
        Vector3 min =
            worldBounds.min;

        Vector3 max =
            worldBounds.max;

        for (int x = 0; x < 2; x++)
        {
            for (int y = 0; y < 2; y++)
            {
                for (int z = 0; z < 2; z++)
                {
                    Vector3 worldPoint =
                        new Vector3(
                            x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z
                        );

                    Vector3 localPoint =
                        targetSpace.InverseTransformPoint(
                            worldPoint
                        );

                    if (!initialized)
                    {
                        localBounds =
                            new Bounds(
                                localPoint,
                                Vector3.zero
                            );

                        initialized =
                            true;
                    }
                    else
                    {
                        localBounds.Encapsulate(
                            localPoint
                        );
                    }
                }
            }
        }
    }

    private static Vector3 ExpandSize(
        Vector3 size)
    {
        size.x =
            Mathf.Max(
                size.x + ProxyPadding * 2f,
                MinimumProxySize
            );

        size.y =
            Mathf.Max(
                size.y + ProxyPadding * 2f,
                MinimumProxySize
            );

        size.z =
            Mathf.Max(
                size.z + ProxyPadding * 2f,
                MinimumProxySize
            );

        return size;
    }

    private static int ResolveRaycastInteractableLayer()
    {
        int mask =
            LayerMasks.MASK_RaycastInteractable.value;

        if (raycastInteractableLayer >= 0 &&
            (mask &
                (1 << raycastInteractableLayer)) != 0)
        {
            return raycastInteractableLayer;
        }

        for (int layer = 0;
             layer < 32;
             layer++)
        {
            if ((mask & (1 << layer)) != 0)
            {
                raycastInteractableLayer =
                    layer;

                return layer;
            }
        }

        raycastInteractableLayer =
            0;

        return raycastInteractableLayer;
    }

    private static int ResolveFallbackProxyLayer()
    {
        if (fallbackProxyLayer >= 0)
        {
            return fallbackProxyLayer;
        }

        int layer =
            LayerMask.NameToLayer(
                "Ignore Raycast"
            );

        fallbackProxyLayer =
            layer >= 0
                ? layer
                : 2;

        return fallbackProxyLayer;
    }

    private static Block GetSecondaryBlockFromHit(
        RaycastHit hit)
    {
        Block block = null;

        if (hit.collider != null)
        {
            block =
                hit.collider.GetComponentInParent<Block>();
        }

        if (block == null && hit.transform != null)
        {
            block =
                hit.transform.GetComponentInParent<Block>();
        }

        if (block == null ||
            !MultiRaftRegistry.IsSecondaryBlock(block))
        {
            return null;
        }

        return block;
    }

    private static RaycastInteractable
        FindInteractableOnRay(
            Ray ray,
            float distance,
            QueryTriggerInteraction triggerMode)
    {
        RaycastHit[] hits =
            Physics.RaycastAll(
                ray,
                distance,
                LayerMasks.MASK_RaycastInteractable,
                QueryTriggerInteraction.Collide
            );

        SortHits(hits);

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastInteractable interactable =
                ResolveInteractable(
                    hits[i].collider
                );

            if (interactable != null &&
                IsSecondaryInteractable(
                    interactable
                ))
            {
                return interactable;
            }
        }

        return null;
    }

    private static RaycastInteractable ResolveInteractable(
        Collider collider)
    {
        if (collider == null)
        {
            return null;
        }

        RaycastInteractable interactable =
            collider.GetComponent
                <RaycastInteractable>();

        if (interactable != null)
        {
            return interactable;
        }

        RaycastInteractable_Redirect redirect =
            collider.GetComponent
                <RaycastInteractable_Redirect>();

        if (redirect != null &&
            redirect.RaycastInteractable != null)
        {
            return redirect.RaycastInteractable;
        }

        interactable =
            collider.GetComponentInParent
                <RaycastInteractable>();

        if (interactable != null)
        {
            return interactable;
        }

        redirect =
            collider.GetComponentInParent
                <RaycastInteractable_Redirect>();

        if (redirect != null &&
            redirect.RaycastInteractable != null)
        {
            return redirect.RaycastInteractable;
        }

        Block block =
            collider.GetComponentInParent<Block>();

        if (block == null ||
            !MultiRaftRegistry
                .IsSecondaryBlock(block))
        {
            return null;
        }

        RaycastInteractable[] interactables =
            block.GetComponentsInChildren
                <RaycastInteractable>(true);

        RaycastInteractable single =
            null;

        for (int i = 0;
             i < interactables.Length;
             i++)
        {
            RaycastInteractable candidate =
                interactables[i];

            if (candidate == null)
            {
                continue;
            }

            if (single != null &&
                single != candidate)
            {
                return null;
            }

            single = candidate;
        }

        return single;
    }

    private static bool IsSecondaryInteractable(
        RaycastInteractable interactable)
    {
        if (interactable == null)
        {
            return false;
        }

        Block block =
            interactable.GetComponentInParent<Block>();

        if (block != null)
        {
            return MultiRaftRegistry
                .IsSecondaryBlock(block);
        }

        return MultiRaftRegistry
            .GetRootFromTransform(
                interactable.transform
            ) != null;
    }

    private static void SortHits(
        RaycastHit[] hits)
    {
        if (hits == null || hits.Length < 2)
        {
            return;
        }

        Array.Sort(
            hits,
            delegate (
                RaycastHit left,
                RaycastHit right)
            {
                return left.distance
                    .CompareTo(right.distance);
            }
        );
    }
}

[HarmonyPatch(
    typeof(Helper),
    "FindInteractable",
    new Type[]
    {
        typeof(float),
        typeof(QueryTriggerInteraction)
    }
)]
public static class Helper_FindInteractable_RebuildableRaft
{
    [HarmonyPostfix]
    public static void Postfix(
        float __0,
        QueryTriggerInteraction __1,
        ref RaycastInteractable __result)
    {
        if (__result != null)
        {
            return;
        }

        __result =
            SecondaryRaftInteraction
                .FindInteractable(
                    __0,
                    __1
                );
    }
}

[HarmonyPatch(typeof(RaycastInteractable), "InitRaycastables")]
public static class RaycastInteractable_Init_RebuildableRaft
{
    [HarmonyPostfix]
    public static void Postfix(
        RaycastInteractable __instance)
    {
        SecondaryRaftInteraction
            .ExpandRaycastables(__instance);
    }
}

[HarmonyPatch]
public static class Helper_HitAtCursor_RebuildableRaft
{
    private static MethodBase TargetMethod()
    {
        return AccessTools.Method(
            typeof(Helper),
            "HitAtCursor",
            new Type[]
            {
                typeof(RaycastHit)
                    .MakeByRefType(),
                typeof(float),
                typeof(LayerMask),
                typeof(QueryTriggerInteraction)
            }
        );
    }

    [HarmonyPostfix]
    public static void Postfix(
        ref RaycastHit __0,
        float __1,
        LayerMask __2,
        QueryTriggerInteraction __3,
        ref bool __result)
    {
        if (__result ||
            (__2.value &
                LayerMasks.MASK_Block.value) == 0)
        {
            return;
        }

        RaycastHit hit;

        if (SecondaryRaftInteraction
            .TryFindSecondaryBlockHit(
                out hit,
                __1,
                __3
            ))
        {
            __0 = hit;
            __result = true;
        }
    }
}

[HarmonyPatch]
public static class Helper_HitAllAtCursor_RebuildableRaft
{
    private static MethodBase TargetMethod()
    {
        return AccessTools.Method(
            typeof(Helper),
            "HitAllAtCursor",
            new Type[]
            {
                typeof(RaycastHit[])
                    .MakeByRefType(),
                typeof(float),
                typeof(LayerMask)
            }
        );
    }

    [HarmonyPostfix]
    public static void Postfix(
        ref RaycastHit[] __0,
        float __1,
        LayerMask __2,
        ref bool __result)
    {
        if ((__2.value &
                LayerMasks.MASK_Block.value) == 0)
        {
            return;
        }

        RaycastHit[] extra =
            SecondaryRaftInteraction
                .FindSecondaryBlockHits(__1);

        if (extra == null ||
            extra.Length == 0)
        {
            return;
        }

        List<RaycastHit> merged =
            new List<RaycastHit>();

        HashSet<Collider> added =
            new HashSet<Collider>();

        if (__0 != null)
        {
            for (int i = 0;
                 i < __0.Length;
                 i++)
            {
                if (__0[i].collider != null &&
                    added.Add(__0[i].collider))
                {
                    merged.Add(__0[i]);
                }
            }
        }

        for (int i = 0;
             i < extra.Length;
             i++)
        {
            if (extra[i].collider != null &&
                added.Add(extra[i].collider))
            {
                merged.Add(extra[i]);
            }
        }

        RaycastHit[] result =
            merged.ToArray();

        Array.Sort(
            result,
            delegate (
                RaycastHit left,
                RaycastHit right)
            {
                return left.distance
                    .CompareTo(right.distance);
            }
        );

        __0 = result;
        __result = result.Length > 0;
    }
}

[HarmonyPatch(typeof(ColliderPrefabEnabler), "DetachColliderPrefab")]
public static class ColliderPrefabEnabler_Detach_RebuildableRaft
{
    [HarmonyPrefix]
    public static bool Prefix(Block block)
    {
        if (block == null ||
            !MultiRaftRegistry.IsSecondaryBlock(block))
        {
            return true;
        }

        SecondaryRaftInteraction
            .EnsureInteractionCollider(block);

        return false;
    }
}

[HarmonyPatch(typeof(Block), "OnFinishedPlacement")]
public static class Block_OnFinishedPlacement_Interaction_RebuildableRaft
{
    [HarmonyPostfix]
    public static void Postfix(Block __instance)
    {
        SecondaryRaftInteraction
            .EnsureInteractionCollider(__instance);
    }
}


[HarmonyPatch(typeof(RemovePlaceables), "Update")]
public static class RemovePlaceables_Update_RebuildableRaft
{
    [HarmonyPrefix]
    public static bool Prefix(
        Network_Player ___playerNetwork,
        CanvasHelper ___canvas,
        ref Block ___currentBlock,
        ref bool ___showingText,
        InputActionReference ___inputRemove,
        ref float ___removeTimer,
        float ___removeTime)
    {
        if (___playerNetwork == null ||
            !___playerNetwork.IsLocalPlayer ||
            CanvasHelper.ActiveMenu != MenuType.None ||
            ChatTextFieldController.IsChatWindowSelected ||
            !___playerNetwork.PlayerItemManager.CanSwitch())
        {
            return true;
        }

        if (___currentBlock != null)
        {
            if (!MultiRaftRegistry
                    .IsSecondaryBlock(___currentBlock))
            {
                return true;
            }

            if (!SecondaryRaftInteraction
                    .IsCursorOverSecondaryBlock(
                        ___currentBlock,
                        5f
                    ))
            {
                ResetState(
                    ___canvas,
                    ref ___currentBlock,
                    ref ___showingText,
                    ref ___removeTimer
                );

                return false;
            }

            if (SimpleMonoBehaviourSingleton
                    <CustomInputConfig>.Instance
                    .IsPressed(
                        ___inputRemove,
                        "Remove"
                    ))
            {
                ___removeTimer +=
                    Application.isEditor
                        ? Time.deltaTime * 10f
                        : Time.deltaTime;
            }
            else if (SimpleMonoBehaviourSingleton
                        <CustomInputConfig>.Instance
                        .WasReleasedThisFrame(
                            ___inputRemove,
                            "Remove"
                        ))
            {
                ResetState(
                    ___canvas,
                    ref ___currentBlock,
                    ref ___showingText,
                    ref ___removeTimer
                );

                return false;
            }

            float removeTime =
                ___removeTime;

            if (___currentBlock.buildableItem != null &&
                (___currentBlock.buildableItem.UniqueIndex == 277 ||
                 ___currentBlock.buildableItem.UniqueIndex == 341))
            {
                removeTime = 1f;
            }

            float duration =
                removeTime /
                GameModeValueManager
                    .GetCurrentGameModeValue()
                    .toolVariables
                    .removeSpeedMultiplier;

            if (___removeTimer >= duration)
            {
                Block block =
                    ___currentBlock;

                if (RemovePlaceables
                    .ReturnItemsFromBlock(
                        block,
                        ___playerNetwork,
                        false
                    ))
                {
                    BlockCreator.RemoveBlockNetwork(
                        block,
                        ___playerNetwork,
                        true
                    );

                    AchievementHandler
                        .AddBuildRemoveCount(1);
                }

                ResetState(
                    ___canvas,
                    ref ___currentBlock,
                    ref ___showingText,
                    ref ___removeTimer
                );

                return false;
            }

            if (___canvas != null)
            {
                ___canvas.SetLoadCircle(
                    ___removeTimer > 0f
                );

                ___canvas.SetLoadCircle(
                    ___removeTimer / duration
                );
            }

            return false;
        }

        RaycastHit hit;

        Block target =
            SecondaryRaftInteraction
                .FindSecondaryRemovablePlaceableAtCursor(
                    5f,
                    out hit
                );

        if (target == null)
        {
            return true;
        }

        if (!Helper.LocalPlayerIsWithinDistance(
                hit.point,
                Player.UseDistance * 2f
            ))
        {
            if (___showingText)
            {
                ResetState(
                    ___canvas,
                    ref ___currentBlock,
                    ref ___showingText,
                    ref ___removeTimer
                );
            }

            return true;
        }

        if (___canvas != null &&
            ___inputRemove != null)
        {
            ___canvas.displayTextManager.ShowText(
                Helper.GetTerm(
                    "Game/Remove",
                    false
                ),
                ___inputRemove.action
                    .GetBindingDisplayString(
                        (InputBinding.DisplayStringOptions)0,
                        null
                    ),
                MyInput.Keybinds["Remove"].MainKey,
                3,
                0,
                false
            );

            ___showingText = true;
        }

        if (___inputRemove != null &&
            SimpleMonoBehaviourSingleton
                <CustomInputConfig>.Instance
                .WasPressedThisFrame(
                    ___inputRemove,
                    "Remove"
                ))
        {
            ___currentBlock =
                target;

            ___removeTimer =
                0f;
        }

        return false;
    }

    private static void ResetState(
        CanvasHelper canvas,
        ref Block currentBlock,
        ref bool showingText,
        ref float removeTimer)
    {
        removeTimer = 0f;
        currentBlock = null;
        showingText = false;

        if (canvas == null)
        {
            return;
        }

        canvas.SetLoadCircle(false);
        canvas.displayTextManager
            .HideDisplayTexts();
    }
}

[HarmonyPatch(typeof(Anchor_Throwable), "Update")]
public static class Anchor_Throwable_Update_RebuildableRaft
{
    [HarmonyPrefix]
    public static bool Prefix(
        Anchor_Throwable __instance,
        Network_Player ___playerNetwork,
        CanvasHelper ___canvas,
        InputActionReference ___inputInteract,
        ref bool ___isShowingText,
        ref float ___holdRemoveValue)
    {
        if (__instance == null ||
            ___playerNetwork == null ||
            !___playerNetwork.IsLocalPlayer ||
            __instance.anchor_stand != null ||
            CanvasHelper.ActiveMenu != MenuType.None ||
            PlayerItemManager.IsBusy)
        {
            return true;
        }

        Anchor_Throwable_Stand stand =
            SecondaryRaftInteraction
                .FindThrowableAnchorStand(
                    Player.UseDistance
                );

        if (stand == null)
        {
            return true;
        }

        if (stand.FullyAnchored)
        {
            if (___canvas == null ||
                ___inputInteract == null)
            {
                return false;
            }

            ___canvas.displayTextManager.ShowText(
                Helper.GetTerm("Game/Remove", false),
                ___inputInteract.action
                    .GetBindingDisplayString(
                        (InputBinding.DisplayStringOptions)0,
                        null
                    ),
                MyInput.Keybinds["Remove"].MainKey,
                0,
                0,
                true
            );

            ___isShowingText = true;

            if (SimpleMonoBehaviourSingleton
                    <CustomInputConfig>.Instance
                    .IsPressed(
                        ___inputInteract,
                        "Remove"
                    ))
            {
                ___holdRemoveValue +=
                    Time.deltaTime;

                ___canvas.SetLoadCircle(
                    ___holdRemoveValue
                );

                if (___holdRemoveValue >= 1f)
                {
                    ___canvas.SetLoadCircle(false);
                    ___canvas.displayTextManager
                        .HideDisplayTexts(0);

                    ___isShowingText = false;
                    ___holdRemoveValue = 0f;
                    stand.DestroyStand();
                }
            }
            else if (___holdRemoveValue > 0f)
            {
                ___holdRemoveValue = 0f;
                ___canvas.SetLoadCircle(false);
            }

            // Таймер сбрасывается ванильным Update при промахе raycast
            return false;
        }

        if (stand.IsBusy() ||
            stand.HasThrownAnchor)
        {
            if (___isShowingText &&
                ___canvas != null)
            {
                ___isShowingText = false;
                ___holdRemoveValue = 0f;
                ___canvas.SetLoadCircle(false);
                ___canvas.displayTextManager
                    .HideDisplayTexts(0);
            }

            return false;
        }

        if (___canvas == null ||
            ___inputInteract == null)
        {
            return false;
        }

        ___canvas.displayTextManager.ShowText(
            Helper.GetTerm("Game/PickUp", false),
            ___inputInteract.action
                .GetBindingDisplayString(
                    (InputBinding.DisplayStringOptions)0,
                    null
                ),
            MyInput.Keybinds["Interact"].MainKey,
            0,
            0,
            false
        );

        ___isShowingText = true;

        if (!SimpleMonoBehaviourSingleton
                <CustomInputConfig>.Instance
                .WasPressedThisFrame(
                    ___inputInteract,
                    "Interact"
                ))
        {
            return false;
        }

        ___canvas.displayTextManager
            .HideDisplayTexts();

        ___isShowingText = false;

        Message_NetworkBehaviour_SteamID message =
            new Message_NetworkBehaviour_SteamID(
                Messages.ConnectThrowableAnchorStand,
                stand,
                ___playerNetwork.Network
                    .GetIDFromPlayer(
                        ___playerNetwork
                    )
            );

        if (Raft_Network.IsHost)
        {
            ___playerNetwork.Network.RPC(
                message,
                Target.Other,
                EP2PSend
                    .k_EP2PSendReliable,
                NetworkChannel.Channel_Game
            );

            stand.ConnectStandWithThrowable(
                ___playerNetwork
            );

            return false;
        }

        ___playerNetwork.SendP2P(
            message,
            EP2PSend.k_EP2PSendReliable,
            NetworkChannel.Channel_Game
        );

        return false;
    }
}
