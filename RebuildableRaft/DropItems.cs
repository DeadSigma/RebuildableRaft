using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(DropItem), "OnEnable")]
public static class DropItem_OnEnable_RebuildableRaft
{
    [HarmonyPostfix]
    private static void Postfix(DropItem __instance)
    {
        if (__instance == null || __instance.transform == null)
        {
            return;
        }

        SecondaryRaftDropMotion motion =
            __instance.GetComponent<SecondaryRaftDropMotion>();

        if (motion == null)
        {
            motion = __instance.gameObject.AddComponent<SecondaryRaftDropMotion>();
        }

        Network_Player localPlayer = ComponentManager<Network_Player>.Value;
        SecondaryRaftRoot sourceRoot = null;

        if (localPlayer != null && localPlayer.IsLocalPlayer)
        {
            sourceRoot = MultiRaftRegistry.GetRootFromTransform(localPlayer.transform);

            if (sourceRoot != null &&
                (__instance.transform.position - localPlayer.transform.position).sqrMagnitude > 25f)
            {
                sourceRoot = null;
            }
        }

        motion.Initialize(sourceRoot);
    }
}

[HarmonyPatch(typeof(DropItem), "OnCollisionEnter")]
public static class DropItem_OnCollisionEnter_RebuildableRaft
{
    [HarmonyPrefix]
    private static bool Prefix(DropItem __instance, Collision collision)
    {
        if (__instance == null || collision == null)
        {
            return true;
        }

        SecondaryRaftRoot root = SecondaryRaftDropMotion.FindSecondaryRoot(collision);

        if (root == null)
        {
            return true;
        }

        SecondaryRaftDropMotion motion =
            __instance.GetComponent<SecondaryRaftDropMotion>();

        if (motion == null)
        {
            motion = __instance.gameObject.AddComponent<SecondaryRaftDropMotion>();
            motion.Initialize(null);
        }

        motion.RegisterContact(root, collision.collider);

        return false;
    }
}

[DefaultExecutionOrder(10000)]
public class SecondaryRaftDropMotion : MonoBehaviour
{
    private readonly HashSet<int> secondaryContacts = new HashSet<int>();

    private Rigidbody body;
    private SecondaryRaftRoot sourceRoot;
    private SecondaryRaftRoot groundedRoot;

    private bool inertiaApplied;
    private bool relativeFrameActive;
    private bool pivotPoseInitialized;
    private bool interpolationCaptured;
    private RigidbodyInterpolation originalInterpolation;

    private Vector3 previousPivotPosition;
    private Quaternion previousPivotRotation;
    private Vector3 lastPlatformVelocity;

    private int fixedTick;
    private int lastContactTick = -1000;

    public void Initialize(SecondaryRaftRoot root)
    {
        body = GetComponent<Rigidbody>();
        sourceRoot = root;
        inertiaApplied = false;
    }

    private void Start()
    {
        ApplyInitialInertia();
    }

    private void FixedUpdate()
    {
        fixedTick++;

        if (body == null)
        {
            body = GetComponent<Rigidbody>();

            if (body == null)
            {
                return;
            }
        }

        if (!inertiaApplied)
        {
            ApplyInitialInertia();
        }

        if (groundedRoot == null || !relativeFrameActive)
        {
            return;
        }

        if (fixedTick - lastContactTick > 2)
        {
            ReleaseFromSecondaryRaft();
        }
    }

    private void LateUpdate()
    {
        if (body == null ||
            groundedRoot == null ||
            !relativeFrameActive)
        {
            return;
        }

        Transform pivot = groundedRoot.BuildPivot;

        if (pivot == null)
        {
            ReleaseFromSecondaryRaft();
            return;
        }

        Vector3 currentPivotPosition = pivot.position;
        Quaternion currentPivotRotation = pivot.rotation;

        if (!pivotPoseInitialized)
        {
            previousPivotPosition = currentPivotPosition;
            previousPivotRotation = currentPivotRotation;
            pivotPoseInitialized = true;
            return;
        }

        Vector3 localPoint =
            Quaternion.Inverse(previousPivotRotation) *
            (body.position - previousPivotPosition);

        Vector3 targetPosition =
            currentPivotPosition +
            currentPivotRotation * localPoint;

        Vector3 platformDelta =
            targetPosition - body.position;

        if (platformDelta.sqrMagnitude < 4f)
        {
            // Физика и Transform синхронизируются в одном кадре
            body.position = targetPosition;
            transform.position = targetPosition;

            lastPlatformVelocity =
                platformDelta /
                Mathf.Max(Time.deltaTime, 0.0001f);
        }
        else
        {
            lastPlatformVelocity =
                GetPlatformVelocity(groundedRoot);
        }

        previousPivotPosition = currentPivotPosition;
        previousPivotRotation = currentPivotRotation;
    }

    private void OnCollisionEnter(Collision collision)
    {
        SecondaryRaftRoot root = FindSecondaryRoot(collision);

        if (root != null)
        {
            RegisterContact(root, collision.collider);
        }
    }

    private void OnCollisionStay(Collision collision)
    {
        SecondaryRaftRoot root = FindSecondaryRoot(collision);

        if (root != null)
        {
            RegisterContact(root, collision.collider);
        }
    }

    private void OnCollisionExit(Collision collision)
    {
        if (collision == null || collision.collider == null)
        {
            return;
        }

        secondaryContacts.Remove(collision.collider.GetInstanceID());

        if (secondaryContacts.Count == 0)
        {
            ReleaseFromSecondaryRaft();
        }
    }

    public void RegisterContact(SecondaryRaftRoot root, Collider collider)
    {
        if (root == null || body == null || body.isKinematic)
        {
            return;
        }

        if (collider != null)
        {
            secondaryContacts.Add(collider.GetInstanceID());
        }

        lastContactTick = fixedTick;

        if (groundedRoot == root && relativeFrameActive)
        {
            return;
        }

        if (relativeFrameActive)
        {
            ReleaseFromSecondaryRaft();
        }

        groundedRoot = root;
        secondaryContacts.Clear();

        if (collider != null)
        {
            secondaryContacts.Add(collider.GetInstanceID());
        }

        if (transform.parent != null)
        {
            transform.SetParent(null, true);
        }

        Vector3 platformVelocity = GetPlatformVelocity(root);

        body.velocity -= platformVelocity;
        lastPlatformVelocity = platformVelocity;

        if (!interpolationCaptured)
        {
            originalInterpolation = body.interpolation;
            interpolationCaptured = true;
        }

        // Интерполяция отключается - позиция переносится каждый кадр вместе с плотом
        body.interpolation = RigidbodyInterpolation.None;

        relativeFrameActive = true;
        CapturePivotPose(root);
    }

    private void ApplyInitialInertia()
    {
        if (inertiaApplied)
        {
            return;
        }

        inertiaApplied = true;

        if (sourceRoot == null || body == null || body.isKinematic)
        {
            return;
        }

        body.velocity += GetPlatformVelocity(sourceRoot);
    }

    private void CapturePivotPose(SecondaryRaftRoot root)
    {
        Transform pivot = root != null ? root.BuildPivot : null;

        if (pivot == null)
        {
            pivotPoseInitialized = false;
            return;
        }

        previousPivotPosition = pivot.position;
        previousPivotRotation = pivot.rotation;
        pivotPoseInitialized = true;
    }

    private void ReleaseFromSecondaryRaft()
    {
        if (relativeFrameActive && body != null)
        {
            body.velocity += lastPlatformVelocity;
        }

        if (interpolationCaptured && body != null)
        {
            body.interpolation = originalInterpolation;
            interpolationCaptured = false;
        }

        relativeFrameActive = false;
        groundedRoot = null;
        secondaryContacts.Clear();
        pivotPoseInitialized = false;
    }

    private Vector3 GetPlatformVelocity(SecondaryRaftRoot root)
    {
        if (root == null || root.Body == null || body == null)
        {
            return Vector3.zero;
        }

        return root.Body.GetPointVelocity(body.worldCenterOfMass);
    }

    public static SecondaryRaftRoot FindSecondaryRoot(Collision collision)
    {
        if (collision == null)
        {
            return null;
        }

        Collider hitCollider = collision.collider;
        Block block = null;

        if (hitCollider != null)
        {
            block = hitCollider.GetComponentInParent<Block>();
        }

        if (block == null && collision.gameObject != null)
        {
            block = collision.gameObject.GetComponentInParent<Block>();
        }

        return MultiRaftRegistry.GetRootFromBlock(block);
    }
}
