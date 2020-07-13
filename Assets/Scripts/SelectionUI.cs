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

    [NonSerialized]
    public Vector2 StartPoint;
    [NonSerialized]
    public Vector2 EndPoint;

    private bool _Dragging;
    private Vector3 _Positon;

    private void Start()
    {
        World.DefaultGameObjectInjectionWorld.GetOrCreateSystem<SelectionSystemDataPrep>().Selection = this;
    }

    void Update()
    {
        if (_Dragging)
        {
            EndPoint = Input.mousePosition;

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
                StartPoint = _Positon;
            }
        }
    }
}

[UpdateInGroup(typeof(InitializationSystemGroup))]
class SelectionSystemDataPrep : SystemBase
{
    public SelectionUI Selection;
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

            var width = Screen.width;
            var height = Screen.height;

            var startPoint = Selection.StartPoint;
            var endPoint = Selection.EndPoint;

            startPoint.x /= width;
            startPoint.y /= height;

            endPoint.x /= width;
            endPoint.y /= height;

            startPoint *= 2f;
            endPoint *= 2f;

            startPoint.x -= 1;
            startPoint.y -= 1;

            endPoint.x -= 1;
            endPoint.y -= 1;

            var commandBuffer = _ECBSystem.CreateCommandBuffer().ToConcurrent();

            Entities.ForEach((Entity entity, int entityInQueryIndex, in LocalToWorld trans, in UnitData data) =>
            {
                var positionV4 = new float4(trans.Position.x, trans.Position.y, trans.Position.z, 1);
                var viewPos = math.mul(fullViewProj, positionV4);
                var viewportPoint = viewPos / -viewPos.w;

                var screenCoord = new float2(viewportPoint.x, viewportPoint.y);

                var selected = screenCoord.x > startPoint.x && screenCoord.y > startPoint.y &&
                                screenCoord.x < endPoint.x && screenCoord.y < endPoint.y;

                var indication = 0f;
                if (selected)
                    indication = 1f;

                commandBuffer.SetComponent(entityInQueryIndex, data.StatusIndicator, new UnitStatusIndicatorMaterialProperties
                {
                    Selected = indication
                });

                screenCoord /= 2f;

                screenCoord.x = screenCoord.x * width;
                screenCoord.y = screenCoord.y * height;

                screenPositions[entityInQueryIndex] = new SelectionSystem.EntityWithScreenPoint
                {
                    Selected = selected,
                    Entity = entity,
                    ScreenPoint = screenCoord,
                };

            }).WithName("SELECTION").ScheduleParallel();
        }

        _ECBSystem.AddJobHandleForProducer(Dependency);
    }
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
class SelectionSystem : SystemBase
{
    public struct EntityWithScreenPoint
    {
        public bool Selected;
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
        if (_OnScreenDataBuffer.IsCreated)
        {
            //var counter = 0;
            //for (int i = 0; i < _OnScreenDataBuffer.Length; i++)
            //{
            //    if (_OnScreenDataBuffer[i].Selected)
            //        counter++;
            //}

            //Debug.Log(_OnScreenDataBuffer.Length + ", counter: " + counter);

            _OnScreenDataBuffer.Dispose();
        }
    }
}