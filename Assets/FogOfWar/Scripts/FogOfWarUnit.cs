using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;

namespace Prototype
{
    public class FogOfWarUnit : MonoBehaviour
    {
        [SerializeField, Range(0, 31)]
        private int _teamLayer = 0;
        [SerializeField]
        private bool _hasVision = true;
        [SerializeField, Min(0)]
        private float _visionRadius = 5f;

        private readonly List<FogOfWarUnitRenderer> _renderers = new();

        private void Awake()
        {
            var initialRenderers = GetComponentsInChildren<FogOfWarUnitRenderer>();
            foreach (var renderer in initialRenderers)
            {
                AddRenderer(renderer);
            }
        }

        private void OnDestroy()
        {
            if (FogOfWar != null)
            {
                FogOfWar.RemoveUnit(this);
                FogOfWar = null;
            }
        }

        public void AddRenderer(FogOfWarUnitRenderer renderer)
        {
            if (_renderers.Contains(renderer))
                return;

            if (FogOfWar != null && FogOfWar.IsActivated)
            {
                renderer.SetVisible(IsVisible);
            }
            _renderers.Add(renderer);
        }

        public bool IsTeammate(int teamMask)
        {
            return (teamMask & (1 << _teamLayer)) != 0;
        }

        internal void SetFogOfWar(FogOfWar fow)
        {
            Assert.IsNull(FogOfWar);

            FogOfWar = fow;
        }

        internal void SetVisible(bool visible)
        {
            Assert.IsNotNull(FogOfWar);

            foreach (var renderer in _renderers)
            {
                renderer.SetVisible(visible);
            }
            IsVisible = visible;
            VisibilityChanged?.Invoke(IsVisible);
        }

        public int TeamLayer
        {
            get => _teamLayer;
            set => _teamLayer = Mathf.Clamp(value, 0, 31);
        }

        public event Action<bool> VisibilityChanged;

        public bool HasVision
        {
            get => _hasVision;
            set => _hasVision = value;
        }

        public float VisionRadius
        {
            get => _visionRadius;
            set => _visionRadius = value < 0f ? 0f : value;
        }

        public bool IsVisible { get; private set; }
        public FogOfWar FogOfWar { get; private set; }
    }
}