using UnityEngine;

namespace EunoLab.FogOfWar
{
	public class FogOfWarRendererSwitcher : FogOfWarVisibilityHandlerBase
	{
		private Renderer _renderer;

		protected override void OnAwake() => TryGetComponent(out _renderer);
		protected override void OnVisibilityChanged(bool isVisible) => _renderer.enabled = isVisible;
	}
}
