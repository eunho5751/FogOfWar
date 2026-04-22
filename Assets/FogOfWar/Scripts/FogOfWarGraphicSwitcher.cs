using UnityEngine.UI;

namespace EunoLab.FogOfWar
{
	public class FogOfWarGraphicSwitcher : FogOfWarVisibilityHandlerBase
	{
		private Graphic _graphic;

		protected override void OnAwake() => TryGetComponent(out _graphic);
		protected override void OnVisibilityChanged(bool isVisible) => _graphic.enabled = isVisible;
	}
}
