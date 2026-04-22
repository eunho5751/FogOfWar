using System;
using UnityEngine;
using UnityEngine.Assertions;

namespace EunoLab.FogOfWar
{
	public class FogOfWarUnit : MonoBehaviour
	{
		[SerializeField]
		private bool _autoRegisterToMain = true;
		[SerializeField, Range(0, 31)]
		private int _teamLayer = 0;
		[SerializeField]
		private bool _hasVision = true;
		[SerializeField, Min(0)]
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
