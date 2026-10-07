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

public static class SecondaryRaftWaterSampler
{
    private static Water water;
    private static WaterSample sample;

    public static bool TryGetSurfaceY(
        Vector3 worldPosition,
        float offset,
        out float surfaceY)
    {
        surfaceY = 0f;

        try
        {
            if (water == null)
            {
                GameManager gameManager =
                    SingletonGeneric<GameManager>.Singleton;

                if (gameManager == null ||
                    gameManager.water == null)
                {
                    return false;
                }

                water = gameManager.water;
            }

            if (sample == null)
            {
                sample =
                    new WaterSample(
                        water,
                        (WaterSample.DisplacementMode)0,
                        0.2f
                    );

                sample.Start(worldPosition);
            }

            Vector3 surface =
                sample.GetAndReset(
                    worldPosition.x,
                    worldPosition.z,
                    WaterSample.ComputationsMode
                        .ForceCompletion
                );

            surfaceY = surface.y + offset;

            return !float.IsNaN(surfaceY) &&
                   !float.IsInfinity(surfaceY);
        }
        catch
        {
            return false;
        }
    }

    public static void Reset()
    {
        if (sample != null)
        {
            try
            {
                sample.Stop();
            }
            catch
            {
            }
        }

        sample = null;
        water = null;
    }
}


public class SecondaryRaftCollisionRelay : MonoBehaviour
{
    private SecondaryRaftRoot root;

    public void Initialize(
        SecondaryRaftRoot raftRoot)
    {
        root = raftRoot;
    }

    private void OnCollisionEnter(
        Collision collision)
    {
        Report(collision);
    }

    private void OnCollisionStay(
        Collision collision)
    {
        Report(collision);
    }

    private void Report(
        Collision collision)
    {
        if (root == null ||
            collision == null)
        {
            return;
        }

        Rigidbody otherBody =
            collision.rigidbody;

        if (otherBody != null)
        {
            root.RegisterDynamicBodyContact(
                otherBody
            );
        }
    }
}

public class SecondaryRaftRoot : MonoBehaviour
{
    private const float DefaultWaterDriftSpeed = 1.5f;
    private const float DefaultMaxMovementSpeed = 1.5f;
    private const float DefaultMaxVelocity = 5f;
    private const float DefaultAccelerationSpeed = 2f;
    private const float DefaultDeAccelerationSpeed = 1f;
    private const float DefaultMaxSteeringTorque = 100f;
    private const float DefaultMaxDistanceFromAnchorPoint = 5f;
    private const float DeviceRefreshInterval = 0.25f;
    private const float EmptyDestroyDelay = 2f;
    private const float NetworkLerpSpeed = 8f;
    private const float WaterSurfaceOffset = 0.4f;
    private const float WaveHeightFollowSpeed = 5f;
    private const float WaveTiltFollowSpeed = 3f;
    private const float MinWaveSampleHalfExtent = 0.75f;

    private static readonly FieldInfo MainRaftBodyField =
        AccessTools.Field(typeof(Raft), "body");

    private static readonly FieldInfo MainRaftMaxAnchorDistanceField =
        AccessTools.Field(
            typeof(Raft),
            "maxDistanceFromAnchorPoint"
        );

    private static int cachedRaftGroundLayer = -1;

    private Sail[] sails =
        new Sail[0];

    private MotorWheel[] motors =
        new MotorWheel[0];

    private SteeringWheel[] steeringWheels =
        new SteeringWheel[0];

    private Block[] blocks =
        new Block[0];

    private Transform wavePivot;
    private Rigidbody collisionBody;
    private int lastWavePoseFrame = -1;

    private float waterDriftSpeed =
        DefaultWaterDriftSpeed;
    private float maxMovementSpeed =
        DefaultMaxMovementSpeed;
    private float maxVelocity =
        DefaultMaxVelocity;
    private float accelerationSpeed =
        DefaultAccelerationSpeed;
    private float deAccelerationSpeed =
        DefaultDeAccelerationSpeed;
    private float maxSteeringTorque =
        DefaultMaxSteeringTorque;
    private float maxDistanceFromAnchorPoint =
        DefaultMaxDistanceFromAnchorPoint;

    private readonly Dictionary<int, GameObject>
        anchorSources =
            new Dictionary<int, GameObject>();

    private Vector3 anchorPosition;

    private float deviceRefreshTimer;
    private float emptyTimer;
    private float driftSpeed;

    private Vector3 networkPosition;
    private float networkPitch;
    private float networkYaw;
    private float networkRoll;
    private Vector3 networkVelocity;
    private float networkAngularVelocityY;
    private bool hasNetworkState;

    private bool waveReferenceInitialized;
    private float mainWaveReferenceY;
    private float raftWaveReferenceY;

    private bool carryPoseInitialized;
    private bool carryingLocalPlayer;
    private Vector3 previousCarryPosition;
    private Quaternion previousCarryRotation;
    private int lastCarryFrame = -1;

    private sealed class DynamicBodyCarryState
    {
        public Rigidbody body;
        public int lastContactTick;
        public bool wasCarried;
        public Vector3 lastPlatformVelocity;
        public bool interpolationCaptured;
        public RigidbodyInterpolation originalInterpolation;
    }

    private readonly Dictionary<int, DynamicBodyCarryState>
        dynamicBodyCarryStates =
            new Dictionary<int, DynamicBodyCarryState>();

    private int fixedTick;
    private bool physicsCarryPoseInitialized;
    private Vector3 previousPhysicsCarryPosition;
    private Quaternion previousPhysicsCarryRotation;

    public int RaftId
    {
        get;
        private set;
    }

    public Rigidbody Body
    {
        get;
        private set;
    }

    public bool IsAnchored
    {
        get
        {
            return anchorSources.Count > 0;
        }
    }

    public int AnchorCount
    {
        get
        {
            return anchorSources.Count;
        }
    }

    public Transform BuildPivot
    {
        get
        {
            EnsureWavePivot();
            return wavePivot != null
                ? wavePivot
                : transform;
        }
    }

    public Quaternion WaveWorldRotation
    {
        get
        {
            Transform pivot = BuildPivot;
            return pivot != null
                ? pivot.rotation
                : transform.rotation;
        }
    }

    public bool HasPlacedBlocks
    {
        get
        {
            for (int i = 0;
                 i < blocks.Length;
                 i++)
            {
                Block block =
                    blocks[i];

                if (block != null &&
                    block.hasBeenPlaced &&
                    block.gameObject.activeSelf)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private void Awake()
    {
        Body =
            GetComponent<Rigidbody>();

        EnsureWavePivot();
    }

    public void Initialize(
        int raftId)
    {
        RaftId =
            raftId;

        Body =
            GetComponent<Rigidbody>();

        EnsureWavePivot();
        RefreshMainRaftSettings();

        if (Body != null)
        {
            Body.isKinematic =
                !Raft_Network.IsHost;
            Body.useGravity = false;
            Body.constraints =
                RigidbodyConstraints.FreezeRotationX |
                RigidbodyConstraints.FreezeRotationZ;
        }

        CaptureWaveReference();
        RefreshDevices();
        PrepareWavePoseForFrame();

        previousCarryPosition =
            BuildPivot.position;

        previousCarryRotation =
            BuildPivot.rotation;

        carryPoseInitialized =
            true;

        previousPhysicsCarryPosition =
            transform.position;

        previousPhysicsCarryRotation =
            transform.rotation;

        physicsCarryPoseInitialized =
            true;

        MultiRaftRegistry.Register(this);

        WorldShiftManager.OnWorldShift =
            (Action<Vector3>)Delegate.Combine(
                WorldShiftManager.OnWorldShift,
                new Action<Vector3>(
                    OnWorldShift
                )
            );
    }

    private void OnDestroy()
    {
        DetachPlayers();

        MultiRaftRegistry.Unregister(this);

        WorldShiftManager.OnWorldShift =
            (Action<Vector3>)Delegate.Remove(
                WorldShiftManager.OnWorldShift,
                new Action<Vector3>(
                    OnWorldShift
                )
            );
    }

    private void Update()
    {
        EnsureWavePivot();
        CleanupAnchorSources();

        if (!Raft_Network.IsHost &&
            Body != null &&
            !Body.isKinematic)
        {
            Body.velocity =
                Vector3.zero;

            Body.angularVelocity =
                Vector3.zero;

            Body.isKinematic =
                true;
        }

        if (Raft_Network.IsHost)
        {
            float targetDriftSpeed =
                IsAnchored
                    ? 0f
                    : waterDriftSpeed;

            float driftChangeSpeed =
                IsAnchored
                    ? deAccelerationSpeed
                    : accelerationSpeed;

            driftSpeed =
                Mathf.MoveTowards(
                    driftSpeed,
                    targetDriftSpeed,
                    Time.deltaTime *
                    driftChangeSpeed
                );

            PrepareWavePoseForFrame();
        }

        deviceRefreshTimer -=
            Time.deltaTime;

        if (deviceRefreshTimer <= 0f)
        {
            deviceRefreshTimer =
                DeviceRefreshInterval;

            RefreshDevices();
        }

        if (HasPlacedBlocks)
        {
            emptyTimer = 0f;
        }
        else
        {
            emptyTimer +=
                Time.deltaTime;

            if (emptyTimer >=
                EmptyDestroyDelay)
            {
                Destroy(gameObject);
                return;
            }
        }

        if (!Raft_Network.IsHost &&
            hasNetworkState)
        {
            float distance =
                Vector3.Distance(
                    transform.position,
                    networkPosition
                );

            Vector3 position =
                distance > 1.5f
                    ? networkPosition
                    : Vector3.Lerp(
                        transform.position,
                        networkPosition,
                        Time.deltaTime *
                        NetworkLerpSpeed
                    );

            Quaternion targetRotation =
                Quaternion.Euler(
                    0f,
                    networkYaw,
                    0f
                );

            float angle =
                Quaternion.Angle(
                    transform.rotation,
                    targetRotation
                );

            Quaternion rotation =
                angle > 20f
                    ? targetRotation
                    : Quaternion.Slerp(
                        transform.rotation,
                        targetRotation,
                        Time.deltaTime *
                        NetworkLerpSpeed
                    );

            transform.SetPositionAndRotation(
                position,
                rotation
            );

            if (Body != null)
            {
                Body.position =
                    position;

                Body.rotation =
                    rotation;
            }

            ApplyNetworkWaveTilt();
        }
    }

    private void EnsureWavePivot()
    {
        if (wavePivot == null)
        {
            Transform existing =
                transform.Find(
                    "SecondaryRaftWavePivot"
                );

            if (existing != null)
            {
                wavePivot = existing;
            }
            else
            {
                GameObject pivotObject =
                    new GameObject(
                        "SecondaryRaftWavePivot"
                    );

                wavePivot =
                    pivotObject.transform;

                wavePivot.SetParent(
                    transform,
                    false
                );

                wavePivot.localPosition =
                    Vector3.zero;

                wavePivot.localRotation =
                    Quaternion.identity;

                wavePivot.localScale =
                    Vector3.one;
            }
        }

        EnsureCollisionBody();
    }

    private void EnsureCollisionBody()
    {
        if (wavePivot == null)
        {
            return;
        }

        if (collisionBody == null)
        {
            collisionBody =
                wavePivot.GetComponent<Rigidbody>();

            if (collisionBody == null)
            {
                collisionBody =
                    wavePivot.gameObject
                        .AddComponent<Rigidbody>();
            }
        }

        collisionBody.useGravity = false;
        collisionBody.isKinematic = true;
        collisionBody.detectCollisions = true;
        collisionBody.interpolation =
            RigidbodyInterpolation.Interpolate;

        SecondaryRaftCollisionRelay relay =
            wavePivot.GetComponent
                <SecondaryRaftCollisionRelay>();

        if (relay == null)
        {
            relay =
                wavePivot.gameObject.AddComponent
                    <SecondaryRaftCollisionRelay>();
        }

        relay.Initialize(this);
    }

    private void RefreshMainRaftSettings()
    {
        Raft mainRaft =
            ComponentManager<Raft>.Value;

        if (mainRaft == null)
        {
            return;
        }

        waterDriftSpeed =
            mainRaft.waterDriftSpeed;

        maxMovementSpeed =
            mainRaft.maxSpeed;

        maxVelocity =
            mainRaft.maxVelocity;

        accelerationSpeed =
            mainRaft.accelerationSpeed;

        deAccelerationSpeed =
            mainRaft.deAccelerationSpeed;

        maxSteeringTorque =
            mainRaft.maxSteeringTorque;

        if (MainRaftMaxAnchorDistanceField != null)
        {
            object value =
                MainRaftMaxAnchorDistanceField
                    .GetValue(mainRaft);

            if (value is float)
            {
                maxDistanceFromAnchorPoint =
                    (float)value;
            }
        }

        if (Body == null ||
            MainRaftBodyField == null)
        {
            return;
        }

        Rigidbody mainBody =
            MainRaftBodyField.GetValue(
                mainRaft
            ) as Rigidbody;

        if (mainBody == null ||
            mainBody == Body)
        {
            return;
        }

        Body.mass =
            mainBody.mass;

        Body.drag =
            mainBody.drag;

        Body.angularDrag =
            mainBody.angularDrag;

        Body.interpolation =
            mainBody.interpolation;

        Body.collisionDetectionMode =
            mainBody.collisionDetectionMode;

        Body.maxAngularVelocity =
            mainBody.maxAngularVelocity;
    }

    private void PrepareWavePoseForFrame()
    {
        if (lastWavePoseFrame ==
            Time.frameCount)
        {
            return;
        }

        lastWavePoseFrame =
            Time.frameCount;

        EnsureWavePivot();

        if (wavePivot == null)
        {
            return;
        }

        if (Raft_Network.IsHost)
        {
            UpdateWaveTilt();
        }
        else if (hasNetworkState)
        {
            ApplyNetworkWaveTilt();
        }

        Physics.SyncTransforms();
    }

    private void UpdateWaveTilt()
    {
        if (wavePivot == null)
        {
            return;
        }

        if (!GameModeValueManager
                .GetCurrentGameModeValue()
                .raftSpecificVariables
                .usesBuoyancy)
        {
            wavePivot.localRotation =
                Quaternion.Slerp(
                    wavePivot.localRotation,
                    Quaternion.identity,
                    1f - Mathf.Exp(
                        -WaveTiltFollowSpeed *
                        Time.deltaTime
                    )
                );

            return;
        }

        Vector3 centerLocal;
        float halfX;
        float halfZ;

        if (!TryGetWaveBounds(
                out centerLocal,
                out halfX,
                out halfZ
            ))
        {
            return;
        }

        Vector3 right =
            transform.right;

        Vector3 forward =
            transform.forward;

        right.y = 0f;
        forward.y = 0f;

        if (right.sqrMagnitude < 0.0001f ||
            forward.sqrMagnitude < 0.0001f)
        {
            return;
        }

        right.Normalize();
        forward.Normalize();

        Vector3 centerWorld =
            transform.TransformPoint(
                new Vector3(
                    centerLocal.x,
                    0f,
                    centerLocal.z
                )
            );

        Vector3 pointL =
            centerWorld -
            right * halfX;

        Vector3 pointR =
            centerWorld +
            right * halfX;

        Vector3 pointB =
            centerWorld -
            forward * halfZ;

        Vector3 pointT =
            centerWorld +
            forward * halfZ;

        float yL;
        float yR;
        float yB;
        float yT;

        if (!SecondaryRaftWaterSampler
                .TryGetSurfaceY(
                    pointL,
                    0f,
                    out yL
                ) ||
            !SecondaryRaftWaterSampler
                .TryGetSurfaceY(
                    pointR,
                    0f,
                    out yR
                ) ||
            !SecondaryRaftWaterSampler
                .TryGetSurfaceY(
                    pointB,
                    0f,
                    out yB
                ) ||
            !SecondaryRaftWaterSampler
                .TryGetSurfaceY(
                    pointT,
                    0f,
                    out yT
                ))
        {
            return;
        }

        pointL.y = yL;
        pointR.y = yR;
        pointB.y = yB;
        pointT.y = yT;

        Vector3 longitudinal =
            pointT - pointB;

        Vector3 lateral =
            pointR - pointL;

        Vector3 up =
            Vector3.Cross(
                longitudinal,
                lateral
            );

        if (longitudinal.sqrMagnitude <
                0.0001f ||
            up.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Quaternion targetWorld =
            Quaternion.LookRotation(
                longitudinal,
                up
            );

        Vector3 targetEuler =
            targetWorld.eulerAngles;

        targetEuler.y =
            transform.eulerAngles.y;

        targetWorld =
            Quaternion.Euler(
                targetEuler
            );

        Quaternion targetLocal =
            Quaternion.Inverse(
                transform.rotation
            ) * targetWorld;

        float follow =
            1f - Mathf.Exp(
                -WaveTiltFollowSpeed *
                Time.deltaTime
            );

        wavePivot.localRotation =
            Quaternion.Slerp(
                wavePivot.localRotation,
                targetLocal,
                follow
            );
    }

    private void ApplyNetworkWaveTilt()
    {
        if (wavePivot == null ||
            !hasNetworkState)
        {
            return;
        }

        Quaternion targetWorld =
            Quaternion.Euler(
                networkPitch,
                networkYaw,
                networkRoll
            );

        wavePivot.rotation =
            Quaternion.Slerp(
                wavePivot.rotation,
                targetWorld,
                Time.deltaTime *
                NetworkLerpSpeed
            );
    }

    private bool TryGetWaveBounds(
        out Vector3 centerLocal,
        out float halfX,
        out float halfZ)
    {
        centerLocal =
            Vector3.zero;

        halfX =
            MinWaveSampleHalfExtent;

        halfZ =
            MinWaveSampleHalfExtent;

        Transform pivot =
            BuildPivot;

        if (pivot == null ||
            blocks == null ||
            blocks.Length == 0)
        {
            return false;
        }

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minZ = float.MaxValue;
        float maxZ = float.MinValue;
        bool found = false;

        for (int pass = 0;
             pass < 2 && !found;
             pass++)
        {
            for (int i = 0;
                 i < blocks.Length;
                 i++)
            {
                Block block =
                    blocks[i];

                if (block == null ||
                    block.buildableItem == null ||
                    !block.hasBeenPlaced ||
                    !block.gameObject.activeSelf)
                {
                    continue;
                }

                bool foundation =
                    Block.IsBlockIndexFoundation(
                        block.buildableItem
                            .UniqueIndex
                    );

                if (pass == 0 &&
                    !foundation)
                {
                    continue;
                }

                if (pass == 1 &&
                    !block.IsWalkable())
                {
                    continue;
                }

                Vector3 local =
                    pivot.InverseTransformPoint(
                        block.transform.position
                    );

                minX = Mathf.Min(
                    minX,
                    local.x - 0.75f
                );

                maxX = Mathf.Max(
                    maxX,
                    local.x + 0.75f
                );

                minZ = Mathf.Min(
                    minZ,
                    local.z - 0.75f
                );

                maxZ = Mathf.Max(
                    maxZ,
                    local.z + 0.75f
                );

                found = true;
            }
        }

        if (!found)
        {
            return false;
        }

        centerLocal =
            new Vector3(
                (minX + maxX) * 0.5f,
                0f,
                (minZ + maxZ) * 0.5f
            );

        halfX =
            Mathf.Max(
                (maxX - minX) * 0.5f,
                MinWaveSampleHalfExtent
            );

        halfZ =
            Mathf.Max(
                (maxZ - minZ) * 0.5f,
                MinWaveSampleHalfExtent
            );

        return true;
    }

    private void FixedUpdate()
    {
        fixedTick++;

        if (Raft_Network.IsHost)
        {
            CarrySupportedDynamicBodies();
        }

        if (!Raft_Network.IsHost ||
            Body == null ||
            !HasPlacedBlocks)
        {
            return;
        }

        Body.isKinematic =
            false;

        ApplyWaterHeightFixed();

        if (IsAnchored)
        {
            ApplyAnchorConstraintFixed();
            ClampBodyMotion();
            return;
        }

        Vector3 motorDirection =
            Vector3.zero;

        int motorStrength = 0;
        int extraMotorStrength = 0;
        float motorSpeed = 0f;

        for (int i = 0;
             i < motors.Length;
             i++)
        {
            MotorWheel motor =
                motors[i];

            if (motor == null)
            {
                continue;
            }

            motorDirection +=
                motor.PushDirection;

            motorStrength +=
                motor.MotorStrength;

            extraMotorStrength +=
                motor.ExtraMotorStrength;

            motorSpeed +=
                motor.RaftSpeed;
        }

        int foundationWeight =
            CountFoundations();

        bool motorStrong =
            motorDirection.sqrMagnitude >
                0.0001f &&
            foundationWeight <=
                motorStrength;

        bool motorWeak =
            !motorStrong &&
            motorDirection.sqrMagnitude >
                0.0001f &&
            foundationWeight <=
                motorStrength +
                extraMotorStrength;

        bool useMotor =
            motorStrong ||
            motorWeak;

        Vector3 moveDirection;
        float sailForwardBoost = 1f;

        if (useMotor)
        {
            moveDirection =
                motorDirection;

            if (motorWeak)
            {
                motorSpeed *= 0.5f;
            }
        }
        else
        {
            Vector3 oceanDirection =
                MultiRaftRegistry
                    .GetOceanDirection();

            if (oceanDirection.sqrMagnitude <
                0.0001f)
            {
                oceanDirection =
                    Vector3.forward;
            }

            oceanDirection.y = 0f;
            oceanDirection.Normalize();

            moveDirection =
                oceanDirection;

            Vector3 sailDirection =
                Vector3.zero;

            for (int i = 0;
                 i < sails.Length;
                 i++)
            {
                Sail sail =
                    sails[i];

                if (sail != null &&
                    sail.open)
                {
                    sailDirection +=
                        sail.GetNormalizedDirection();
                }
            }

            Vector3 oceanRight =
                Vector3.Cross(
                    Vector3.up,
                    oceanDirection
                ).normalized;

            float sailForward =
                Vector3.Dot(
                    sailDirection,
                    oceanDirection
                );

            float sailSide =
                Vector3.Dot(
                    sailDirection,
                    oceanRight
                );

            float currentForward = 1f;

            if (sailForward > 0f)
            {
                sailForwardBoost =
                    1f +
                    Mathf.Clamp01(
                        sailForward
                    );
            }

            // Ограничения паруса считаются относительно течения
            if (sailForward < 0f)
            {
                if (Mathf.Abs(sailSide) >
                    0.7f)
                {
                    sailForward = 0f;
                    currentForward = 0f;
                }
                else
                {
                    sailForward = -0.8f;
                }
            }

            moveDirection =
                oceanDirection *
                    (currentForward + sailForward) +
                oceanRight * sailSide;
        }

        float movementSpeed =
            driftSpeed;

        if (useMotor)
        {
            movementSpeed =
                motorSpeed;

            if (movementSpeed <
                driftSpeed)
            {
                movementSpeed =
                    driftSpeed;
            }
        }

        movementSpeed =
            Mathf.Min(
                movementSpeed,
                maxMovementSpeed
            );

        if (moveDirection.sqrMagnitude >
            0.0001f)
        {
            moveDirection =
                Vector3.ClampMagnitude(
                    moveDirection,
                    1f
                );

            Body.AddForce(
                moveDirection *
                movementSpeed *
                sailForwardBoost
            );
        }

        float steering = 0f;

        for (int i = 0;
             i < steeringWheels.Length;
             i++)
        {
            SteeringWheel steeringWheel =
                steeringWheels[i];

            if (steeringWheel != null)
            {
                steering +=
                    steeringWheel
                        .SteeringRotation;
            }
        }

        steering =
            Mathf.Clamp(
                steering,
                -1f,
                1f
            );

        if (steering != 0f)
        {
            Vector3 torque =
                new Vector3(
                    0f,
                    Mathf.Tan(
                        Mathf.Deg2Rad *
                        steering
                    ),
                    0f
                ) *
                maxSteeringTorque;

            Body.AddTorque(
                torque,
                ForceMode.Acceleration
            );
        }

        ClampBodyMotion();
    }

    private void ApplyAnchorConstraintFixed()
    {
        Vector3 toAnchor =
            anchorPosition -
            transform.position;

        toAnchor.y = 0f;

        float distance =
            toAnchor.magnitude;

        if (distance >=
            maxDistanceFromAnchorPoint * 3f)
        {
            anchorPosition =
                transform.position;

            return;
        }

        if (distance >
                maxDistanceFromAnchorPoint &&
            toAnchor.sqrMagnitude > 0.0001f)
        {
            Body.AddForce(
                toAnchor.normalized * 2f
            );
        }
    }

    private void ClampBodyMotion()
    {
        Vector3 velocity =
            Body.velocity;

        velocity.y = 0f;

        if (velocity.sqrMagnitude >
            maxVelocity *
            maxVelocity)
        {
            velocity =
                Vector3.ClampMagnitude(
                    velocity,
                    maxVelocity
                );
        }

        Body.velocity =
            velocity;

        Vector3 angularVelocity =
            Body.angularVelocity;

        angularVelocity.x = 0f;
        angularVelocity.z = 0f;

        Body.angularVelocity =
            angularVelocity;
    }

    public bool AddSecondaryAnchor(
        GameObject source)
    {
        if (source == null)
        {
            return false;
        }

        int id =
            source.GetInstanceID();

        if (anchorSources.ContainsKey(id))
        {
            return false;
        }

        anchorSources[id] =
            source;

        if (anchorSources.Count == 1)
        {
            anchorPosition =
                transform.position;
        }

        return true;
    }

    public bool RemoveSecondaryAnchor(
        GameObject source)
    {
        if (source == null)
        {
            return false;
        }

        return anchorSources.Remove(
            source.GetInstanceID()
        );
    }

    private void CleanupAnchorSources()
    {
        if (anchorSources.Count == 0)
        {
            return;
        }

        List<int> dead = null;

        foreach (KeyValuePair<int, GameObject> pair
            in anchorSources)
        {
            if (pair.Value != null)
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

        if (dead == null)
        {
            return;
        }

        for (int i = 0;
             i < dead.Count;
             i++)
        {
            anchorSources.Remove(
                dead[i]
            );
        }
    }

    private void LateUpdate()
    {
        CaptureCarryPose();
    }

    public void NotifyBlockChanged()
    {
        deviceRefreshTimer =
            DeviceRefreshInterval;

        RefreshDevices();
        emptyTimer = 0f;
    }

    private void RefreshDevices()
    {
        EnsureWavePivot();
        RefreshMainRaftSettings();

        sails =
            GetComponentsInChildren<Sail>(
                true
            );

        motors =
            GetComponentsInChildren
                <MotorWheel>(
                    true
                );

        steeringWheels =
            GetComponentsInChildren
                <SteeringWheel>(
                    true
                );

        blocks =
            GetComponentsInChildren<Block>(
                true
            );

        for (int i = 0;
             i < blocks.Length;
             i++)
        {
            SecondaryRaftInteraction
                .EnsureInteractionCollider(
                    blocks[i]
                );
        }

        if (wavePivot != null)
        {
            for (int i = 0;
                 i < blocks.Length;
                 i++)
            {
                Block block =
                    blocks[i];

                if (block != null &&
                    block.transform.parent ==
                        transform)
                {
                    block.transform.SetParent(
                        wavePivot,
                        true
                    );
                }
            }
        }

        RefreshCollisionProxies();
    }

    private void RefreshCollisionProxies()
    {
        for (int i = 0;
             i < blocks.Length;
             i++)
        {
            Block block =
                blocks[i];

            if (block == null ||
                !block.hasBeenPlaced ||
                !block.gameObject.activeSelf ||
                !block.IsWalkable())
            {
                continue;
            }

            DetachFromGlobalCollision(block);
            EnsureCollisionProxies(block);
            UpdateCollisionProxyState(block);
        }
    }

    private static void DetachFromGlobalCollision(
        Block block)
    {
        if (block == null)
        {
            return;
        }

        BlockCollisionConsolidator consolidator =
            ComponentManager<BlockCollisionConsolidator>.Value;

        if (consolidator != null)
        {
            consolidator.RemoveBlock(block);
        }

        BoxCollider[] colliders =
            block.blockColliders;

        if (colliders == null)
        {
            return;
        }

        for (int i = 0;
             i < colliders.Length;
             i++)
        {
            BoxCollider collider =
                colliders[i];

            if (collider != null)
            {
                collider.enabled = true;
            }
        }
    }

    private static void EnsureCollisionProxies(
        Block block)
    {
        int raftGroundLayer =
            ResolveRaftGroundLayer();

        SecondaryRaftCollisionProxy[] existing =
            block.GetComponentsInChildren
                <SecondaryRaftCollisionProxy>(
                    true
                );

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
                        raftGroundLayer;
                }
            }

            return;
        }

        int created = 0;

        BoxCollider[] boxes =
            block.GetComponentsInChildren
                <BoxCollider>(
                    true
                );

        for (int i = 0;
             i < boxes.Length;
             i++)
        {
            BoxCollider source =
                boxes[i];

            if (source == null ||
                source.isTrigger ||
                !IsRaftGroundLayer(
                    source.gameObject.layer
                ))
            {
                continue;
            }

            CreateCollisionProxy(
                block,
                source,
                raftGroundLayer
            );

            created++;
        }

        if (created == 0)
        {
            CreateWalkableCollisionProxy(
                block,
                raftGroundLayer
            );
        }
    }

    private static void CreateCollisionProxy(
        Block block,
        BoxCollider source,
        int raftGroundLayer)
    {
        GameObject proxyObject =
            new GameObject(
                "SecondaryRaftCollisionProxy"
            );

        proxyObject.layer =
            raftGroundLayer;

        proxyObject.transform.SetPositionAndRotation(
            source.transform.position,
            source.transform.rotation
        );

        proxyObject.transform.localScale =
            source.transform.lossyScale;

        proxyObject.transform.SetParent(
            block.transform,
            true
        );

        proxyObject.AddComponent
            <SecondaryRaftCollisionProxy>();

        BoxCollider proxy =
            proxyObject.AddComponent<BoxCollider>();

        proxy.center =
            source.center;

        proxy.size =
            source.size;

        proxy.sharedMaterial =
            source.sharedMaterial;

        proxy.enabled =
            false;
    }

    private static void CreateWalkableCollisionProxy(
        Block block,
        int raftGroundLayer)
    {
        GameObject proxyObject =
            new GameObject(
                "SecondaryRaftCollisionProxy"
            );

        proxyObject.layer =
            raftGroundLayer;

        proxyObject.transform.SetParent(
            block.transform,
            false
        );

        proxyObject.AddComponent
            <SecondaryRaftCollisionProxy>();

        BoxCollider proxy =
            proxyObject.AddComponent<BoxCollider>();

        float sizeY =
            Mathf.Abs(
                block.transform.localPosition.y
            ) < 0.1f
                ? 0.5f
                : 0.1f;

        proxy.center =
            new Vector3(
                0f,
                -sizeY * 0.5f,
                0f
            );

        proxy.size =
            new Vector3(
                1.5f,
                sizeY,
                1.5f
            );

        proxy.enabled =
            false;
    }

    private static void UpdateCollisionProxyState(
        Block block)
    {
        Collider[] colliders =
            block.GetComponentsInChildren
                <Collider>(
                    true
                );

        bool hasOriginalGroundCollider =
            false;

        for (int i = 0;
             i < colliders.Length;
             i++)
        {
            Collider collider =
                colliders[i];

            if (collider == null ||
                collider.GetComponent
                    <SecondaryRaftCollisionProxy>() !=
                    null)
            {
                continue;
            }

            if (collider.enabled &&
                collider.gameObject.activeInHierarchy &&
                !collider.isTrigger &&
                IsRaftGroundLayer(
                    collider.gameObject.layer
                ))
            {
                hasOriginalGroundCollider =
                    true;
                break;
            }
        }

        int raftGroundLayer =
            ResolveRaftGroundLayer();

        SecondaryRaftCollisionProxy[] proxies =
            block.GetComponentsInChildren
                <SecondaryRaftCollisionProxy>(
                    true
                );

        for (int i = 0;
             i < proxies.Length;
             i++)
        {
            SecondaryRaftCollisionProxy proxy =
                proxies[i];

            if (proxy == null)
            {
                continue;
            }

            proxy.gameObject.layer =
                raftGroundLayer;

            Collider collider =
                proxy.GetComponent<Collider>();

            if (collider != null)
            {
                collider.enabled =
                    !hasOriginalGroundCollider;
            }
        }
    }

    private static int ResolveRaftGroundLayer()
    {
        if (cachedRaftGroundLayer >= 0 &&
            IsRaftGroundLayer(
                cachedRaftGroundLayer
            ))
        {
            return cachedRaftGroundLayer;
        }

        RaftCollisionManager manager =
            ComponentManager
                <RaftCollisionManager>.Value;

        if (manager != null)
        {
            BoxCollider[] colliders =
                manager.GetComponentsInChildren
                    <BoxCollider>(
                        true
                    );

            for (int i = 0;
                 i < colliders.Length;
                 i++)
            {
                BoxCollider collider =
                    colliders[i];

                if (collider != null &&
                    IsRaftGroundLayer(
                        collider.gameObject.layer
                    ))
                {
                    cachedRaftGroundLayer =
                        collider.gameObject.layer;

                    return cachedRaftGroundLayer;
                }
            }
        }

        int mask =
            LayerMasks.MASK_GroundMask_Raft;

        for (int layer = 0;
             layer < 32;
             layer++)
        {
            if ((mask & (1 << layer)) != 0)
            {
                cachedRaftGroundLayer =
                    layer;

                return layer;
            }
        }

        cachedRaftGroundLayer =
            0;

        return cachedRaftGroundLayer;
    }

    private static bool IsRaftGroundLayer(
        int layer)
    {
        return ((1 << layer) &
            LayerMasks.MASK_GroundMask_Raft) != 0;
    }

    public float GetGroundPointScore(
        Vector3 worldPoint)
    {
        float bestScore =
            float.MaxValue;

        for (int i = 0;
             i < blocks.Length;
             i++)
        {
            Block block =
                blocks[i];

            if (block == null ||
                !block.hasBeenPlaced ||
                !block.gameObject.activeSelf ||
                !block.IsWalkable())
            {
                continue;
            }

            Vector3 localPoint =
                block.transform
                    .InverseTransformPoint(
                        worldPoint
                    );

            if (Mathf.Abs(localPoint.x) >
                    0.9f ||
                Mathf.Abs(localPoint.z) >
                    0.9f ||
                localPoint.y < -0.75f ||
                localPoint.y > 1.1f)
            {
                continue;
            }

            float score =
                Mathf.Abs(localPoint.y) +
                (Mathf.Abs(localPoint.x) +
                 Mathf.Abs(localPoint.z)) *
                0.01f;

            if (score < bestScore)
            {
                bestScore =
                    score;
            }
        }

        return bestScore;
    }

    private int CountFoundations()
    {
        int count = 0;

        for (int i = 0;
             i < blocks.Length;
             i++)
        {
            Block block =
                blocks[i];

            if (block == null ||
                block.buildableItem == null ||
                !block.hasBeenPlaced ||
                !block.gameObject.activeSelf)
            {
                continue;
            }

            if (Block.IsBlockIndexFoundation(
                    block.buildableItem
                        .UniqueIndex
                ))
            {
                count++;
            }
        }

        return count;
    }

    private void CaptureWaveReference()
    {
        Transform mainPivot =
            MultiRaftRegistry.MainPivot;

        if (mainPivot == null)
        {
            return;
        }

        mainWaveReferenceY =
            mainPivot.position.y;

        raftWaveReferenceY =
            transform.position.y;

        waveReferenceInitialized =
            true;
    }

    public void RegisterDynamicBodyContact(
        Rigidbody body)
    {
        if (!Raft_Network.IsHost ||
            body == null ||
            body == Body ||
            body == collisionBody ||
            body.isKinematic)
        {
            return;
        }

        if (body.GetComponent<DropItem>() != null ||
            body.GetComponentInParent<DropItem>() != null ||
            body.GetComponentInChildren<DropItem>() != null)
        {
            return;
        }

        int id =
            body.GetInstanceID();

        DynamicBodyCarryState state;

        if (!dynamicBodyCarryStates.TryGetValue(
                id,
                out state) ||
            state == null)
        {
            state =
                new DynamicBodyCarryState();

            state.body = body;

            dynamicBodyCarryStates[id] =
                state;
        }

        state.lastContactTick =
            fixedTick;
    }

    private void CarrySupportedDynamicBodies()
    {
        Transform carryPivot =
            transform;

        Vector3 currentPosition =
            carryPivot.position;

        Quaternion currentRotation =
            carryPivot.rotation;

        if (!physicsCarryPoseInitialized)
        {
            previousPhysicsCarryPosition =
                currentPosition;

            previousPhysicsCarryRotation =
                currentRotation;

            physicsCarryPoseInitialized =
                true;

            return;
        }

        float deltaTime =
            Mathf.Max(
                Time.fixedDeltaTime,
                0.0001f
            );

        Quaternion inversePreviousRotation =
            Quaternion.Inverse(
                previousPhysicsCarryRotation
            );

        Quaternion rotationDelta =
            currentRotation *
            inversePreviousRotation;

        List<int> dead =
            null;

        foreach (KeyValuePair<int, DynamicBodyCarryState> pair
            in dynamicBodyCarryStates)
        {
            DynamicBodyCarryState state =
                pair.Value;

            Rigidbody body =
                state != null
                    ? state.body
                    : null;

            if (body == null)
            {
                if (dead == null)
                {
                    dead =
                        new List<int>();
                }

                dead.Add(pair.Key);
                continue;
            }

            bool supported =
                state.lastContactTick >=
                fixedTick - 1;

            if (supported)
            {
                Vector3 previousLocalPosition =
                    inversePreviousRotation *
                    (
                        body.position -
                        previousPhysicsCarryPosition
                    );

                Vector3 targetPosition =
                    currentPosition +
                    currentRotation *
                    previousLocalPosition;

                Vector3 platformDelta =
                    targetPosition -
                    body.position;

                if (platformDelta.sqrMagnitude <
                    100f)
                {
                    if (!state.interpolationCaptured)
                    {
                        state.originalInterpolation =
                            body.interpolation;

                        state.interpolationCaptured =
                            true;
                    }

                    if (body.interpolation ==
                        RigidbodyInterpolation.None)
                    {
                        body.interpolation =
                            RigidbodyInterpolation.Interpolate;
                    }

                    body.MovePosition(
                        targetPosition
                    );

                    body.MoveRotation(
                        rotationDelta *
                        body.rotation
                    );

                    state.lastPlatformVelocity =
                        platformDelta /
                        deltaTime;

                    state.wasCarried =
                        true;
                }

                continue;
            }

            if (state.wasCarried)
            {
                body.velocity +=
                    state.lastPlatformVelocity;

                state.wasCarried =
                    false;
            }

            if (state.interpolationCaptured)
            {
                body.interpolation =
                    state.originalInterpolation;

                state.interpolationCaptured =
                    false;
            }

            if (fixedTick -
                state.lastContactTick > 2)
            {
                if (dead == null)
                {
                    dead =
                        new List<int>();
                }

                dead.Add(pair.Key);
            }
        }

        if (dead != null)
        {
            for (int i = 0;
                 i < dead.Count;
                 i++)
            {
                dynamicBodyCarryStates.Remove(
                    dead[i]
                );
            }
        }

        previousPhysicsCarryPosition =
            currentPosition;

        previousPhysicsCarryRotation =
            currentRotation;
    }

    private void ApplyWaterHeightFixed()
    {
        if (Body == null)
        {
            return;
        }

        Vector3 position =
            Body.position;

        float targetY;

        if (!SecondaryRaftWaterSampler
                .TryGetSurfaceY(
                    position,
                    WaterSurfaceOffset,
                    out targetY
                ))
        {
            GameManager gameManager =
                SingletonGeneric<GameManager>.Singleton;

            if (gameManager == null ||
                gameManager.buoyancy == null)
            {
                return;
            }

            targetY =
                gameManager.buoyancy.CenterPointY;
        }

        float follow =
            1f - Mathf.Exp(
                -WaveHeightFollowSpeed *
                Time.fixedDeltaTime
            );

        position.y =
            Mathf.Lerp(
                position.y,
                targetY,
                follow
            );

        Body.MovePosition(position);
    }

    public void CarryLocalPlayerBeforeUpdate(
        PersonController personController)
    {
        PrepareWavePoseForFrame();

        if (lastCarryFrame ==
            Time.frameCount)
        {
            return;
        }

        Network_Player player =
            ComponentManager<Network_Player>.Value;

        if (player == null ||
            !player.IsLocalPlayer ||
            personController == null ||
            player.PersonController !=
                personController)
        {
            carryingLocalPlayer = false;
            return;
        }

        SecondaryRaftRoot groundRoot =
            MultiRaftRegistry
                .FindRootAtGroundPoint(
                    player.FeetPosition
                );

        if (groundRoot != this)
        {
            carryingLocalPlayer = false;
            return;
        }

        lastCarryFrame =
            Time.frameCount;

        if (player.transform.parent != null)
        {
            player.transform.SetParent(
                null,
                true
            );
        }

        if (!carryPoseInitialized ||
            !carryingLocalPlayer)
        {
            carryingLocalPlayer = true;
            return;
        }

        Transform carryPivot =
            BuildPivot;

        if (carryPivot == null)
        {
            carryingLocalPlayer = false;
            return;
        }

        Quaternion inversePreviousRotation =
            Quaternion.Inverse(
                previousCarryRotation
            );

        Vector3 previousLocalPosition =
            inversePreviousRotation *
            (
                player.transform.position -
                previousCarryPosition
            );

        Vector3 targetPosition =
            carryPivot.position +
            carryPivot.rotation *
            previousLocalPosition;

        Vector3 delta =
            targetPosition -
            player.transform.position;

        if (personController.controller != null &&
            personController.controller.enabled)
        {
            personController.controller.Move(
                delta
            );
        }
        else
        {
            player.transform.position =
                targetPosition;
        }

        float yawDelta =
            Mathf.DeltaAngle(
                previousCarryRotation
                    .eulerAngles.y,
                carryPivot.rotation
                    .eulerAngles.y
            );

        if (Mathf.Abs(yawDelta) >
            0.0001f)
        {
            Vector3 euler =
                player.transform.eulerAngles;

            euler.y += yawDelta;

            player.transform.eulerAngles =
                euler;
        }
    }

    private void CaptureCarryPose()
    {
        Transform carryPivot =
            BuildPivot;

        if (carryPivot == null)
        {
            return;
        }

        previousCarryPosition =
            carryPivot.position;

        previousCarryRotation =
            carryPivot.rotation;

        carryPoseInitialized =
            true;
    }

    private void DetachPlayers()
    {
        PersonController[] players =
            GetComponentsInChildren
                <PersonController>(
                    true
                );

        Transform mainPivot =
            MultiRaftRegistry.MainPivot;

        for (int i = 0;
             i < players.Length;
             i++)
        {
            PersonController player =
                players[i];

            if (player == null)
            {
                continue;
            }

            player.transform.SetParent(
                mainPivot,
                true
            );
        }
    }

    private void OnWorldShift(
        Vector3 shift)
    {
        transform.position -=
            shift;

        previousCarryPosition -=
            shift;

        previousPhysicsCarryPosition -=
            shift;

        anchorPosition -=
            shift;

        networkPosition -=
            shift;
    }

    public Message_SecondaryRaftState
        CreateStateMessage()
    {
        return new Message_SecondaryRaftState(
            this
        );
    }

    public void ApplyNetworkState(
        Message_SecondaryRaftState state)
    {
        if (state == null)
        {
            return;
        }

        networkPosition =
            new Vector3(
                state.x,
                state.y,
                state.z
            );

        networkPitch =
            state.pitch;

        networkYaw =
            state.yaw;

        networkRoll =
            state.roll;

        networkVelocity =
            new Vector3(
                state.velocityX,
                0f,
                state.velocityZ
            );

        networkAngularVelocityY =
            state.angularVelocityY;

        bool firstState =
            !hasNetworkState;

        hasNetworkState =
            true;

        if (firstState &&
            !Raft_Network.IsHost)
        {
            Vector3 targetPosition =
                networkPosition;

            Quaternion targetRotation =
                Quaternion.Euler(
                    0f,
                    networkYaw,
                    0f
                );

            transform.SetPositionAndRotation(
                targetPosition,
                targetRotation
            );

            if (Body != null)
            {
                Body.position =
                    targetPosition;

                Body.rotation =
                    targetRotation;

                Body.velocity =
                    Vector3.zero;

                Body.angularVelocity =
                    Vector3.zero;

                Body.isKinematic =
                    true;
            }

            ApplyNetworkWaveTilt();

            previousCarryPosition =
                BuildPivot.position;

            previousCarryRotation =
                BuildPivot.rotation;

            previousPhysicsCarryPosition =
                transform.position;

            previousPhysicsCarryRotation =
                transform.rotation;

            carryPoseInitialized =
                true;

            physicsCarryPoseInitialized =
                true;
        }
    }
}
