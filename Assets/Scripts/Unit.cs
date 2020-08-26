using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Authoring;
using Unity.Physics.Systems;
using Unity.Rendering;
using Unity.Transforms;

public class Unit : UnityEngine.MonoBehaviour, IConvertGameObjectToEntity
{
    public UnityEngine.GameObject StatusIndicator;
    public float ProximityDistance;
    public float TargetSpeed;
    public float AvoidSpeed;

    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        var healthLight = conversionSystem.GetPrimaryEntity(StatusIndicator);

        dstManager.AddComponent<UnitStatusIndicatorMaterialProperties>(healthLight);

        dstManager.AddComponent<LockRotationXZ>(entity);
        dstManager.AddComponentData(entity, new UnitData
        {
            StatusIndicator = healthLight,
            ProximityDistance = ProximityDistance,
            TargetSpeed = TargetSpeed,
            AvoidSpeed = AvoidSpeed,
        });
    }
}

struct UnitData : IComponentData
{
    public Entity StatusIndicator;
    public Entity TargetEntity;
    public float3 TargetPosition;
    public bool HasTargetPosition;
    public bool HasTargetEntity;
    public bool Selected;
    public float ProximityDistance;
    public float TargetSpeed;
    public float AvoidSpeed;
}
struct TargetTag : IComponentData
{ }

[MaterialProperty("_Selected", MaterialPropertyFormat.Float)]
struct UnitStatusIndicatorMaterialProperties : IComponentData
{
    public float Selected;
}

[UpdateInGroup(typeof(InitializationSystemGroup))]
class UnitMovementPerpSystem : SystemBase
{
    public static readonly uint2 MapSize = new uint2(100, 100);
    public static readonly float BucketSize = 4;

    private EntityQuery _UnitQuery;

    protected override void OnCreate()
    {
        _UnitQuery = GetEntityQuery(ComponentType.ReadOnly<UnitData>());
    }

    protected override void OnUpdate()
    {
        var unitCount = _UnitQuery.CalculateEntityCount();
        var bucketCount = MapSize.x * MapSize.y;

        var buckets = new NativeMultiHashMap<uint, Entity>((int)bucketCount, Allocator.TempJob);
        World.GetOrCreateSystem<UnitMovementSystem>().SetBucketBuffer(buckets);

        var bucketsPW = buckets.AsParallelWriter();

        Entities.ForEach((Entity entity, in LocalToWorld trans, in UnitData data) =>
        {
            var postion2D = new float2(trans.Position.x, trans.Position.z);
            var bucketIndex = SpatialHelper.PositionToCellIndex(postion2D, MapSize, BucketSize, default);
            bucketsPW.Add(bucketIndex, entity);

        }).WithName("PROP_BOCKETS").ScheduleParallel();
    }
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
class UnitMovementSystem : SystemBase
{
    public SelectionUI Selection;

    private NativeMultiHashMap<uint, Entity> _Buckets;
    private BuildPhysicsWorld _BuildPhysicsWorld;
    private EndSimulationEntityCommandBufferSystem _ECBSystem;

    public void SetBucketBuffer(NativeMultiHashMap<uint, Entity> buckets)
    {
        if (_Buckets.IsCreated)
            _Buckets.Dispose();

        _Buckets = buckets;
    }

    protected override void OnCreate()
    {
        _BuildPhysicsWorld = World.GetOrCreateSystem<BuildPhysicsWorld>();
        _ECBSystem = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();
    }

    protected override void OnDestroy()
    {
        if (_Buckets.IsCreated)
            _Buckets.Dispose();
    }

    protected override void OnUpdate()
    {
        if (_Buckets.IsCreated)
        {
            UnityEngine.Camera camera = null;
            Entities.ForEach((in PlayerCamera playerCamera) =>
            {
                camera = playerCamera.Camera;

            }).WithoutBurst().Run();

            var buckets = _Buckets;
            var deltaTime = Time.DeltaTime;
            var transforms = GetComponentDataFromEntity<LocalToWorld>(true);

            var rightClickWorldPos = new float3();
            var rightClickGround = false;

            var targetClicked = false;
            var targetEntity = Entity.Null;

            if (Selection.RightClick.HasValue)
            {
                var rightClickRay = camera.ScreenPointToRay(Selection.RightClick.Value);

                var selectionData = GetSingleton<SelectionData>();
                var collisionWorld = _BuildPhysicsWorld.PhysicsWorld.CollisionWorld;
                if (collisionWorld.CastRay(new RaycastInput { Start = rightClickRay.origin, End = rightClickRay.GetPoint(1000), Filter = selectionData.TargetFilter }, out var hitInfo))
                {
                    if (HasComponent<TargetTag>(hitInfo.Entity))
                    {
                        targetClicked = true;
                        targetEntity = hitInfo.Entity;
                    }
                    else
                    {
                        rightClickGround = true;
                        rightClickWorldPos = hitInfo.Position;
                    }
                }
            }

            var commandBuffer = _ECBSystem.CreateCommandBuffer().ToConcurrent();

            Entities.WithReadOnly(buckets).WithReadOnly(transforms).ForEach((Entity entity, int entityInQueryIndex, ref UnitData data, ref Rotation rot, in LocalToWorld trans) =>
            {
                var postion2D = new float2(trans.Position.x, trans.Position.z);
                var bucketIndex = SpatialHelper.PositionToCellIndex(postion2D, UnitMovementPerpSystem.MapSize, UnitMovementPerpSystem.BucketSize, default);
                var proxDist = data.ProximityDistance * data.ProximityDistance;

                Entity closestEntity = Entity.Null;
                var closestDistSqrd = float.MaxValue;

                var right = bucketIndex + 1;
                var left = bucketIndex - 1;
                var up = bucketIndex - UnitMovementPerpSystem.MapSize.x;
                var down = bucketIndex + UnitMovementPerpSystem.MapSize.x;
                var leftUp = up - 1;
                var leftDown = down - 1;
                var rightUp = up + 1;
                var rightDown = down + 1;

                ProcessBucket(buckets, bucketIndex, entity, postion2D, proxDist, ref closestDistSqrd, ref closestEntity, transforms);
                ProcessBucket(buckets, right, entity, postion2D, proxDist, ref closestDistSqrd, ref closestEntity, transforms);
                ProcessBucket(buckets, left, entity, postion2D, proxDist, ref closestDistSqrd, ref closestEntity, transforms);
                ProcessBucket(buckets, up, entity, postion2D, proxDist, ref closestDistSqrd, ref closestEntity, transforms);
                ProcessBucket(buckets, down, entity, postion2D, proxDist, ref closestDistSqrd, ref closestEntity, transforms);
                ProcessBucket(buckets, leftUp, entity, postion2D, proxDist, ref closestDistSqrd, ref closestEntity, transforms);
                ProcessBucket(buckets, leftDown, entity, postion2D, proxDist, ref closestDistSqrd, ref closestEntity, transforms);
                ProcessBucket(buckets, rightUp, entity, postion2D, proxDist, ref closestDistSqrd, ref closestEntity, transforms);
                ProcessBucket(buckets, rightDown, entity, postion2D, proxDist, ref closestDistSqrd, ref closestEntity, transforms);

                if (data.Selected)
                {
                    if (targetClicked)
                    {
                        data.HasTargetPosition = false;
                        data.HasTargetEntity = true;
                        data.TargetEntity = targetEntity;
                    }
                    else if (rightClickGround)
                    {
                        data.HasTargetEntity = false;
                        data.HasTargetPosition = true;
                        data.TargetPosition = rightClickWorldPos;
                    }
                }

                var isLazerBot = HasComponent<LazerBot>(entity);
                if (data.HasTargetEntity)
                {
                    if (isLazerBot)
                    {
                        var lazerBot = GetComponent<LazerBot>(entity);
                        var targetTrans = GetComponent<LocalToWorld>(data.TargetEntity);
                        var lazerTrans = GetComponent<LocalToWorld>(lazerBot.Lazer);
                        var targetVec = targetTrans.Position - lazerTrans.Position;
                        var normTargetDir = math.normalize(targetVec);

                        var lazerBeamPos = GetComponent<Translation>(lazerBot.LazerBeam);
                        var lazerBeamScale = GetComponent<NonUniformScale>(lazerBot.LazerBeam);
                        lazerBeamPos.Value.z = math.length(targetVec) / 2;
                        lazerBeamScale.Value.y = lazerBeamPos.Value.z;

                        commandBuffer.SetComponent(entityInQueryIndex, lazerBot.LazerBeam, lazerBeamPos);
                        commandBuffer.SetComponent(entityInQueryIndex, lazerBot.LazerBeam, lazerBeamScale);

                        var lazerRot = quaternion.LookRotation(normTargetDir, new float3(0, 1, 0));
                        commandBuffer.SetComponent(entityInQueryIndex, lazerBot.Lazer, new Rotation { Value = math.mul(math.inverse(rot.Value), lazerRot) });

                        var flatDirection = MathUtilities.ProjectVectorOnPlane(new float3(0, 1, 0), normTargetDir);
                        var unitRot = quaternion.LookRotation(flatDirection, new float3(0, 1, 0));
                        rot.Value = unitRot;

                        if (HasComponent<Disabled>(lazerBot.LazerBeam))
                            commandBuffer.RemoveComponent<Disabled>(entityInQueryIndex, lazerBot.LazerBeam);
                    }
                }
                else
                {
                    if (isLazerBot)
                    {
                        var lazerBot = GetComponent<LazerBot>(entity);
                        if (HasComponent<Disabled>(lazerBot.LazerBeam) == false)
                            commandBuffer.AddComponent<Disabled>(entityInQueryIndex, lazerBot.LazerBeam);
                    }
                }

                var newPos = GetComponent<Translation>(entity);
                if (data.HasTargetPosition)
                {
                    var direction = math.normalize(data.TargetPosition - trans.Position);
                    if (math.lengthsq(direction) > 0.01f)
                    {
                        var movement = direction * data.TargetSpeed * deltaTime;
                        newPos.Value += movement;
                        postion2D += new float2(movement.x, movement.z);

                        var flatDirection = MathUtilities.ProjectVectorOnPlane(new float3(0, 1, 0), direction);
                        var unitRot = quaternion.LookRotation(flatDirection, new float3(0, 1, 0));
                        rot.Value = unitRot;

                        commandBuffer.SetComponent(entityInQueryIndex, entity, newPos);
                    }
                }

                //if (closestEntity != Entity.Null)
                //{
                //    var closestPosition = GetComponent<LocalToWorld>(closestEntity).Position;
                //    var closestPostion2D = new float2(closestPosition.x, closestPosition.z);

                //    var direction = math.normalize(postion2D - closestPostion2D);
                //    var avoid2D = direction * data.AvoidSpeed * deltaTime;
                //    var avoid3D = new float3(avoid2D.x, 0, avoid2D.y);
                //    newPos += avoid3D;
                //}

            }).WithName("PROXIMITY").ScheduleParallel();
        }
    }

    private static void ProcessBucket(NativeMultiHashMap<uint, Entity> buckets, uint bucketIndex, Entity entity, float2 postion2D, float proxDist, ref float closestDistSqrd, ref Entity closestEntity, ComponentDataFromEntity<LocalToWorld> transforms)
    {
        Entity closeEntity;
        if (buckets.TryGetFirstValue(bucketIndex, out closeEntity, out var iterator))
        {
            do
            {
                if (closeEntity != entity)
                {
                    var closePosition = transforms[closeEntity].Position;
                    var closePostion2D = new float2(closePosition.x, closePosition.z);
                    var distSqrd = math.distancesq(closePostion2D, postion2D);
                    if (distSqrd < proxDist && distSqrd < closestDistSqrd)
                    {
                        closestDistSqrd = proxDist;
                        closestEntity = closeEntity;
                    }
                }

            } while (buckets.TryGetNextValue(out closeEntity, ref iterator));
        }
    }
}