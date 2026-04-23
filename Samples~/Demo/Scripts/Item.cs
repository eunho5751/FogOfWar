using UnityEngine;
using EunoLab.FogOfWar;

[RequireComponent(typeof(FogOfWarUnit))]
public class Item : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        GetComponent<FogOfWarUnit>().HasVision = true;
        gameObject.SetActive(false);
    }
}