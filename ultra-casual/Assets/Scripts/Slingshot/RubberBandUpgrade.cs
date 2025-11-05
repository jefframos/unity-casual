using System;
using UnityEngine;

public class RubberBandUpgrade : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public LineRenderer[] lineRenderer;

    public Material[] materials;
    void Start()
    {
        UpgradeSystem.Instance.OnReachedNextStep += OnReachedNextStep;
    }

    private void OnReachedNextStep(UpgradeType type, int arg2)
    {
        if (type == UpgradeType.SLINGSHOT)
        {
            var material = materials[arg2];
            foreach (var lr in lineRenderer)
            {
                lr.material = material;
            }
        }
    }

    // Update is called once per frame
    void Update()
    {

    }
}
