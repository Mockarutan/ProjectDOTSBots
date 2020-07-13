using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Authoring;
using Unity.Physics.Systems;
using Unity.Transforms;

[UnityEngine.RequireComponent(typeof(UnityEngine.BoxCollider))]
public class DebugSpawner : UnityEngine.MonoBehaviour, IConvertGameObjectToEntity
{
    public UnityEngine.RectTransform TestPoint;

    public int Count;
    public float Height;
    public UnityEngine.GameObject ObjectToSpawn;
    public PhysicsCategoryTags GroundTag;

    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        dstManager.World.GetOrCreateSystem<SelectionSystemDataPrep>().TestPoint = TestPoint;

        var convSettings = GameObjectConversionSettings.FromWorld(dstManager.World, conversionSystem.BlobAssetStore);
        var prefab = GameObjectConversionUtility.ConvertGameObjectHierarchy(ObjectToSpawn, convSettings);

        var box = GetComponent<UnityEngine.BoxCollider>();
        var min = (transform.position + box.center) - box.size;
        var max = (transform.position + box.center) + box.size;

        var random = new System.Random();

        var buffer = new byte[4];
        random.NextBytes(buffer);
        var seed = System.BitConverter.ToUInt32(buffer, 0);

        var fastRandom = new Unity.Mathematics.Random(seed);

        var groundFilter = new CollisionFilter
        {
            BelongsTo = CollisionFilter.Default.BelongsTo,
            CollidesWith = GroundTag.Value,
        };

        var spawnerEntity = dstManager.CreateEntity();
        dstManager.AddComponentData(spawnerEntity, new SpawnData
        {
            Count = Count,
            Prefab = prefab,
            Random = fastRandom,
            Min = min,
            Max = max,
            GroundFilter = groundFilter,
            Height = Height,
        });

        //for (int i = 0; i < entities.Length; i++)
        //{
        //    var x = fastRandom.NextFloat(min.x, max.x);
        //    var z = fastRandom.NextFloat(min.z, max.z);
            
        //    var start = new float3(x, Height, z);
        //    var end = new float3(x, -Height, z);

        //    if (collisionWorld.CastRay(new RaycastInput { Start = start, End = end, Filter = groundFilter }, out var hitInfo))
        //    {
        //        dstManager.SetComponentData(entities[i], new Translation { Value = hitInfo.Position });
        //    }
        //    else
        //        UnityEngine.Debug.LogError("NO GROUND!");
        //}
    }
}

struct SpawnData : IComponentData
{
    public int Count;
    public Entity Prefab;
    public Random Random;

    public float3 Min;
    public float3 Max;
    public float Height;
    public CollisionFilter GroundFilter;
}

[UpdateAfter(typeof(BuildPhysicsWorld))]
class SpawnSystem : SystemBase
{
    private BuildPhysicsWorld _BuildPhysicsWorld;

    private EntityQuery _Query;

    protected override void OnCreate()
    {
        _BuildPhysicsWorld = World.GetOrCreateSystem<BuildPhysicsWorld>();
        _Query = GetEntityQuery(typeof(SpawnData));
    }

    protected override void OnUpdate()
    {
        var collisionWorld = _BuildPhysicsWorld.PhysicsWorld.CollisionWorld;

        Entities.ForEach((in SpawnData data) =>
        {
            var entities = new NativeArray<Entity>(data.Count, Allocator.TempJob);
            EntityManager.Instantiate(data.Prefab, entities);

            var random = data.Random;
            for (int i = 0; i < entities.Length; i++)
            {
                var x = random.NextFloat(data.Min.x, data.Max.x);
                var z = random.NextFloat(data.Min.z, data.Max.z);

                var start = new float3(x, data.Height, z);
                var end = new float3(x, -data.Height, z);

                if (collisionWorld.CastRay(new RaycastInput { Start = start, End = end, Filter = data.GroundFilter }, out var hitInfo))
                {
                    EntityManager.SetComponentData(entities[i], new Translation { Value = hitInfo.Position });
                }
                else
                    UnityEngine.Debug.LogError("NO GROUND!");
            }

            entities.Dispose();
        }).WithStructuralChanges().Run();

        EntityManager.DestroyEntity(_Query);
    }
}