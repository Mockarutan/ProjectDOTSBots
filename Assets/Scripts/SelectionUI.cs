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

    public bool Dragging { get; private set; }
    public Vector2 RightClick { get; private set; }

    private Vector3 _Positon;

    private void Start()
    {
        World.DefaultGameObjectInjectionWorld.GetOrCreateSystem<SelectionSystemDataPrep>().Selection = this;
        World.DefaultGameObjectInjectionWorld.GetOrCreateSystem<UnitMovementSystem>().Selection = this;
    }

    void Update()
    {
        if (Dragging)
        {
            EndPoint = Input.mousePosition;

            var mid = (Input.mousePosition + _Positon) / 2;

            mid.x -= Screen.width / 2;
            mid.y -= Screen.height / 2;

            Squre.anchoredPosition = mid;
            Squre.sizeDelta = Input.mousePosition - _Positon;

            if (Input.GetMouseButton(0) == false)
                Dragging = false;
        }
        else
        {
            if (Input.GetMouseButton(0))
            {
                Dragging = true;
                _Positon = Input.mousePosition;
                StartPoint = _Positon;
            }
            if (Input.GetMouseButtonDown(1))
            {
                RightClick = Input.mousePosition;
            }
        }

        UnityHelp.SetActive(Squre, Dragging);
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
            var screenSize = new Vector2(width, height);
            var dragging = Selection.Dragging;

            var startPoint = Selection.StartPoint;
            var endPoint = Selection.EndPoint;

            startPoint.x = ((startPoint.x / width) * 2f) - 1;
            endPoint.x = ((endPoint.x / width) * 2f) - 1;

            startPoint.y = ((startPoint.y / height) * 2f) - 1;
            endPoint.y = ((endPoint.y / height) * 2f) - 1;

            var selectRect = Rect.MinMaxRect(startPoint.x, startPoint.y, endPoint.x, endPoint.y);

            var commandBuffer = _ECBSystem.CreateCommandBuffer().ToConcurrent();

            Entities.ForEach((Entity entity, int entityInQueryIndex, ref UnitData data, in LocalToWorld trans) =>
            {
                var positionV4 = new float4(trans.Position.x, trans.Position.y, trans.Position.z, 1);
                var viewPos = math.mul(fullViewProj, positionV4);
                var viewportPoint = viewPos / -viewPos.w;

                var screenCoord = new float2(viewportPoint.x, viewportPoint.y);

                if (dragging)
                    data.Selected = selectRect.Contains(screenCoord);

                //var indication = 0f;
                //if (data.Selected)
                //    indication = 1f;

                //materialProps.Selected = indication;

                screenCoord /= 2f;

                screenCoord.x = screenCoord.x * width;
                screenCoord.y = screenCoord.y * height;

                screenPositions[entityInQueryIndex] = new SelectionSystem.EntityWithScreenPoint
                {
                    Selected = data.Selected,
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
        var commandBuffer = new EntityCommandBuffer(Allocator.TempJob);
        Entities.ForEach((Entity entity, in UnitData data) =>
        {
            float indication;
            if (data.Selected)
                indication = 1;
            else
                indication = 0;

            commandBuffer.SetComponent(data.StatusIndicator, new UnitStatusIndicatorMaterialProperties
            {
                Selected = indication
            });

        }).Run();

        commandBuffer.Playback(EntityManager);
        commandBuffer.Dispose();

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