using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public static class UnityHelp
{
    public static bool SetActive(GameObject gameObject, bool isActive)
    {
        if (gameObject.activeSelf != isActive)
        {
            gameObject.SetActive(isActive);
            return true;
        }
        return false;
    }
    public static bool SetActive(Behaviour behaviour, bool isActive)
    {
        if (behaviour.enabled != isActive)
        {
            behaviour.enabled = isActive;
            return true;
        }
        return false;
    }
    public static bool SetActive(Transform trans, bool isActive)
    {
        if (trans.gameObject.activeSelf != isActive)
        {
            trans.gameObject.SetActive(isActive);
            return true;
        }
        return false;
    }
    public static bool SetActive(RectTransform trans, bool isActive)
    {
        if (trans.gameObject.activeSelf != isActive)
        {
            trans.gameObject.SetActive(isActive);
            return true;
        }
        return false;
    }
}

