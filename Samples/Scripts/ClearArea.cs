using System.Collections;
using UnityEngine;
using EunoLab.FogOfWar;

public class ClearArea : MonoBehaviour
{
    private void OnTriggerEnter(Collider col)
    {
        if (col.TryGetComponent(out FogOfWarUnit unit))
        {
            unit.IgnoreObstacles = true;
            StartCoroutine(BrightenLevel(unit));
            GetComponent<Collider>().enabled = false;            
        }
    }

    private IEnumerator BrightenLevel(FogOfWarUnit unit)
    {
        float elapsedTime = 0f;
        while (elapsedTime < 3f)
        {
            yield return null;

            elapsedTime += Time.deltaTime;
            unit.VisionRadius += 5f * Time.deltaTime;
        }
    }
}