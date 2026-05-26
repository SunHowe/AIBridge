namespace AIBridgeCLI.Core
{
    public static class RuntimeTransportClientFactory
    {
        public static IRuntimeTransportClient Create(RuntimeTransportOptions options)
        {
            switch (options.Kind)
            {
                case RuntimeTransportKind.Http:
                    return new HttpRuntimeTransportClient(options);
                case RuntimeTransportKind.Adb:
                    return new AdbRuntimeTransportClient(options);
                case RuntimeTransportKind.File:
                default:
                    return new FileRuntimeTransportClient(options.RuntimeDirectory);
            }
        }
    }
}
