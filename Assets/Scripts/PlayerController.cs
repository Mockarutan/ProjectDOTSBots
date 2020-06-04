using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Authoring;
using Unity.Physics.Systems;
using Unity.Transforms;
using UnityEditorInternal;

public class PlayerController : UnityEngine.MonoBehaviour, IConvertGameObjectToEntity
{
    public float MovementSpeed;
    public float RotationSpeedY;

    public float RTSCameraHeight;
    public float RTSCameraFrameWidth;
    public float RTSCameraSpeed;

    public UnityEngine.Camera Camera;
    public UnityEngine.Transform RotationRoot;
    public UnityEngine.Transform FocusPoint;
    public UnityEngine.Transform CameraPoint;

    public PhysicsCategoryTags GroundTag;

    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        dstManager.AddComponent<ThirdPersonMode>(entity);

        dstManager.AddComponentData(entity, new PlayerMovementState
        {
            MovementSpeed = MovementSpeed,
            RotationSpeedY = RotationSpeedY,
            RTSCamerHeight = RTSCameraHeight,
            RTSCameraFrameWidth = RTSCameraFrameWidth,
            RTSCameraSpeed = RTSCameraSpeed,

            RotationRoot = conversionSystem.GetPrimaryEntity(RotationRoot.gameObject),
            FocusPoint = conversionSystem.GetPrimaryEntity(FocusPoint.gameObject),
            CameraPoint = conversionSystem.GetPrimaryEntity(CameraPoint.gameObject),

            GroundFilter = new CollisionFilter
            {
                BelongsTo = CollisionFilter.Default.BelongsTo,
                CollidesWith = GroundTag.Value,
            },
        });

        dstManager.AddComponentData(entity, new PlayerCamera
        {
            Camera = Camera,
        });
    }
}

public struct PlayerMovementState : IComponentData
{
    public float MovementSpeed;
    public float RotationSpeedY;

    public float YawRotation;
    public float PitchRotation;

    public float RTSCamerHeight;
    public float RTSCameraFrameWidth;
    public float RTSCameraSpeed;

    public Entity RotationRoot;
    public Entity FocusPoint;
    public Entity CameraPoint;

    public CollisionFilter GroundFilter;
}
public struct ThirdPersonMode : IComponentData { }
public struct OverviewMode : IComponentData
{
    public float3 GroundPosition;
    public float3 CameraPosition;
}
public class PlayerCamera : IComponentData
{
    public UnityEngine.Camera Camera;
}

public struct FinishedTag : IComponentData { }

class PlayerControllerSystem : SystemBase
{
    private BuildPhysicsWorld _BuildPhysicsWorld;
    //private float2 _ViewportSize;

    protected override void OnCreate()
    {
        _BuildPhysicsWorld = World.GetOrCreateSystem<BuildPhysicsWorld>();
    }

    protected override void OnUpdate()
    {
        var commandBuffer = new EntityCommandBuffer(Allocator.TempJob);

        Entities.WithNone<FinishedTag>().ForEach((Entity entity, ref PlayerMovementState state, ref Rotation rot, ref PhysicsMass physicsMass) =>
        {
            physicsMass.InverseInertia = new float3(0, 1, 0);
            rot.Value = quaternion.identity;

            commandBuffer.AddComponent<FinishedTag>(entity);

        }).Run();

        commandBuffer.Playback(EntityManager);
        commandBuffer.Dispose();


        var mouseDelta = new float2(UnityEngine.Input.GetAxis("Mouse X"), UnityEngine.Input.GetAxis("Mouse Y"));

        var move = new float3();
        if (UnityEngine.Input.GetKey(UnityEngine.KeyCode.W))
            move.z += 1;
        if (UnityEngine.Input.GetKey(UnityEngine.KeyCode.S))
            move.z -= 1;
        if (UnityEngine.Input.GetKey(UnityEngine.KeyCode.A))
            move.x -= 1;
        if (UnityEngine.Input.GetKey(UnityEngine.KeyCode.D))
            move.x += 1;

        var switchCamera = UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Tab);

        move = math.normalizesafe(move);

        commandBuffer = new EntityCommandBuffer(Allocator.TempJob);

        Entities.WithAll<PlayerMovementState>().ForEach((ref Rotation rot) =>
        {
            rot.Value = quaternion.identity;

        }).Run();

        // Third person mode
        Entities.WithAll<ThirdPersonMode>().ForEach((Entity entity, ref PhysicsVelocity velocity, ref PlayerMovementState state, in LocalToWorld tans) =>
        {
            if (switchCamera)
            {
                commandBuffer.RemoveComponent<ThirdPersonMode>(entity);
                commandBuffer.AddComponent(entity, new OverviewMode
                {
                    GroundPosition = tans.Position,
                });
            }

            state.YawRotation += mouseDelta.x * state.RotationSpeedY;

            var yRot = quaternion.RotateY(state.YawRotation);

            commandBuffer.SetComponent(state.RotationRoot, new Rotation { Value = yRot });

            var viewRotation = GetComponent<Rotation>(state.RotationRoot);
            move = math.mul(viewRotation.Value, move);

            var yVelocity = velocity.Linear.y;
            velocity.Linear = move * state.MovementSpeed;
            velocity.Linear.y = yVelocity;

        }).Run();

        Entities.WithAll<ThirdPersonMode>().ForEach((PlayerCamera playerCamera, in PlayerMovementState state, in Rotation rot, in LocalToWorld trans) =>
        {
            var focusPointPosition = GetComponent<LocalToWorld>(state.FocusPoint).Position;
            var cameraPosition = GetComponent<LocalToWorld>(state.CameraPoint).Position;

            var camDirection = focusPointPosition - (float3)playerCamera.Camera.transform.position;

            playerCamera.Camera.transform.position = cameraPosition;
            playerCamera.Camera.transform.rotation = quaternion.LookRotationSafe(camDirection, new float3(0, 1, 0));

        }).WithoutBurst().Run();

        // Overview mode

        var deltaTime = Time.DeltaTime;
        var mousePosition = UnityEngine.Input.mousePosition;
        var viewPort = new float2(UnityEngine.Screen.width, UnityEngine.Screen.height);
        var collisionWorld = _BuildPhysicsWorld.PhysicsWorld.CollisionWorld;

        Entities.WithReadOnly(collisionWorld).ForEach((Entity entity, ref OverviewMode overview, in PlayerMovementState state) =>
        {
            if (switchCamera)
            {
                commandBuffer.AddComponent<ThirdPersonMode>(entity);
                commandBuffer.RemoveComponent<OverviewMode>(entity);
            }

            var normMouseX = mousePosition.x / viewPort.x;
            var normMouseY = mousePosition.y / viewPort.y;

            var rtsMove = new float3();

            if (normMouseX < state.RTSCameraFrameWidth)
            {
                var normMouseXInFrame = 1 - math.clamp(normMouseX / state.RTSCameraFrameWidth, 0, 1);
                rtsMove.x = -normMouseXInFrame;
            }
            else if (normMouseX > (1 - state.RTSCameraFrameWidth))
            {
                var normMouseXInFrame = 1 - math.clamp((1 - normMouseX) / state.RTSCameraFrameWidth, 0, 1);
                rtsMove.x = normMouseXInFrame;
            }

            if (normMouseY < state.RTSCameraFrameWidth)
            {
                var normMouseXInFrame = 1 - math.clamp(normMouseY / state.RTSCameraFrameWidth, 0, 1);
                rtsMove.z = -normMouseXInFrame;
            }
            else if (normMouseY > (1 - state.RTSCameraFrameWidth))
            {
                var normMouseXInFrame = 1 - math.clamp((1 - normMouseY) / state.RTSCameraFrameWidth, 0, 1);
                rtsMove.z = normMouseXInFrame;
            }

            overview.GroundPosition += rtsMove * state.RTSCameraSpeed * deltaTime;

            var start = overview.GroundPosition;
            var end = overview.GroundPosition;
            start = new float3(start.x, state.RTSCamerHeight, start.z);
            end = new float3(start.x, -state.RTSCamerHeight, start.z);

            if (collisionWorld.CastRay(new RaycastInput { Start = start, End = end, Filter = state.GroundFilter }, out var raycatHit))
            {
                overview.CameraPosition = raycatHit.Position + new float3(0, state.RTSCamerHeight, 0);
            }

        }).WithoutBurst().Run();

        Entities.ForEach((PlayerCamera playerCamera, in OverviewMode overview) =>
        {
            playerCamera.Camera.transform.position = overview.CameraPosition;
            playerCamera.Camera.transform.rotation = quaternion.LookRotation(new float3(0, -1, 0), new float3(0, 0, 1));

        }).WithoutBurst().Run();

        commandBuffer.Playback(EntityManager);
        commandBuffer.Dispose();
    }
}