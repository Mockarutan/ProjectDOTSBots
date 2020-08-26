using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

public class GiantBot : MonoBehaviour, IConvertGameObjectToEntity
{
    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        dstManager.AddComponent<TargetTag>(entity);
        dstManager.AddComponentData(entity, new GiantBotState
        {

        });
    }
}

struct GiantBotState : IComponentData
{

}