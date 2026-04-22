namespace EunoLab.FogOfWar
{
	public class FogOfWarGameObjectSwitcher : FogOfWarVisibilityHandlerBase
	{
		protected override void OnVisibilityChanged(bool isVisible) => gameObject.SetActive(isVisible);
	}
}
