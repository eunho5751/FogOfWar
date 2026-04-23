using System;
using UnityEngine;
using UnityEngine.Assertions;

namespace EunoLab.FogOfWar
{
	public class FogOfWarUnit : MonoBehaviour
	{
		[SerializeField, Tooltip("If enabled, this unit registers itself with FogOfWar.Main on Awake. Disable when you want to register the unit to a specific FogOfWar instance manually.")]
		private bool _autoRegisterToMain = true;
		[SerializeField, Range(0, 31), Tooltip("Team index (0-31) this unit belongs to. Units are visible to any FogOfWar whose team mask includes this layer, and their vision clears fog for the same mask.")]
		private int _teamLayer = 0;
		[SerializeField, Tooltip("If enabled, this unit's vision ignores blocking tiles and reveals fog through walls and other obstacles.")]
		private bool _ignoreObstacles;
		[SerializeField, Tooltip("If enabled, this unit grants vision (clears fog within its radius). Disable for units that should be revealed by allies but not contribute their own sight.")]
		private bool _hasVision = true;
		[SerializeField, Min(0), Tooltip("Vision radius in world units. Tiles within this distance from the unit are revealed each visibility update.")]
		private float _visionRadius = 5f;

		private void Awake()
		{
			if (_autoRegisterToMain)
			{
				var mainFOW = FogOfWar.Main;
				if (mainFOW != null)
				{
					mainFOW.AddUnit(this);
				}
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

		private void OnEnable()
		{
			if (FogOfWar != null)
			{
				SetVisible(true);
			}
		}

		private void OnDisable()
		{
			if (FogOfWar != null)
			{
				SetVisible(false);
			}
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

			if (IsVisible == visible)
				return;

			IsVisible = visible;
			VisibilityChanged?.Invoke(IsVisible);
		}

		public int TeamLayer
		{
			get => _teamLayer;
			set => _teamLayer = Mathf.Clamp(value, 0, 31);
		}

		public event Action<bool> VisibilityChanged;

		public bool IgnoreObstacles
		{
			get => _ignoreObstacles;
			set => _ignoreObstacles = value;
		}

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
