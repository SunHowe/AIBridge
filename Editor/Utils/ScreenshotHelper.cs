using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace AIBridge.Editor
{
    /// <summary>
    /// Screenshot result data.
    /// </summary>
    public class ScreenshotResult
    {
        public bool Success;
        public string ImagePath;
        public string Filename;
        public int Width;
        public int Height;
        public string Timestamp;
        public string Error;
    }

    /// <summary>
    /// Frame capture result for GIF recording.
    /// </summary>
    public class FrameCaptureResult
    {
        public bool Success;
        public byte[] Pixels;  // RGBA32 pixel data
        public int Width;
        public int Height;
        public string Error;
    }

    /// <summary>
    /// Shared screenshot capture logic for CLI and hotkey.
    /// Reads the Game view's internal RenderTexture directly, capturing the full
    /// composited output including Screen Space - Overlay Canvas without requiring focus.
    /// </summary>
    public static class ScreenshotHelper
    {
        private const int JpegQuality = 85;
        private const int MaxScreenshotDimension = 8192;

        private static string _screenshotsDir;

        // Cached flip buffer for frame capture (reused across frames)
        private static byte[] _cachedFlipBuffer;
        private static int _cachedWidth;
        private static int _cachedHeight;

        // Cached reflection handles for Game view render texture
        private static bool _reflectionInitialized;
        private static System.Type _gameViewType;
        private static FieldInfo _renderTextureField;

        /// <summary>
        /// Get the screenshots directory path.
        /// </summary>
        public static string ScreenshotsDir
        {
            get
            {
                if (string.IsNullOrEmpty(_screenshotsDir))
                {
                    var projectRoot = Path.GetDirectoryName(Application.dataPath);
                    _screenshotsDir = Path.Combine(projectRoot, ".aibridge", "screenshots");
                }
                return _screenshotsDir;
            }
        }

        /// <summary>
        /// Capture Game view screenshot.
        /// </summary>
        public static ScreenshotResult CaptureGameView(bool checkPlayMode = true)
        {
            if (checkPlayMode && !EditorApplication.isPlaying)
            {
                return new ScreenshotResult
                {
                    Success = false,
                    Error = "Screenshot requires Play mode. Please start the game first."
                };
            }

            EnsureScreenshotsDirectory();

            try
            {
                var texture2D = CaptureGameViewTexture();
                if (texture2D == null)
                {
                    return new ScreenshotResult
                    {
                        Success = false,
                        Error = "Failed to capture Game view. Ensure the Game view is open and in Play mode."
                    };
                }

                return SaveScreenshotTexture(texture2D, "game", "Game view");
            }
            catch (Exception ex)
            {
                return new ScreenshotResult
                {
                    Success = false,
                    Error = $"Failed to capture screenshot: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 捕获 Scene 视图截图。
        /// </summary>
        public static ScreenshotResult CaptureSceneView(int width = 0, int height = 0)
        {
            EnsureScreenshotsDirectory();

            try
            {
                string error;
                var texture2D = CaptureSceneViewTexture(width, height, out error);
                if (texture2D == null)
                {
                    return new ScreenshotResult
                    {
                        Success = false,
                        Error = string.IsNullOrEmpty(error)
                            ? "Failed to capture Scene view. Ensure a Scene view is open in the Unity Editor."
                            : error
                    };
                }

                return SaveScreenshotTexture(texture2D, "scene_view", "Scene view");
            }
            catch (Exception ex)
            {
                return new ScreenshotResult
                {
                    Success = false,
                    Error = $"Failed to capture Scene view screenshot: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Capture a single frame for GIF recording.
        /// </summary>
        public static FrameCaptureResult CaptureFrame(float scale = 1f)
        {
            if (!EditorApplication.isPlaying)
            {
                return new FrameCaptureResult
                {
                    Success = false,
                    Error = "Frame capture requires Play mode."
                };
            }

            try
            {
                var texture2D = CaptureGameViewTexture();
                if (texture2D == null)
                {
                    return new FrameCaptureResult
                    {
                        Success = false,
                        Error = "Failed to capture Game view."
                    };
                }

                int width = texture2D.width;
                int height = texture2D.height;

                // Apply scale if needed
                if (scale < 1f)
                {
                    scale = Mathf.Clamp(scale, 0.25f, 1f);
                    int scaledWidth = Mathf.Max(1, (int)(width * scale));
                    int scaledHeight = Mathf.Max(1, (int)(height * scale));

                    var rt = RenderTexture.GetTemporary(scaledWidth, scaledHeight);
                    Graphics.Blit(texture2D, rt);

                    var scaledTexture = new Texture2D(scaledWidth, scaledHeight, TextureFormat.RGBA32, false);
                    RenderTexture.active = rt;
                    scaledTexture.ReadPixels(new Rect(0, 0, scaledWidth, scaledHeight), 0, 0);
                    scaledTexture.Apply();
                    RenderTexture.active = null;
                    RenderTexture.ReleaseTemporary(rt);

                    UnityEngine.Object.DestroyImmediate(texture2D);
                    texture2D = scaledTexture;
                    width = scaledWidth;
                    height = scaledHeight;
                }

                // Get raw pixel data and flip vertically
                var pixels = texture2D.GetRawTextureData();
                EnsureFlipBuffer(width, height);
                FlipVerticallyInPlace(pixels, _cachedFlipBuffer, width, height);

                UnityEngine.Object.DestroyImmediate(texture2D);

                // Return a copy of the flipped buffer
                var result = new byte[_cachedFlipBuffer.Length];
                Buffer.BlockCopy(_cachedFlipBuffer, 0, result, 0, result.Length);

                return new FrameCaptureResult
                {
                    Success = true,
                    Pixels = result,
                    Width = width,
                    Height = height
                };
            }
            catch (Exception ex)
            {
                return new FrameCaptureResult
                {
                    Success = false,
                    Error = $"Failed to capture frame: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Capture the Game view content by reading its internal RenderTexture directly.
        /// This captures everything including Screen Space - Overlay Canvas,
        /// regardless of which editor window is currently focused.
        /// Returns a new Texture2D that the caller must destroy.
        /// </summary>
        private static Texture2D CaptureGameViewTexture()
        {
            var rt = GetGameViewRenderTexture();
            if (rt != null)
            {
                // Use GPU Blit to flip vertically when needed, avoiding CPU pixel ops
                var flipped = RenderTexture.GetTemporary(rt.width, rt.height, 0, rt.graphicsFormat);
                if (SystemInfo.graphicsUVStartsAtTop)
                    Graphics.Blit(rt, flipped, new Vector2(1, -1), new Vector2(0, 1));
                else
                    Graphics.Blit(rt, flipped);

                var texture2D = new Texture2D(flipped.width, flipped.height, TextureFormat.RGBA32, false);
                var prev = RenderTexture.active;
                RenderTexture.active = flipped;
                texture2D.ReadPixels(new Rect(0, 0, flipped.width, flipped.height), 0, 0);
                texture2D.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(flipped);

                return texture2D;
            }

            return CaptureActiveFrameTexture();
        }

        private static Texture2D CaptureActiveFrameTexture()
        {
            var width = Screen.width;
            var height = Screen.height;
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            // Assets 安装模式下 Unity 2021 可能未引用 ScreenCaptureModule，使用 ReadPixels 避免编译期依赖。
            var previousActive = RenderTexture.active;
            try
            {
                var texture2D = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture2D.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture2D.Apply();
                return texture2D;
            }
            finally
            {
                RenderTexture.active = previousActive;
            }
        }

        /// <summary>
        /// 通过复制 SceneView 相机到临时相机完成截图，避免修改编辑器窗口内部相机状态。
        /// </summary>
        private static Texture2D CaptureSceneViewTexture(int requestedWidth, int requestedHeight, out string error)
        {
            error = null;

            if (requestedWidth < 0 || requestedHeight < 0)
            {
                error = "Parameters 'width' and 'height' must be positive integers when specified.";
                return null;
            }

            if ((requestedWidth > 0 && requestedHeight <= 0) ||
                (requestedHeight > 0 && requestedWidth <= 0))
            {
                error = "Parameters 'width' and 'height' must be specified together.";
                return null;
            }

            if (requestedWidth > MaxScreenshotDimension || requestedHeight > MaxScreenshotDimension)
            {
                error = $"Scene view screenshot size exceeds maximum allowed ({MaxScreenshotDimension}x{MaxScreenshotDimension}).";
                return null;
            }

            var sceneView = GetSceneViewWindow();
            if (sceneView == null)
            {
                error = "No Scene view window found. Open the Scene view in the Unity Editor first.";
                return null;
            }

            var sourceCamera = sceneView.camera;
            if (sourceCamera == null)
            {
                error = "Scene view camera is not available.";
                return null;
            }

            var width = requestedWidth;
            var height = requestedHeight;

            if (width <= 0 || height <= 0)
            {
                width = sourceCamera.pixelWidth;
                height = sourceCamera.pixelHeight;
            }

            if (width <= 0 || height <= 0)
            {
                width = Mathf.RoundToInt(sceneView.position.width);
                height = Mathf.RoundToInt(sceneView.position.height);
            }

            width = Mathf.Clamp(width, 1, MaxScreenshotDimension);
            height = Mathf.Clamp(height, 1, MaxScreenshotDimension);

            RenderTexture rt = null;
            var previousActive = RenderTexture.active;
            GameObject cameraObject = null;
            Camera captureCamera = null;
            Texture2D texture2D = null;

            try
            {
                rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
                rt.Create();

                // 使用临时相机渲染，避免直接操作 SceneView.camera 带来的编辑器状态污染。
                cameraObject = new GameObject("AIBridge SceneView Capture Camera");
                cameraObject.hideFlags = HideFlags.HideAndDontSave;

                captureCamera = cameraObject.AddComponent<Camera>();
                captureCamera.enabled = false;
                captureCamera.CopyFrom(sourceCamera);
                captureCamera.targetTexture = rt;
                captureCamera.Render();

                texture2D = new Texture2D(width, height, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                texture2D.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture2D.Apply();

                var result = texture2D;
                texture2D = null;
                return result;
            }
            catch (Exception ex)
            {
                if (texture2D != null)
                {
                    UnityEngine.Object.DestroyImmediate(texture2D);
                }

                error = $"Failed to render Scene view camera: {ex.Message}";
                return null;
            }
            finally
            {
                RenderTexture.active = previousActive;

                if (captureCamera != null)
                {
                    captureCamera.targetTexture = null;
                }

                if (rt != null)
                {
                    RenderTexture.ReleaseTemporary(rt);
                }

                if (cameraObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(cameraObject);
                }
            }
        }

        /// <summary>
        /// Get the Game view's internal RenderTexture via reflection.
        /// Searches GameView and its base class PlayModeView for the m_RenderTexture field.
        /// </summary>
        private static RenderTexture GetGameViewRenderTexture()
        {
            InitReflection();
            if (_gameViewType == null) return null;

            var gameView = GetGameViewWindow();
            if (gameView == null) return null;

            if (_renderTextureField != null)
            {
                return _renderTextureField.GetValue(gameView) as RenderTexture;
            }

            return null;
        }

        private static void InitReflection()
        {
            if (_reflectionInitialized) return;
            _reflectionInitialized = true;

            var editorAssembly = typeof(EditorWindow).Assembly;
            _gameViewType = editorAssembly.GetType("UnityEditor.GameView");
            if (_gameViewType == null) return;

            const BindingFlags kInstance = BindingFlags.NonPublic | BindingFlags.Instance;

            // Try GameView first, then walk up to base classes (PlayModeView, etc.)
            var type = _gameViewType;
            while (type != null && type != typeof(object))
            {
                _renderTextureField = type.GetField("m_RenderTexture", kInstance);
                if (_renderTextureField != null) break;
                type = type.BaseType;
            }
        }

        private static EditorWindow GetGameViewWindow()
        {
            if (_gameViewType == null) return null;

            // Find existing Game view without creating or focusing it
            var allWindows = Resources.FindObjectsOfTypeAll(_gameViewType);
            if (allWindows.Length > 0)
            {
                return allWindows[0] as EditorWindow;
            }

            return null;
        }

        private static SceneView GetSceneViewWindow()
        {
            if (SceneView.lastActiveSceneView != null)
            {
                return SceneView.lastActiveSceneView;
            }

            var sceneViews = SceneView.sceneViews;
            if (sceneViews != null)
            {
                for (int i = 0; i < sceneViews.Count; i++)
                {
                    var sceneView = sceneViews[i] as SceneView;
                    if (sceneView != null)
                    {
                        return sceneView;
                    }
                }
            }

            return null;
        }

        private static ScreenshotResult SaveScreenshotTexture(Texture2D texture2D, string filePrefix, string logLabel)
        {
            if (texture2D == null)
            {
                return new ScreenshotResult
                {
                    Success = false,
                    Error = $"Failed to capture {logLabel} screenshot."
                };
            }

            var timestamp = DateTime.Now;
            var filename = $"{filePrefix}_{timestamp:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 8)}.jpg";
            var fullPath = Path.Combine(ScreenshotsDir, filename);

            try
            {
                int width = texture2D.width;
                int height = texture2D.height;

                var jpgData = texture2D.EncodeToJPG(JpegQuality);
                File.WriteAllBytes(fullPath, jpgData);

                AIBridgeLogger.LogInfo($"{logLabel} screenshot saved: {fullPath}");

                return new ScreenshotResult
                {
                    Success = true,
                    ImagePath = fullPath,
                    Filename = filename,
                    Width = width,
                    Height = height,
                    Timestamp = timestamp.ToString("yyyy-MM-ddTHH:mm:ss")
                };
            }
            catch (Exception ex)
            {
                return new ScreenshotResult
                {
                    Success = false,
                    Error = $"Failed to save {logLabel} screenshot: {ex.Message}"
                };
            }
            finally
            {
                if (texture2D != null)
                {
                    UnityEngine.Object.DestroyImmediate(texture2D);
                }
            }
        }

        /// <summary>
        /// Ensure flip buffer has correct size.
        /// </summary>
        private static void EnsureFlipBuffer(int width, int height)
        {
            if (_cachedWidth == width && _cachedHeight == height && _cachedFlipBuffer != null)
                return;

            _cachedFlipBuffer = new byte[width * height * 4];
            _cachedWidth = width;
            _cachedHeight = height;
        }

        /// <summary>
        /// Release cached resources. Call when recording ends.
        /// </summary>
        public static void ReleaseCachedResources()
        {
            _cachedFlipBuffer = null;
            _cachedWidth = 0;
            _cachedHeight = 0;
        }

        /// <summary>
        /// Ensure screenshots directory exists.
        /// </summary>
        public static void EnsureScreenshotsDirectory()
        {
            if (!Directory.Exists(ScreenshotsDir))
            {
                Directory.CreateDirectory(ScreenshotsDir);
                var gitignorePath = Path.Combine(ScreenshotsDir, ".gitignore");
                File.WriteAllText(gitignorePath, "*\n!.gitignore\n");
            }
        }

        /// <summary>
        /// Flip pixel data vertically using pre-allocated buffer.
        /// </summary>
        private static void FlipVerticallyInPlace(byte[] src, byte[] dst, int width, int height)
        {
            int rowSize = width * 4;

            for (int y = 0; y < height; y++)
            {
                int srcRow = y * rowSize;
                int dstRow = (height - 1 - y) * rowSize;
                Buffer.BlockCopy(src, srcRow, dst, dstRow, rowSize);
            }
        }
    }
}
