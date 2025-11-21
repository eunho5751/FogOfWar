using UnityEngine;
using UnityEngine.UI;

namespace Prototype
{
    public class FogOfWarUnitRenderer : MonoBehaviour
    {
        private Renderer _objectRenderer;
        private Graphic _uiRenderer;

        private void Awake()
        {
            if (TryGetComponent(out Renderer objectRenderer))
            {
                _objectRenderer = objectRenderer;
            }
            if (TryGetComponent(out Graphic uiRenderer))
            {
                _uiRenderer = uiRenderer;
            }
        }

        public void SetVisible(bool visible)
        {
            if (_objectRenderer != null)
                _objectRenderer.enabled = visible;
            if (_uiRenderer != null)
                _uiRenderer.enabled = visible;
        }
    }
}