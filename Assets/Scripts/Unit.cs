using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

public class Unit : MonoBehaviour, IConvertGameObjectToEntity
{
    public GameObject StatusIndicator;
    public float ProximityDistance;
    public float TargetSpeed;
    public float AvoidSpeed;

    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        var healthLight = conversionSystem.GetPrimaryEntity(StatusIndicator);

        dstManager.AddComponent<UnitStatusIndicatorMaterialProperties>(healthLight);

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
    public bool Selected;
    public float ProximityDistance;
    public float TargetSpeed;
    public float AvoidSpeed;
}

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

    public void SetBucketBuffer(NativeMultiHashMap<uint, Entity> buckets)
    {
        if (_Buckets.IsCreated)
            _Buckets.Dispose();

        _Buckets = buckets;
    }

    protected override void OnCreate()
    {
        _BuildPhysicsWorld = World.GetOrCreateSystem<BuildPhysicsWorld>();
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
            Camera camera = null;
            Entities.ForEach((in PlayerCamera playerCamera) =>
            {
                camera = playerCamera.Camera;

            }).WithoutBurst().Run();

            var buckets = _Buckets;
            var deltaTime = Time.DeltaTime;
            var transforms = GetComponentDataFromEntity<LocalToWorld>(true);

            var rightClickRay = camera.ScreenPointToRay(Selection.RightClick);
            var rightClickWorldPos = new float3();

            var collisionWorld = _BuildPhysicsWorld.PhysicsWorld.CollisionWorld;
            var groundHit = collisionWorld.CastRay(new RaycastInput { Start = rightClickRay.origin, End = rightClickRay.GetPoint(1000), Filter = CollisionFilter.Default }, out var hitInfo);

            rightClickWorldPos = hitInfo.Position;

            Entities.WithReadOnly(buckets).WithReadOnly(transforms).ForEach((Entity entity, ref Translation pos, in LocalToWorld trans, in UnitData data) =>
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
                var rightDown = down - 1;

                ProcessBucket(buckets, bucketIndex, entity, postion2D, proxDist, ref closestDistSqrd, ref closestEntity, transforms);
                ProcessBucket(buckets, right, entity, postion2D, proxDist, ref closestDistSqrd, ref closestEntity, transforms);
                ProcessBucket(buckets, left, entity, postion2D, proxDist, ref closestDistSqrd, ref closestEntity, transforms);
                ProcessBucket(buckets, up, entity, postion2D, proxDist, ref closestDistSqrd, ref closestEntity, transforms);
                ProcessBucket(buckets, down, entity, postion2D, proxDist, ref closestDistSqrd, ref closestEntity, transforms);
                ProcessBucket(buckets, leftUp, entity, postion2D, proxDist, ref closestDistSqrd, ref closestEntity, transforms);
                ProcessBucket(buckets, leftDown, entity, postion2D, proxDist, ref closestDistSqrd, ref closestEntity, transforms);
                ProcessBucket(buckets, rightUp, entity, postion2D, proxDist, ref closestDistSqrd, ref closestEntity, transforms);
                ProcessBucket(buckets, rightDown, entity, postion2D, proxDist, ref closestDistSqrd, ref closestEntity, transforms);

                if (data.Selected && groundHit)
                {
                    var direction = math.normalize(rightClickWorldPos - trans.Position);
                    pos.Value += direction * data.TargetSpeed * deltaTime;
                }

                if (closestEntity != Entity.Null)
                {
                    var closestPosition = GetComponent<LocalToWorld>(closestEntity).Position;
                    var closestPostion2D = new float2(closestPosition.x, closestPosition.z);

                    var direction = math.normalize(postion2D - closestPostion2D);
                    var avoid2D = direction * data.AvoidSpeed * deltaTime;
                    var avoid3D = new float3(avoid2D.x, 0, avoid2D.y);
                    pos.Value += avoid3D;
                }

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