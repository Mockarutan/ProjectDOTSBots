using System;
using System.Collections;
using System.Collections.Generic;
using System.Xml;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public class SelectionUI : MonoBehaviour
{
    public RectTransform Squre;

    private bool _Dragging;
    private Vector3 _Positon;

    void Update()
    {
        if (_Dragging)
        {
            var mid = (Input.mousePosition + _Positon) / 2;

            mid.x -= Screen.width / 2;
            mid.y -= Screen.height / 2;

            Squre.anchoredPosition = mid;
            Squre.sizeDelta = Input.mousePosition - _Positon;

            if (Input.GetMouseButton(0) == false)
                _Dragging = false;
        }
        else
        {
            if (Input.GetMouseButton(0))
            {
                _Dragging = true;
                _Positon = Input.mousePosition;
            }
        }
    }
}

[UpdateInGroup(typeof(InitializationSystemGroup))]
class SelectionSystemDataPrep : SystemBase
{
    public RectTransform TestPoint;

    private EntityQuery _UnitQuery;
    private SelectionSystem _SelectionSystem;
    private EndInitializationEntityCommandBufferSystem _ECBSystem;

    protected override void OnCreate()
    {
        _UnitQuery = GetEntityQuery(typeof(UnitData));
        _SelectionSystem = World.GetOrCreateSystem<SelectionSystem>();
        _ECBSystem = World.GetOrCreateSystem<EndInitializationEntityCommandBufferSystem>();
    }

    protected override void OnUpdate()
    {
        Camera camera = null;
        Entities.ForEach((in PlayerCamera playerCamera) =>
        {
            camera = playerCamera.Camera;

        }).WithoutBurst().Run();

        if (camera != null)
        {
            var count = _UnitQuery.CalculateEntityCount();
            var screenPositions = new NativeArray<SelectionSystem.EntityWithScreenPoint>(count, Allocator.TempJob);
            _SelectionSystem.SetBuffer(screenPositions);

            var fullViewProj = (float4x4)(camera.projectionMatrix * camera.transform.worldToLocalMatrix);

            Entities.ForEach((Entity entity, int entityInQueryIndex, in LocalToWorld trans, in UnitData data) =>
            {
                var positionV4 = new float4(trans.Position.x, trans.Position.y, trans.Position.z, 1);
                var viewPos = math.mul(fullViewProj, positionV4);
                var viewportPoint = viewPos / -viewPos.w;

                var screenCoord = new float2(viewportPoint.x, viewportPoint.y);

                screenCoord /= 2f;

                screenCoord.x = screenCoord.x * Screen.width;
                screenCoord.y = screenCoord.y * Screen.height;

                screenPositions[entityInQueryIndex] = new SelectionSystem.EntityWithScreenPoint
                {
                    Entity = entity,
                    ScreenPoint = screenCoord,
                };

            }).ScheduleParallel();
        }

        _ECBSystem.AddJobHandleForProducer(Dependency);
    }
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
class SelectionSystem : SystemBase
{
    public struct EntityWithScreenPoint
    {
        public Entity Entity;
        public float2 ScreenPoint;
    }

    private NativeArray<EntityWithScreenPoint> _OnScreenDataBuffer;

    public void SetBuffer(NativeArray<EntityWithScreenPoint> buffer)
    {
        _OnScreenDataBuffer = buffer;
    }

    protected override void OnUpdate()
    {
        for (int i = 0; i < _OnScreenDataBuffer.Length; i++)
        {
            Debug.Log("_OnScreenDataBuffer: " + _OnScreenDataBuffer[i].ScreenPoint);
        }

        _OnScreenDataBuffer.Dispose();
    }
}