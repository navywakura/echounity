using System.Collections.Generic;
using UnityEngine;
namespace EchoStation
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class CameraFacing : MonoBehaviour
    {
        private struct FacingState
        {
            public Camera camera;
            public Quaternion rotation;
        }

        private readonly Stack<FacingState> renders = new Stack<FacingState>();

        private void OnEnable()
        {
            Camera.onPreCull += BeforeCamera;
            Camera.onPostRender += AfterCamera;
        }

        private void BeforeCamera(Camera camera)
        {
            if (!isActiveAndEnabled || (camera.cullingMask & (1 << gameObject.layer)) == 0) return;
#if UNITY_EDITOR
            bool previewCamera = UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(camera.scene);
            bool previewObject = UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(gameObject.scene);
            if ((previewCamera || previewObject) && camera.scene != gameObject.scene) return;
#endif
            // Set orientation before culling, even when the previous orientation was edge-on.
            renders.Push(new FacingState { camera = camera, rotation = transform.rotation });
            transform.rotation = camera.transform.rotation;
        }

        private void AfterCamera(Camera camera)
        {
            if (renders.Count == 0 || renders.Peek().camera != camera) return;
            transform.rotation = renders.Pop().rotation;
        }

        private void OnDisable()
        {
            Camera.onPreCull -= BeforeCamera;
            Camera.onPostRender -= AfterCamera;
            while (renders.Count != 0) transform.rotation = renders.Pop().rotation;
        }
    }
}
