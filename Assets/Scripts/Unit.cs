using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

public class Unit : MonoBehaviour, IConvertGameObjectToEntity
{
    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        dstManager.AddComponent<UnitData>(entity);
    }
}

struct UnitData : IComponentData
{ }