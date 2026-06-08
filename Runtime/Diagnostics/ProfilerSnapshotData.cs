using System;

namespace AIBridge.Runtime.Diagnostics
{
    [Serializable]
    public sealed class ProfilerSnapshot
    {
        public string source;
        public string timestampUtc;
        public string targetId;
        public ProfilerStats stats;
        public ProfilerUnsupportedItem[] unsupported;
        public string[] warnings;
    }

    [Serializable]
    public sealed class ProfilerStats
    {
        public ProfilerStatusStats status;
        public ProfilerModuleStats[] modules;
        public ProfilerFrameStats frame;
        public ProfilerMemoryStats memory;
        public ProfilerRenderingStats rendering;
        public ProfilerScriptStats script;
    }

    [Serializable]
    public sealed class ProfilerStatusStats
    {
        public bool profilerEnabled;
        public bool isEditor;
        public bool isPlaying;
        public bool isPaused;
        public bool supportsRuntimeProfiler;
        public bool supportsEditorProfiler;
    }

    [Serializable]
    public sealed class ProfilerModuleStats
    {
        public string name;
        public bool enabled;
        public bool supported;
        public string source;
    }

    [Serializable]
    public sealed class ProfilerFrameStats
    {
        public int frameCount;
        public double deltaTimeMs;
        public double fps;
        public double realtimeSinceStartup;
        public string sampleSource;
    }

    [Serializable]
    public sealed class ProfilerMemoryStats
    {
        public long totalReservedBytes;
        public long totalAllocatedBytes;
        public long totalUnusedReservedBytes;
        public long monoUsedBytes;
        public long monoHeapBytes;
        public long gcUsedBytes;
        public long systemUsedBytes;
        public long graphicsDriverBytes;
    }

    [Serializable]
    public sealed class ProfilerRenderingStats
    {
        public double frameTimeMs;
        public double fps;
        public int vSyncCount;
        public int targetFrameRate;
        public string graphicsDeviceType;
        public string graphicsDeviceName;
        public int screenWidth;
        public int screenHeight;
        public string renderPipeline;
    }

    [Serializable]
    public sealed class ProfilerScriptStats
    {
        public double mainThreadFrameTimeMs;
        public long gcAllocatedBytesDelta;
        public int gcCollectionCount0Delta;
        public long monoUsedBytes;
        public long gcUsedBytes;
        public string timingSource;
    }

    [Serializable]
    public sealed class ProfilerUnsupportedItem
    {
        public string feature;
        public string reason;

        public ProfilerUnsupportedItem()
        {
        }

        public ProfilerUnsupportedItem(string feature, string reason)
        {
            this.feature = feature;
            this.reason = reason;
        }
    }
}
