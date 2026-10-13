using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Voidward.Dev
{
    /// <summary>A camera pose for test shots: position, look-at point, field of view.</summary>
    public struct DevPose
    {
        public Vector3 Pos, LookAt;
        public float Fov;
        public DevPose(Vector3 pos, Vector3 lookAt, float fov = 50f) { Pos = pos; LookAt = lookAt; Fov = fov; }
    }

    /// <summary>
    /// Small, cheap test images (#65): renders from a free camera pose or grabs the player's screen, downscaled to
    /// 960x540 JPG by default, and combines several frames into one contact sheet so a whole effect timeline is
    /// one image. Files go to %TEMP%/VoidwardDev (editor and dev builds alike); every save logs "[Dev] shot path".
    /// Works in play mode and (pose renders) in edit mode.
    /// </summary>
    public static class DevShots
    {
        public const int W = 960, H = 540;
        const int Gap = 4;

        public static string Folder
        {
            get { string f = Path.Combine(Path.GetTempPath(), "VoidwardDev"); Directory.CreateDirectory(f); return f; }
        }

        static Camera cam;

        static Camera ShotCam()
        {
            if (cam != null) return cam;
            var go = new GameObject("_DevShotCam") { hideFlags = HideFlags.HideAndDontSave };
            cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 30000f;
            go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
            return cam;
        }

        /// <summary>Render a free camera pose into a new texture (caller owns it). up: camera roll reference (world up by default; pass the local up on planets).</summary>
        public static Texture2D Render(DevPose pose, int w = W, int h = H, Vector3? up = null)
        {
            var c = ShotCam();
            c.transform.position = pose.Pos;
            var dir = pose.LookAt - pose.Pos;
            c.transform.rotation = Quaternion.LookRotation(dir.sqrMagnitude > 1e-6f ? dir : Vector3.forward, up ?? Vector3.up);
            c.fieldOfView = pose.Fov > 1f ? pose.Fov : 50f;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            c.targetTexture = rt;
            c.Render();
            c.targetTexture = null;
            var tex = ReadBack(rt, w, h);
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        /// <summary>
        /// The player's view incl. HUD, downscaled. Only valid at the end of a frame (use DevTest.Screen /
        /// DevTest.Sequence, which wait for it).
        /// </summary>
        public static Texture2D GrabScreen(int w = W, int h = H)
        {
            var full = ScreenCapture.CaptureScreenshotAsTexture();
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(full, rt);
            Object.Destroy(full);
            var tex = ReadBack(rt, w, h);
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        static Texture2D ReadBack(RenderTexture rt, int w, int h)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            return tex;
        }

        /// <summary>Render a pose and save it: returns the file path.</summary>
        public static string Shot(string name, DevPose pose, int w = W, int h = H)
        {
            var tex = Render(pose, w, h);
            string path = Save(tex, name);
            Kill(tex);
            return path;
        }

        /// <summary>Save as JPG (quality 85) into the dev folder and log the path.</summary>
        public static string Save(Texture2D tex, string name)
        {
            string path = Path.Combine(Folder, name + ".jpg");
            File.WriteAllBytes(path, tex.EncodeToJPG(85));
            Debug.Log("[Dev] shot " + path);
            return path;
        }

        /// <summary>Tiles left to right, top to bottom, in one image (all tiles the same size).</summary>
        public static Texture2D Sheet(List<Texture2D> tiles, int cols = 0)
        {
            if (tiles.Count == 0) return null;
            int n = tiles.Count;
            if (cols <= 0) cols = n <= 2 ? n : n <= 4 ? 2 : 3;
            int rows = (n + cols - 1) / cols;
            int tw = tiles[0].width, th = tiles[0].height;
            var sheet = new Texture2D(cols * tw + (cols - 1) * Gap, rows * th + (rows - 1) * Gap, TextureFormat.RGB24, false);
            var fill = new Color32[sheet.width * sheet.height];
            for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(20, 20, 24, 255);
            sheet.SetPixels32(fill);
            for (int i = 0; i < n; i++)
            {
                int c = i % cols, r = i / cols;
                int x = c * (tw + Gap), y = (rows - 1 - r) * (th + Gap); // texture origin is bottom-left
                sheet.SetPixels32(x, y, tw, th, tiles[i].GetPixels32());
            }
            sheet.Apply();
            return sheet;
        }

        public static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }
    }
}
