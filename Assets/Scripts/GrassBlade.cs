using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;
//using static UnityEditor.ShaderGraph.Internal.KeywordDependentCollection;

[RequireComponent(typeof(Collider))]
public class GrassBlade : MonoBehaviour
{
    [Tooltip("Tag that the cutting object must have")]
    public string cutterTag = "Blade";

    private bool cut = false;

    void Awake()
    {
        Collider col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        Debug.Log("Grass trigger hit by: " + other.name);

        if (other.CompareTag(cutterTag))
        {
            CutGrass();
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        Debug.Log("Grass collision hit by: " + collision.gameObject.name);

        if (collision.collider.CompareTag(cutterTag))
        {
            CutGrass();
        }
    }

    void CutGrass()
    {
        if (cut)
            return;

        cut = true;

        Debug.Log("CUT GRASS: " + gameObject.name);

        Destroy(gameObject);
    }
}
