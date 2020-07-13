using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Rendering;
using UnityEngine;

public class Unit : MonoBehaviour, IConvertGameObjectToEntity
{
    public GameObject StatusIndicator;

    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        var healthLight = conversionSystem.GetPrimaryEntity(StatusIndicator);

        dstManager.AddComponent<UnitStatusIndicatorMaterialProperties>(healthLight);

        dstManager.AddComponentData(entity, new UnitData
        {
            StatusIndicator = healthLight,
        });
    }
}

struct UnitData : IComponentData
{
    public Entity StatusIndicator;
}

[MaterialProperty("_Selected", MaterialPropertyFormat.Float)]
struct UnitStatusIndicatorMaterialProperties : IComponentData
{
    public float Selected;
}