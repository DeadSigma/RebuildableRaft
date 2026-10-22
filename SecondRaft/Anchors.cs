using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class SecondaryAnchorRouting
{
    [ThreadStatic]
    private static Component currentAnchor;

    public static Component Push(
        Component anchor)
    {
        Component previous =
            currentAnchor;

        currentAnchor = anchor;

        return previous;
    }

    public static void Pop(
        Component previous)
    {
        currentAnchor = previous;
    }

    private static SecondaryRaftRoot
        FindRoot(
            GameObject soundParent)
    {
        SecondaryRaftRoot root = null;

        if (soundParent != null)
        {
            root =
                MultiRaftRegistry
                    .GetRootFromTransform(
                        soundParent.transform
                    );
        }

        if (root == null &&
            currentAnchor != null)
        {
            root =
                MultiRaftRegistry
                    .GetRootFromTransform(
                        currentAnchor.transform
                    );
        }

        return root;
    }

    private static GameObject
        FindSource(
            SecondaryRaftRoot root,
            GameObject soundParent)
    {
        if (root == null)
        {
            return null;
        }

        if (currentAnchor != null &&
            MultiRaftRegistry
                .GetRootFromTransform(
                    currentAnchor.transform
                ) == root)
        {
            return currentAnchor.gameObject;
        }

        if (soundParent != null &&
            MultiRaftRegistry
                .GetRootFromTransform(
                    soundParent.transform
                ) == root)
        {
            return soundParent;
        }

        return null;
    }

    public static bool TryAddAnchor(
        Raft mainRaft,
        bool playSound,
        GameObject soundParent)
    {
        SecondaryRaftRoot root =
            FindRoot(soundParent);

        if (root == null)
        {
            return false;
        }

        GameObject source =
            FindSource(
                root,
                soundParent
            );

        if (source == null)
        {
            return false;
        }

        bool added =
            root.AddSecondaryAnchor(
                source
            );

        return true;
    }

    public static bool TryRemoveAnchor()
    {
        if (currentAnchor == null)
        {
            return false;
        }

        SecondaryRaftRoot root =
            MultiRaftRegistry
                .GetRootFromTransform(
                    currentAnchor.transform
                );

        if (root == null)
        {
            return false;
        }

        root.RemoveSecondaryAnchor(
            currentAnchor.gameObject
        );

        return true;
    }
}

[HarmonyPatch]
public static class SecondaryAnchor_Context_RebuildableRaft
{
    public static IEnumerable<MethodBase>
        TargetMethods()
    {
        string[] typeNames =
        {
            "Anchor_Stationary",
            "Anchor_Throwable_Stand"
        };

        for (int i = 0;
             i < typeNames.Length;
             i++)
        {
            Type type =
                AccessTools.TypeByName(
                    typeNames[i]
                );

            if (type == null)
            {
                continue;
            }

            MethodInfo[] methods =
                type.GetMethods(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly
                );

            for (int j = 0;
                 j < methods.Length;
                 j++)
            {
                MethodInfo method =
                    methods[j];

                if (method == null ||
                    method.IsAbstract ||
                    method.ContainsGenericParameters)
                {
                    continue;
                }

                yield return method;
            }
        }
    }

    [HarmonyPrefix]
    public static void Prefix(
        object __instance,
        out Component __state)
    {
        __state =
            SecondaryAnchorRouting.Push(
                __instance as Component
            );
    }

    [HarmonyFinalizer]
    public static Exception Finalizer(
        Exception __exception,
        Component __state)
    {
        SecondaryAnchorRouting.Pop(
            __state
        );

        return __exception;
    }
}

[HarmonyPatch(typeof(Raft), "AddAnchor")]
public static class Raft_AddAnchor_RebuildableRaft
{
    [HarmonyPrefix]
    public static bool Prefix(
        Raft __instance,
        bool playSound,
        GameObject soundParent)
    {
        return !SecondaryAnchorRouting
            .TryAddAnchor(
                __instance,
                playSound,
                soundParent
            );
    }
}

[HarmonyPatch(typeof(Raft), "RemoveAnchor")]
public static class Raft_RemoveAnchor_RebuildableRaft
{
    [HarmonyPrefix]
    public static bool Prefix()
    {
        return !SecondaryAnchorRouting
            .TryRemoveAnchor();
    }
}
