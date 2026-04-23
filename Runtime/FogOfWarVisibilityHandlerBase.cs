using UnityEngine;

namespace EunoLab.FogOfWar
{
	public abstract class FogOfWarVisibilityHandlerBase : MonoBehaviour
	{
		private FogOfWarUnit _unit;

		private void Awake()
		{
			_unit = GetComponentInParent<FogOfWarUnit>(true);
			OnAwake();
		}

		private void OnEnable()
		{
			_unit.VisibilityChanged += OnVisibilityChanged;
			OnVisibilityChanged(_unit.IsVisible);
			OnEnabled();
		}

		private void OnDisable()
		{
			_unit.VisibilityChanged -= OnVisibilityChanged;
			OnDisabled();
		}

		protected virtual void OnAwake() { }
		protected virtual void OnEnabled() { }
		protected virtual void OnDisabled() { }
		protected virtual void OnVisibilityChanged(bool isVisible) { }

		public FogOfWarUnit Unit => _unit;
	}
}
